using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;
using LLMUnity;

namespace MateEngine.AIVoiceMod
{
    [DefaultExecutionOrder(-32000)]
    public sealed class MateEngineModRuntime : MonoBehaviour
    {
        private enum SpeechEventType { Start, Delta, Complete, FishFinished, Error }
        private sealed class SpeechEvent { public SpeechEventType Type; public string Text; public Exception Exception; }
        public static MateEngineModRuntime Instance { get; private set; }
        private readonly ConcurrentQueue<SpeechEvent> events = new ConcurrentQueue<SpeechEvent>();
        private ModSettings settings;
        private string settingsPath;
        private LoopbackLlmProxy proxy;
        private FishSpeechSession fish;
        private FishTimestampSession fishTimestamp;
        private UnityPcmSpeechPlayer player;
        private CancellationTokenSource speechLifetime;
        private float nextMateBind;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureStarted();
        }

        internal static MateEngineModRuntime EnsureStarted()
        {
            if (Instance != null) return Instance;
            var host = new GameObject("MateEngine AI + Voice Runtime");
            DontDestroyOnLoad(host);
            return host.AddComponent<MateEngineModRuntime>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            settingsPath = Path.Combine(Application.persistentDataPath, "MateEngineAIVoiceSettings.json");
            settings = ModSettings.Load(settingsPath);
            player = gameObject.AddComponent<UnityPcmSpeechPlayer>(); player.Initialize(settings);
            proxy = new LoopbackLlmProxy(() => settings);
            proxy.AssistantStreamStarted += () => events.Enqueue(new SpeechEvent { Type = SpeechEventType.Start });
            proxy.AssistantDelta += text => events.Enqueue(new SpeechEvent { Type = SpeechEventType.Delta, Text = text });
            proxy.AssistantStreamCompleted += text => events.Enqueue(new SpeechEvent { Type = SpeechEventType.Complete, Text = text });
            proxy.Error += ex => events.Enqueue(new SpeechEvent { Type = SpeechEventType.Error, Exception = ex });
            if (settings.enabled) proxy.Start(settings.proxyPort);
            Debug.Log("[MateEngineAIVoice] Runtime ready. Proxy=" + (proxy.IsRunning ? ("127.0.0.1:" + proxy.Port) : "disabled") + "; settings=" + settingsPath);
        }

        private void Update()
        {
            SpeechEvent value;
            while (events.TryDequeue(out value)) ProcessSpeechEvent(value);
            if (Time.unscaledTime >= nextMateBind) { nextMateBind = Time.unscaledTime + 0.5f; BindMateEngine(); }
        }

        private void ProcessSpeechEvent(SpeechEvent value)
        {
            if (value.Type == SpeechEventType.Start) BeginFishSpeech();
            else if (value.Type == SpeechEventType.Delta) { if (fish != null) fish.PushDelta(value.Text); else if (fishTimestamp != null) fishTimestamp.PushDelta(value.Text); }
            else if (value.Type == SpeechEventType.Complete) { if (fish != null) fish.Complete(); else if (fishTimestamp != null) fishTimestamp.Complete(); }
            else if (value.Type == SpeechEventType.FishFinished)
            {
                player.EndInput();
                DisposeSpeech();
            }
            else if (value.Type == SpeechEventType.Error)
            {
                player.EndInput();
                DisposeSpeech();
                Debug.LogError("[MateEngineAIVoice] " + value.Exception);
            }
        }

        private async void BeginFishSpeech()
        {
            if (!settings.autoSpeak || string.IsNullOrWhiteSpace(settings.keys.fishAudioApiKey)) return;
            DisposeSpeech();
            player.Apply(settings); player.BeginInput();
            speechLifetime = new CancellationTokenSource();
            Action<string> finished = reason =>
            {
                Debug.Log("[MateEngineAIVoice] Fish realtime speech finished: " + reason);
                events.Enqueue(new SpeechEvent { Type = SpeechEventType.FishFinished, Text = reason });
            };
            Action<Exception> failed = ex => events.Enqueue(new SpeechEvent { Type = SpeechEventType.Error, Exception = ex });
            if (settings.fishTransport == FishTransport.TimestampSse)
            {
                fishTimestamp = new FishTimestampSession(settings, speechLifetime.Token); fishTimestamp.Audio += player.EnqueuePcm; fishTimestamp.Finished += finished; fishTimestamp.Error += failed;
                Debug.Log("[MateEngineAIVoice] Fish Timestamp SSE session ready; waiting for the complete response.");
                return;
            }
            fish = new FishSpeechSession(); fish.Audio += player.EnqueuePcm; fish.Finished += finished; fish.Error += failed;
            Debug.Log("[MateEngineAIVoice] Starting Fish realtime speech (" + settings.remoteTtsMode + ").");
            try
            {
                await fish.StartAsync(settings, speechLifetime.Token);
                Debug.Log("[MateEngineAIVoice] Fish realtime WebSocket connected.");
            }
            catch (Exception ex) { events.Enqueue(new SpeechEvent { Type = SpeechEventType.Error, Exception = ex }); }
        }

        private void BindMateEngine()
        {
            if (!settings.enabled || proxy == null || !proxy.IsRunning) return;
            foreach (var character in FindObjectsByType<LLMCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                character.remote = true; character.host = "http://127.0.0.1"; character.port = proxy.Port;
            }
            foreach (var local in FindObjectsByType<LLM>(FindObjectsInactive.Include, FindObjectsSortMode.None)) local.enabled = false;
        }

        public ModSettings Settings { get { return settings; } }
        public void SaveAndApply()
        {
            settings.Save(settingsPath); player.Apply(settings);
            if (settings.enabled && !proxy.IsRunning) proxy.Start(settings.proxyPort);
            else if (!settings.enabled && proxy.IsRunning) proxy.Stop();
            else if (settings.enabled && proxy.Port != settings.proxyPort) proxy.Start(settings.proxyPort);
            BindMateEngine();
        }

        private void DisposeSpeech()
        {
            if (speechLifetime != null) { speechLifetime.Cancel(); speechLifetime.Dispose(); speechLifetime = null; }
            if (fish != null) { fish.Dispose(); fish = null; }
            if (fishTimestamp != null) { fishTimestamp.Dispose(); fishTimestamp = null; }
        }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DisposeSpeech(); if (proxy != null) proxy.Dispose();
        }
    }
}
