using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MateEngine.AIVoiceMod
{
    public sealed class FishRealtimeOptions
    {
        public string apiKey;
        public string model = "s2.1-pro-free";
        public string voiceId = "";
        public int sampleRate = 24000;
        public int chunkLength = 160;
        public string latency = "balanced";
        public bool conditionOnPreviousChunks = true;
        public float speed = 1f;
        public Uri endpoint = new Uri("wss://api.fish.audio/v1/tts/live");
    }

    public sealed class FishRealtimeClient : IDisposable
    {
        private FishSocketConnection socket;
        private CancellationTokenSource lifetime;
        public event Action<byte[]> Audio;
        public event Action<string> Finished;
        public event Action<Exception> Error;

        public async Task ConnectAsync(FishRealtimeOptions options, CancellationToken token)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (string.IsNullOrWhiteSpace(options.apiKey)) throw new InvalidOperationException("Fish Audio API key is not set.");
            DisposeSocket();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            socket = new FishSocketConnection(HandleFrame, HandleClosed, HandleError);
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + options.apiKey.Trim(),
                ["model"] = string.IsNullOrWhiteSpace(options.model) ? "s2.1-pro-free" : options.model.Trim()
            };
            await BackgroundTask.Run(() => socket.Connect(options.endpoint, headers, lifetime.Token), lifetime.Token).ConfigureAwait(false);
            var request = new Dictionary<string, object>
            {
                ["text"] = "", ["format"] = "pcm", ["sample_rate"] = options.sampleRate,
                ["chunk_length"] = options.chunkLength, ["min_chunk_length"] = 20,
                ["latency"] = options.latency ?? "balanced", ["normalize"] = true,
                ["condition_on_previous_chunks"] = options.conditionOnPreviousChunks,
                ["prosody"] = new Dictionary<string, object> { ["speed"] = options.speed, ["volume"] = 0 }
            };
            if (!string.IsNullOrWhiteSpace(options.voiceId)) request["reference_id"] = options.voiceId.Trim();
            await SendAsync(new Dictionary<string, object> { ["event"] = "start", ["request"] = request }, token).ConfigureAwait(false);
        }

        public Task SendTextAsync(string text, CancellationToken token) { return SendAsync(new Dictionary<string, object> { ["event"] = "text", ["text"] = text ?? "" }, token); }
        public Task FlushAsync(CancellationToken token) { return SendAsync(new Dictionary<string, object> { ["event"] = "flush" }, token); }
        public Task StopAsync(CancellationToken token) { return SendAsync(new Dictionary<string, object> { ["event"] = "stop" }, token); }

        private Task SendAsync(IDictionary<string, object> value, CancellationToken token)
        {
            if (socket == null || !socket.IsOpen) throw new InvalidOperationException("Fish WebSocket is not open.");
            var payload = FishMessagePack.Encode(value);
            return BackgroundTask.Run(() => socket.SendBinary(payload, token), token);
        }

        private void HandleFrame(byte[] payload)
        {
            try
            {
                var map = FishMessagePack.DecodeMap(payload);
                object eventValue; map.TryGetValue("event", out eventValue);
                var eventName = eventValue as string;
                if (eventName == "audio")
                {
                    object audio; if (map.TryGetValue("audio", out audio) && audio is byte[] && Audio != null) Audio((byte[])audio);
                }
                else if (eventName == "finish")
                {
                    object reasonValue; map.TryGetValue("reason", out reasonValue);
                    object messageValue; map.TryGetValue("message", out messageValue);
                    var reason = reasonValue as string ?? "finish";
                    if (reason == "error") throw new InvalidOperationException(messageValue as string ?? "Fish WebSocket error.");
                    if (Finished != null) Finished(reason);
                }
            }
            catch (Exception ex) { HandleError(ex); }
        }

        private void HandleClosed(string reason) { if (Finished != null) Finished(reason); }
        private void HandleError(Exception ex) { if (Error != null) Error(ex); }

        private void DisposeSocket()
        {
            if (lifetime != null) { lifetime.Cancel(); lifetime.Dispose(); lifetime = null; }
            if (socket != null) { socket.Dispose(); socket = null; }
        }

        public void Dispose() { DisposeSocket(); }
    }

    internal sealed class FishSocketConnection : IDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private readonly Action<byte[]> binary;
        private readonly Action<string> closed;
        private readonly Action<Exception> error;
        private readonly object sendGate = new object();
        private TcpClient client;
        private Stream stream;
        private Thread receiver;
        private volatile bool open;

        public bool IsOpen { get { return open; } }

        public FishSocketConnection(Action<byte[]> onBinary, Action<string> onClosed, Action<Exception> onError)
        {
            binary = onBinary;
            closed = onClosed;
            error = onError;
        }

        public void Connect(Uri endpoint, IDictionary<string, string> headers, CancellationToken token)
        {
            if (endpoint == null) throw new ArgumentNullException("endpoint");
            if (endpoint.Scheme != "wss" && endpoint.Scheme != "ws") throw new NotSupportedException("Fish WebSocket URL must use ws or wss.");
            token.ThrowIfCancellationRequested();
            int port = endpoint.IsDefaultPort ? (endpoint.Scheme == "wss" ? 443 : 80) : endpoint.Port;
            client = new TcpClient();
            using (token.Register(() => { try { client.Close(); } catch { } })) client.Connect(endpoint.Host, port);
            Stream active = client.GetStream();
            if (endpoint.Scheme == "wss")
            {
                var tls = new SslStream(active, false);
                tls.AuthenticateAsClient(endpoint.Host);
                active = tls;
            }
            stream = active;

            var nonce = RandomBytes(16);
            var key = Convert.ToBase64String(nonce);
            var host = endpoint.Host + (endpoint.IsDefaultPort ? "" : ":" + port);
            var request = new StringBuilder();
            request.Append("GET ").Append(string.IsNullOrEmpty(endpoint.PathAndQuery) ? "/" : endpoint.PathAndQuery).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(host).Append("\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n");
            request.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\nSec-WebSocket-Version: 13\r\n");
            foreach (var pair in headers) request.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            request.Append("\r\n");
            var handshake = Encoding.ASCII.GetBytes(request.ToString());
            stream.Write(handshake, 0, handshake.Length);
            stream.Flush();

            var response = ReadHttpHeaders(stream, token);
            var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || lines[0].IndexOf(" 101 ", StringComparison.Ordinal) < 0)
                throw new IOException("Fish WebSocket handshake failed: " + (lines.Length == 0 ? "empty response" : lines[0]));
            string accept = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("Sec-WebSocket-Accept:", StringComparison.OrdinalIgnoreCase)) accept = line.Substring(line.IndexOf(':') + 1).Trim();
            }
            using (var sha1 = SHA1.Create())
            {
                var expected = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
                if (!string.Equals(accept, expected, StringComparison.Ordinal)) throw new IOException("Fish WebSocket returned an invalid handshake signature.");
            }

            open = true;
            receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "MateEngine-Fish-WebSocket" };
            receiver.Start();
        }

        public void SendBinary(byte[] payload, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!open || stream == null) throw new InvalidOperationException("WebSocket is closed.");
            WriteFrame(0x2, payload ?? new byte[0]);
        }

        private void ReceiveLoop()
        {
            try
            {
                using (var message = new MemoryStream())
                {
                    int messageOpcode = 0;
                    while (open)
                    {
                        var first = ReadByteRequired(stream);
                        var second = ReadByteRequired(stream);
                        bool fin = (first & 0x80) != 0;
                        int opcode = first & 0x0f;
                        bool masked = (second & 0x80) != 0;
                        ulong length = (ulong)(second & 0x7f);
                        if (length == 126) length = ReadUInt16(stream);
                        else if (length == 127) length = ReadUInt64(stream);
                        if (length > 64UL * 1024UL * 1024UL) throw new InvalidDataException("Fish WebSocket frame is too large.");
                        var mask = masked ? ReadExact(stream, 4) : null;
                        var payload = ReadExact(stream, (int)length);
                        if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(payload[i] ^ mask[i & 3]);

                        if (opcode == 0x8) { open = false; if (closed != null) closed("close"); return; }
                        if (opcode == 0x9) { WriteFrame(0xA, payload); continue; }
                        if (opcode == 0xA) continue;
                        if (opcode == 0x1 || opcode == 0x2) { message.SetLength(0); messageOpcode = opcode; }
                        else if (opcode != 0x0) throw new InvalidDataException("Unsupported Fish WebSocket opcode " + opcode + ".");
                        message.Write(payload, 0, payload.Length);
                        if (fin)
                        {
                            if (messageOpcode == 0x2 && binary != null) binary(message.ToArray());
                            message.SetLength(0);
                            messageOpcode = 0;
                        }
                    }
                }
            }
            catch (Exception ex) { if (open && error != null) error(ex); }
            finally { open = false; }
        }

        private void WriteFrame(int opcode, byte[] payload)
        {
            lock (sendGate)
            {
                if (!open || stream == null) throw new IOException("WebSocket is closed.");
                using (var frame = new MemoryStream())
                {
                    frame.WriteByte((byte)(0x80 | opcode));
                    ulong length = (ulong)payload.Length;
                    if (length < 126) frame.WriteByte((byte)(0x80 | (byte)length));
                    else if (length <= ushort.MaxValue)
                    {
                        frame.WriteByte(0x80 | 126);
                        frame.WriteByte((byte)(length >> 8)); frame.WriteByte((byte)length);
                    }
                    else
                    {
                        frame.WriteByte(0x80 | 127);
                        for (int shift = 56; shift >= 0; shift -= 8) frame.WriteByte((byte)(length >> shift));
                    }
                    var mask = RandomBytes(4);
                    frame.Write(mask, 0, mask.Length);
                    for (int i = 0; i < payload.Length; i++) frame.WriteByte((byte)(payload[i] ^ mask[i & 3]));
                    var bytes = frame.ToArray();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static string ReadHttpHeaders(Stream input, CancellationToken token)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 65536)
            {
                token.ThrowIfCancellationRequested();
                bytes.Add((byte)ReadByteRequired(input));
                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10)
                    return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new InvalidDataException("Fish WebSocket handshake headers are too large.");
        }

        private static int ReadByteRequired(Stream input) { int value = input.ReadByte(); if (value < 0) throw new EndOfStreamException(); return value; }
        private static byte[] ReadExact(Stream input, int count) { var value = new byte[count]; int read = 0; while (read < count) { int n = input.Read(value, read, count - read); if (n <= 0) throw new EndOfStreamException(); read += n; } return value; }
        private static ulong ReadUInt16(Stream input) { return (ulong)((ReadByteRequired(input) << 8) | ReadByteRequired(input)); }
        private static ulong ReadUInt64(Stream input) { ulong value = 0; for (int i = 0; i < 8; i++) value = (value << 8) | (byte)ReadByteRequired(input); return value; }

        public void Dispose()
        {
            open = false;
            try { if (stream != null) stream.Dispose(); } catch { }
            try { if (client != null) client.Close(); } catch { }
            stream = null;
            client = null;
            receiver = null;
        }
    }
}
