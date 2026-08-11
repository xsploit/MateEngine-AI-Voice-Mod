using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MateEngine.AIVoiceMod
{
    public sealed class LoopbackLlmProxy : IDisposable
    {
        private readonly GatewayClient gateway;
        private readonly Func<ModSettings> settings;
        private Socket listener;
        private CancellationTokenSource lifetime;
        private Thread acceptThread;

        public event Action AssistantStreamStarted;
        public event Action<string> AssistantDelta;
        public event Action<string> AssistantStreamCompleted;
        public event Action<Exception> Error;
        public int Port { get; private set; }
        public bool IsRunning { get { return listener != null; } }

        public LoopbackLlmProxy(Func<ModSettings> settingsProvider, GatewayClient gatewayClient = null)
        {
            settings = settingsProvider ?? throw new ArgumentNullException("settingsProvider");
            gateway = gatewayClient ?? new GatewayClient();
        }

        public void Start(int preferredPort)
        {
            Stop();
            lifetime = new CancellationTokenSource();
            Exception last = null;
            for (int offset = 0; offset < 10; offset++)
            {
                Socket candidate = null;
                try
                {
                    Port = preferredPort + offset;
                    candidate = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    candidate.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                    candidate.Bind(new IPEndPoint(IPAddress.Loopback, Port));
                    candidate.Listen(32);
                    listener = candidate;
                    break;
                }
                catch (SocketException ex) { last = ex; if (candidate != null) candidate.Close(); }
            }
            if (listener == null) throw new InvalidOperationException("Could not bind the Mate Engine LLM compatibility port.", last);
            var activeListener = listener;
            var token = lifetime.Token;
            acceptThread = new Thread(() => AcceptLoop(activeListener, token)) { IsBackground = true, Name = "MateEngine-AI-Voice-Proxy" };
            acceptThread.Start();
        }

        private void AcceptLoop(Socket activeListener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { var client = activeListener.Accept(); ThreadPool.QueueUserWorkItem(_ => HandleClient(client, token)); }
                catch (SocketException) { if (!token.IsCancellationRequested && ReferenceEquals(listener, activeListener)) RaiseError(new IOException("Mate Engine proxy accept failed.")); return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        private void HandleClient(Socket client, CancellationToken token)
        {
            using (client)
            using (var stream = new NetworkStream(client, false))
            {
                bool completionRequest = false;
                try
                {
                    var request = ReadRequest(stream);
                    completionRequest = request.Path.IndexOf("completion", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (completionRequest) HandleCompletion(stream, request.Body, token).GetAwaiter().GetResult();
                    else if (request.Path.IndexOf("template", StringComparison.OrdinalIgnoreCase) >= 0) WriteJson(stream, new JObject { ["template"] = "chatml" });
                    else if (request.Path.IndexOf("tokenize", StringComparison.OrdinalIgnoreCase) >= 0) WriteJson(stream, new JObject { ["tokens"] = new JArray() });
                    else if (request.Path.IndexOf("detokenize", StringComparison.OrdinalIgnoreCase) >= 0) WriteJson(stream, new JObject { ["content"] = "" });
                    else if (request.Path.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0) WriteJson(stream, new JObject { ["id_slot"] = 0, ["filename"] = "" });
                    else if (request.Path.IndexOf("props", StringComparison.OrdinalIgnoreCase) >= 0 || request.Path.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0)
                        WriteJson(stream, new JObject { ["default_generation_settings"] = new JObject { ["n_predict"] = settings().maxTokens, ["temperature"] = settings().temperature, ["top_p"] = 0.9 }, ["total_slots"] = 1, ["model"] = settings().model, ["system_prompt"] = "" });
                    else WriteJson(stream, new JObject { ["error"] = "Endpoint not found: " + request.Path }, 404);
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                    try
                    {
                        if (completionRequest)
                        {
                            var message = "Mate Engine AI error: " + SafeErrorMessage(ex);
                            if (AssistantStreamCompleted != null) AssistantStreamCompleted(message);
                            WriteJson(stream, ChatResult(message, true));
                        }
                        else WriteJson(stream, new JObject { ["error"] = SafeErrorMessage(ex) }, 500);
                    }
                    catch { }
                }
            }
        }

        private async Task HandleCompletion(NetworkStream stream, string bodyText, CancellationToken token)
        {
            var body = JObject.Parse(string.IsNullOrWhiteSpace(bodyText) ? "{}" : bodyText);
            bool wantsStream = (bool?)body["stream"] == true;
            if (IsWarmupRequest(body))
            {
                // LLMUnity calls /completion with n_predict=0 whenever the chat UI opens.
                // A local llama.cpp server uses that request only to prime its prompt cache.
                // Forwarding it to a hosted chat API instead generates a real reply to the
                // previously saved conversation, which in turn starts TTS without user input.
                WriteEmptyCompletion(stream, wantsStream);
                return;
            }

            var current = settings(); current.Normalize();
            var messages = new List<ChatMessage>
            {
                new ChatMessage { role = "system", content = current.ActivePersona.BuildSystemMessage(current.replyLength, current.runtimeSituation) },
                new ChatMessage { role = "user", content = (string)body["prompt"] ?? "" }
            };
            if (AssistantStreamStarted != null) AssistantStreamStarted();

            if (!wantsStream)
            {
                var full = await gateway.StreamChatAsync(new GatewayChatRequest { settings = current, messages = messages }, delta => { if (AssistantDelta != null) AssistantDelta(delta); }, token).ConfigureAwait(false);
                if (AssistantStreamCompleted != null) AssistantStreamCompleted(full);
                WriteJson(stream, ChatResult(full, true));
                return;
            }

            var sendLock = new object();
            var selectedKey = current.llmProvider == LlmProvider.OpenRouter ? current.keys.openRouterApiKey : current.keys.vercelApiKey;
            if (string.IsNullOrWhiteSpace(selectedKey)) throw new InvalidOperationException("The selected LLM provider API key is not set.");

            WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
            try
            {
                var responseText = await gateway.StreamChatAsync(new GatewayChatRequest { settings = current, messages = messages }, delta =>
                {
                    if (AssistantDelta != null) AssistantDelta(delta);
                    var payload = "data: " + ChatResult(delta, false).ToString(Formatting.None) + "\n\n";
                    lock (sendLock) WriteChunk(stream, payload);
                }, token).ConfigureAwait(false);
                if (AssistantStreamCompleted != null) AssistantStreamCompleted(responseText);
                lock (sendLock)
                {
                    WriteChunk(stream, "data: " + ChatResult("", true).ToString(Formatting.None) + "\n\n");
                    WriteAscii(stream, "0\r\n\r\n");
                }
            }
            catch (Exception ex)
            {
                RaiseError(ex);
                var message = "Mate Engine AI error: " + SafeErrorMessage(ex);
                if (AssistantStreamCompleted != null) AssistantStreamCompleted(message);
                lock (sendLock)
                {
                    WriteChunk(stream, "data: " + ChatResult(message, true).ToString(Formatting.None) + "\n\n");
                    WriteAscii(stream, "0\r\n\r\n");
                }
            }
        }

        internal static bool IsWarmupRequest(JObject body)
        {
            return body != null && (int?)body["n_predict"] == 0;
        }

        private static void WriteEmptyCompletion(NetworkStream stream, bool wantsStream)
        {
            if (!wantsStream)
            {
                WriteJson(stream, ChatResult("", true));
                return;
            }

            WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
            WriteChunk(stream, "data: " + ChatResult("", true).ToString(Formatting.None) + "\n\n");
            WriteAscii(stream, "0\r\n\r\n");
        }

        private static JObject ChatResult(string content, bool stop) { return new JObject { ["id_slot"] = 0, ["content"] = content ?? "", ["stop"] = stop }; }
        private static string SafeErrorMessage(Exception ex)
        {
            while (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            return string.IsNullOrWhiteSpace(ex.Message) ? "The provider request failed." : ex.Message;
        }
        private static void WriteChunk(Stream stream, string payload) { var bytes = Encoding.UTF8.GetBytes(payload); WriteAscii(stream, bytes.Length.ToString("x") + "\r\n"); stream.Write(bytes, 0, bytes.Length); WriteAscii(stream, "\r\n"); stream.Flush(); }
        private static void WriteAscii(Stream stream, string value) { var bytes = Encoding.ASCII.GetBytes(value); stream.Write(bytes, 0, bytes.Length); stream.Flush(); }

        private static void WriteJson(Stream stream, JObject body, int status = 200)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            WriteAscii(stream, "HTTP/1.1 " + status + (status == 200 ? " OK" : " Error") + "\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }

        private sealed class HttpRequest { public string Path; public string Body; }
        private static HttpRequest ReadRequest(Stream stream)
        {
            var bytes = new List<byte>(); var tail = new Queue<byte>(4); int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                bytes.Add((byte)value); tail.Enqueue((byte)value); while (tail.Count > 4) tail.Dequeue();
                if (tail.Count == 4 && Encoding.ASCII.GetString(tail.ToArray()) == "\r\n\r\n") break;
                if (bytes.Count > 1024 * 1024) throw new InvalidDataException("HTTP headers are too large.");
            }
            var header = Encoding.UTF8.GetString(bytes.ToArray());
            var lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' '); if (first.Length < 2) throw new InvalidDataException("Invalid HTTP request line.");
            int length = 0;
            foreach (var line in lines) if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(line.IndexOf(':') + 1).Trim(), out length);
            if (length > 20 * 1024 * 1024) throw new InvalidDataException("HTTP request body is too large.");
            var body = new byte[length]; int read = 0; while (read < length) { int count = stream.Read(body, read, length - read); if (count <= 0) break; read += count; }
            return new HttpRequest { Path = first[1], Body = Encoding.UTF8.GetString(body, 0, read) };
        }

        private void RaiseError(Exception ex) { if (Error != null) Error(ex); }
        public void Stop()
        {
            if (lifetime != null) { lifetime.Cancel(); lifetime.Dispose(); lifetime = null; }
            if (listener != null) { listener.Close(); listener = null; }
            acceptThread = null;
        }
        public void Dispose() { Stop(); }
    }
}
