using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MateEngine.AIVoiceMod
{
    public sealed class FishSpeechSession : IDisposable
    {
        private sealed class WorkItem { public string Text; public bool Stop; }
        private readonly FishRealtimeClient client;
        private readonly ConcurrentQueue<WorkItem> queue = new ConcurrentQueue<WorkItem>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private CancellationTokenSource lifetime;
        private Task sender;
        private SpeechTextChunker chunker;
        private RemoteTtsMode pacing;
        private StringBuilder fullResponse;

        public event Action<byte[]> Audio { add { client.Audio += value; } remove { client.Audio -= value; } }
        public event Action<string> Finished { add { client.Finished += value; } remove { client.Finished -= value; } }
        public event Action<Exception> Error { add { client.Error += value; } remove { client.Error -= value; } }

        public FishSpeechSession(FishRealtimeClient realtimeClient = null) { client = realtimeClient ?? new FishRealtimeClient(); }

        public async Task StartAsync(ModSettings settings, CancellationToken token)
        {
            DisposeLifetime();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            pacing = settings.remoteTtsMode;
            fullResponse = pacing == RemoteTtsMode.FullResponse ? new StringBuilder() : null;
            var strategy = pacing == RemoteTtsMode.SentenceChunks ? FishChunkingStrategy.SafePhrase : pacing == RemoteTtsMode.EarlyChunks ? FishChunkingStrategy.Eager : settings.fishChunkingStrategy;
            chunker = fullResponse == null ? new SpeechTextChunker(strategy, settings.fishChunkLength) : null;
            await client.ConnectAsync(new FishRealtimeOptions
            {
                apiKey = settings.keys.fishAudioApiKey, model = settings.fishModel,
                voiceId = settings.ActivePersona.voice != null ? settings.ActivePersona.voice.voiceId : settings.fishVoiceId,
                sampleRate = settings.fishSampleRate, chunkLength = settings.fishChunkLength,
                latency = settings.fishLatency, conditionOnPreviousChunks = settings.fishConditionOnPreviousChunks,
                speed = settings.speechSpeed
            }, lifetime.Token).ConfigureAwait(false);
            sender = SenderLoopAsync(lifetime.Token);
        }

        public void PushDelta(string delta)
        {
            if (fullResponse != null) { fullResponse.Append(delta); return; }
            if (chunker == null) return;
            foreach (var text in chunker.Push(delta)) Enqueue(text, false);
        }

        public void Complete()
        {
            if (fullResponse != null)
            {
                var text = fullResponse.ToString(); if (!string.IsNullOrWhiteSpace(text)) Enqueue(text, false);
                Enqueue(null, true); fullResponse = null; return;
            }
            if (chunker == null) return;
            foreach (var text in chunker.Complete()) Enqueue(text, false);
            Enqueue(null, true);
        }

        private void Enqueue(string text, bool stop)
        {
            queue.Enqueue(new WorkItem { Text = text, Stop = stop });
            signal.Release();
        }

        private async Task SenderLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await signal.WaitAsync(token).ConfigureAwait(false);
                    WorkItem item;
                    while (queue.TryDequeue(out item))
                    {
                        if (!string.IsNullOrEmpty(item.Text)) { await client.SendTextAsync(item.Text, token).ConfigureAwait(false); await client.FlushAsync(token).ConfigureAwait(false); }
                        if (item.Stop) { await client.StopAsync(token).ConfigureAwait(false); return; }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void DisposeLifetime()
        {
            if (lifetime != null) { lifetime.Cancel(); lifetime.Dispose(); lifetime = null; }
            WorkItem ignored; while (queue.TryDequeue(out ignored)) { }
            chunker = null; sender = null;
            fullResponse = null;
        }

        public void Dispose() { DisposeLifetime(); client.Dispose(); signal.Dispose(); }
    }
}
