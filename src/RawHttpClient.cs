using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MateEngine.AIVoiceMod
{
    internal static class RawHttpClient
    {
        public static string Get(string url, string bearer, CancellationToken token)
        {
            using (var response = Send("GET", url, bearer, "application/json", null, null, token))
            using (var reader = new StreamReader(response.Body, Encoding.UTF8))
            {
                var body = reader.ReadToEnd();
                if (response.Status < 200 || response.Status >= 300) throw new InvalidOperationException(response.Status + " " + body);
                return body;
            }
        }

        public static string PostSse(string url, string bearer, string json, Action<string> onData, CancellationToken token)
        {
            return PostSse(url, bearer, json, null, onData, token);
        }

        public static string PostSse(string url, string bearer, string json, IDictionary<string, string> headers, Action<string> onData, CancellationToken token)
        {
            var payload = Encoding.UTF8.GetBytes(json ?? "{}");
            using (var response = Send("POST", url, bearer, "text/event-stream", payload, headers, token))
            using (var reader = new StreamReader(response.Body, Encoding.UTF8))
            {
                if (response.Status < 200 || response.Status >= 300) throw new InvalidOperationException(response.Status + " " + reader.ReadToEnd());
                var data = new StringBuilder();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var line = reader.ReadLine();
                    if (line == null) break;
                    if (line.Length == 0)
                    {
                        if (data.Length > 0) { if (onData != null) onData(data.ToString()); data.Length = 0; }
                        continue;
                    }
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line.Substring(5).TrimStart());
                }
                if (data.Length > 0 && onData != null) onData(data.ToString());
                return "";
            }
        }

        public static string EscapeSegment(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            var output = new StringBuilder(bytes.Length);
            const string hex = "0123456789ABCDEF";
            foreach (var b in bytes)
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') output.Append(c);
                else { output.Append('%'); output.Append(hex[b >> 4]); output.Append(hex[b & 15]); }
            }
            return output.ToString();
        }

        private static Response Send(string method, string url, string bearer, string accept, byte[] body, IDictionary<string, string> extraHeaders, CancellationToken token)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" && uri.Scheme != "http") throw new NotSupportedException("Only HTTP(S) endpoints are supported.");
            int port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            var client = new TcpClient();
            try
            {
                using (token.Register(() => { try { client.Close(); } catch { } })) client.Connect(uri.Host, port);
                Stream active = client.GetStream();
                if (uri.Scheme == "https")
                {
                    var tls = new SslStream(active, false);
                    tls.AuthenticateAsClient(uri.Host);
                    active = tls;
                }
                var request = new StringBuilder();
                request.Append(method).Append(' ').Append(string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                request.Append("Host: ").Append(uri.Host).Append(uri.IsDefaultPort ? "" : ":" + port).Append("\r\n");
                request.Append("Accept: ").Append(accept ?? "application/json").Append("\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
                if (!string.IsNullOrWhiteSpace(bearer)) request.Append("Authorization: Bearer ").Append(bearer.Trim()).Append("\r\n");
                if (extraHeaders != null) foreach (var pair in extraHeaders) if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null) request.Append(pair.Key.Trim()).Append(": ").Append(pair.Value.Trim()).Append("\r\n");
                if (body != null) request.Append("Content-Type: application/json\r\nContent-Length: ").Append(body.Length).Append("\r\n");
                request.Append("\r\n");
                var headers = Encoding.ASCII.GetBytes(request.ToString());
                active.Write(headers, 0, headers.Length);
                if (body != null) active.Write(body, 0, body.Length);
                active.Flush();

                var responseHeader = ReadHeaders(active, token);
                var lines = responseHeader.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var statusParts = lines[0].Split(' ');
                int status;
                if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out status)) throw new InvalidDataException("Invalid HTTP status line: " + lines[0]);
                bool chunked = false;
                long? contentLength = null;
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':'); if (colon <= 0) continue;
                    var name = lines[i].Substring(0, colon).Trim(); var value = lines[i].Substring(colon + 1).Trim();
                    if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) chunked = true;
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) { long parsed; if (long.TryParse(value, out parsed)) contentLength = parsed; }
                }
                Stream responseBody = chunked ? (Stream)new ChunkedStream(active) : contentLength.HasValue ? new LimitedStream(active, contentLength.Value) : active;
                return new Response(status, responseBody, active, client);
            }
            catch { client.Close(); throw; }
        }

        private static string ReadHeaders(Stream input, CancellationToken token)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 65536)
            {
                token.ThrowIfCancellationRequested();
                int value = input.ReadByte(); if (value < 0) throw new EndOfStreamException(); bytes.Add((byte)value);
                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new InvalidDataException("HTTP response headers are too large.");
        }

        private sealed class Response : IDisposable
        {
            public readonly int Status; public readonly Stream Body; private readonly Stream transport; private readonly TcpClient client;
            public Response(int status, Stream body, Stream active, TcpClient tcp) { Status = status; Body = body; transport = active; client = tcp; }
            public void Dispose() { try { Body.Dispose(); } catch { } if (!ReferenceEquals(Body, transport)) try { transport.Dispose(); } catch { } try { client.Close(); } catch { } }
        }

        private sealed class LimitedStream : Stream
        {
            private readonly Stream inner; private long remaining;
            public LimitedStream(Stream value, long count) { inner = value; remaining = count; }
            public override int Read(byte[] buffer, int offset, int count) { if (remaining <= 0) return 0; int wanted = (int)Math.Min(count, remaining); int read = inner.Read(buffer, offset, wanted); remaining -= read; return read; }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        private sealed class ChunkedStream : Stream
        {
            private readonly Stream inner; private int remaining; private bool ended;
            public ChunkedStream(Stream value) { inner = value; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (ended) return 0;
                if (remaining == 0)
                {
                    var line = ReadAsciiLine(inner); int semicolon = line.IndexOf(';'); if (semicolon >= 0) line = line.Substring(0, semicolon);
                    if (!int.TryParse(line.Trim(), System.Globalization.NumberStyles.HexNumber, null, out remaining)) throw new InvalidDataException("Invalid chunk length.");
                    if (remaining == 0) { ended = true; while (ReadAsciiLine(inner).Length > 0) { } return 0; }
                }
                int read = inner.Read(buffer, offset, Math.Min(count, remaining)); if (read <= 0) throw new EndOfStreamException(); remaining -= read;
                if (remaining == 0) { if (inner.ReadByte() != 13 || inner.ReadByte() != 10) throw new InvalidDataException("Invalid chunk terminator."); }
                return read;
            }
            private static string ReadAsciiLine(Stream stream) { var bytes = new List<byte>(); while (true) { int value = stream.ReadByte(); if (value < 0) throw new EndOfStreamException(); if (value == 13) { if (stream.ReadByte() != 10) throw new InvalidDataException("Invalid HTTP line ending."); return Encoding.ASCII.GetString(bytes.ToArray()); } bytes.Add((byte)value); } }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
