using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace MateEngine.AIVoiceMod
{
    public sealed class FishTimestampSession : IDisposable
    {
        private readonly StringBuilder text = new StringBuilder();
        private readonly ModSettings settings;
        private readonly CancellationTokenSource lifetime;

        public event Action<byte[]> Audio;
        public event Action<string> Finished;
        public event Action<Exception> Error;

        public FishTimestampSession(ModSettings value, CancellationToken token)
        {
            settings = value;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public void PushDelta(string delta) { if (!string.IsNullOrEmpty(delta)) text.Append(delta); }

        public async void Complete()
        {
            var responseText = text.ToString();
            if (string.IsNullOrWhiteSpace(responseText)) { if (Finished != null) Finished("empty"); return; }
            try
            {
                await BackgroundTask.Run(() =>
                {
                    var body = new JObject
                    {
                        ["text"] = responseText,
                        ["reference_id"] = settings.ActivePersona.voice != null ? settings.ActivePersona.voice.voiceId : settings.fishVoiceId,
                        ["format"] = "pcm",
                        ["sample_rate"] = settings.fishSampleRate,
                        ["chunk_length"] = settings.fishChunkLength,
                        ["latency"] = settings.fishLatency,
                        ["normalize"] = true,
                        ["condition_on_previous_chunks"] = settings.fishConditionOnPreviousChunks
                    };
                    var headers = new Dictionary<string, string> { ["model"] = settings.fishModel };
                    RawHttpClient.PostSse("https://api.fish.audio/v1/tts/stream/with-timestamp", settings.keys.fishAudioApiKey, body.ToString(), headers, data =>
                    {
                        if (string.IsNullOrWhiteSpace(data)) return;
                        var value = JObject.Parse(data); var encoded = (string)value["audio_base64"];
                        if (string.IsNullOrWhiteSpace(encoded)) return;
                        var bytes = Convert.FromBase64String(encoded); if (bytes.Length > 0 && Audio != null) Audio(bytes);
                    }, lifetime.Token);
                }, lifetime.Token).ConfigureAwait(false);
                if (Finished != null) Finished("timestamp-sse");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Error != null) Error(ex); }
        }

        public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    }
}
