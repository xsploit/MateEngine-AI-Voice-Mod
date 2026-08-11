using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MateEngine.AIVoiceMod
{
    public sealed class SseEvent
    {
        public string Name;
        public string Data;
    }

    public static class SseEventReader
    {
        public static async Task ReadAsync(Stream stream, Action<SseEvent> onEvent, CancellationToken token)
        {
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true, 4096, true))
            {
                var eventName = "message";
                var data = new List<string>();
                while (!reader.EndOfStream && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Length == 0)
                    {
                        if (data.Count > 0) onEvent(new SseEvent { Name = eventName, Data = string.Join("\n", data.ToArray()) });
                        eventName = "message";
                        data.Clear();
                    }
                    else if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line.Substring(6).Trim();
                    else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line.Substring(5).TrimStart());
                }
                if (data.Count > 0) onEvent(new SseEvent { Name = eventName, Data = string.Join("\n", data.ToArray()) });
            }
        }
    }
}

