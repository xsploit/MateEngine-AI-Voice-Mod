using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MateEngine.AIVoiceMod
{
    public enum LlmProvider { VercelGateway, OpenRouter }
    public enum OpenRouterRoutingMode { Auto, Latency, Throughput, Pinned }
    public enum VercelRoutingMode { Auto, Latency, Throughput, Cost, Pinned }
    public enum ReplyLength { Short, Balanced, Yap }
    public enum FishTransport { WebSocket, TimestampSse, Http }
    public enum FishChunkingStrategy { Eager, FastPhrase, SafePhrase }
    public enum RemoteTtsMode { LiveBridge = 0, SentenceChunks = 1, FullResponse = 2, EarlyChunks = 3 }

    [Serializable]
    public sealed class ProviderKeys
    {
        public string openRouterApiKey = "";
        public string vercelApiKey = "";
        public string fishAudioApiKey = "";
        public string openAiByokApiKey = "";
    }

    [Serializable]
    public sealed class VoiceBinding
    {
        public string provider = "fish-speech";
        public string voiceId = "";
        public string modelId = "s2.1-pro-free";
        public string label = "Current Fish voice";
    }

    [Serializable]
    public sealed class Persona
    {
        public string id = "mate";
        public string name = "Mate";
        public string systemPrompt = "You are Mate, an animated desktop companion.";
        public string description = "An animated desktop companion.";
        public string userNickname = "";
        public VoiceBinding voice;

        public string BuildSystemMessage(ReplyLength replyLength, string runtimeSituation)
        {
            var blocks = new List<string> { "You are " + name + ". Stay in character and reply naturally." };
            if (!string.IsNullOrWhiteSpace(description)) blocks.Add("Character description: " + description.Trim());
            if (!string.IsNullOrWhiteSpace(systemPrompt)) blocks.Add(systemPrompt.Trim());
            if (!string.IsNullOrWhiteSpace(userNickname)) blocks.Add("The local controller nickname is \"" + userNickname.Trim() + "\". Talk directly to that person in second person.");
            blocks.Add(replyLength == ReplyLength.Short ? "Keep replies short and direct." : replyLength == ReplyLength.Yap ? "You may give a long, expressive reply." : "Use a balanced conversational reply length.");
            if (!string.IsNullOrWhiteSpace(runtimeSituation)) blocks.Add(runtimeSituation.Trim());
            return string.Join("\n\n", blocks.ToArray());
        }
    }

    [Serializable]
    public sealed class ModSettings
    {
        public int schemaVersion = 1;
        public bool enabled = true;
        public int proxyPort = 13333;
        public LlmProvider llmProvider = LlmProvider.VercelGateway;
        public string model = "google/gemini-3.1-flash-lite";
        public OpenRouterRoutingMode openRouterRoutingMode = OpenRouterRoutingMode.Latency;
        public string[] openRouterProviderSlugs = new string[0];
        public bool openRouterAllowFallbacks = true;
        public VercelRoutingMode vercelRoutingMode = VercelRoutingMode.Auto;
        public string[] vercelProviderSlugs = new[] { "fireworks" };
        public bool vercelAllowFallbacks = true;
        public ReplyLength replyLength = ReplyLength.Short;
        public double temperature = 0.95;
        public int maxTokens = 920;
        public string runtimeSituation = "";
        public bool autoSpeak = true;
        public RemoteTtsMode remoteTtsMode = RemoteTtsMode.LiveBridge;
        public FishTransport fishTransport = FishTransport.WebSocket;
        public string fishFormat = "pcm";
        public int fishSampleRate = 24000;
        public string fishModel = "s2.1-pro-free";
        public string fishVoiceId = "";
        public string fishVoiceScope = "mine";
        public string fishLatency = "balanced";
        public bool fishConditionOnPreviousChunks = true;
        public int fishChunkLength = 160;
        public FishChunkingStrategy fishChunkingStrategy = FishChunkingStrategy.SafePhrase;
        public float speechSpeed = 1f;
        public float ttsVolume = 1f;
        public string lipSyncMode = "hybrid";
        public float lipSyncSmoothing = 0.44f;
        public float lipSyncGain = 1f;
        public float lipSyncVolumeInfluence = 1f;
        public ProviderKeys keys = new ProviderKeys();
        public string activePersonaId = "mate";
        public List<Persona> personas = new List<Persona> { new Persona() };

        public Persona ActivePersona
        {
            get
            {
                var found = personas.Find(x => x != null && x.id == activePersonaId);
                return found ?? (personas.Count > 0 ? personas[0] : new Persona());
            }
        }

        public static ModSettings Load(string path)
        {
            if (!File.Exists(path)) return new ModSettings();
            var value = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(path)) ?? new ModSettings();
            value.Normalize();
            return value;
        }

        public void Save(string path)
        {
            Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        public void Normalize()
        {
            if (keys == null) keys = new ProviderKeys();
            if (personas == null) personas = new List<Persona>();
            personas.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.name));
            if (personas.Count == 0) personas.Add(new Persona());
            if (personas.Find(x => x.id == activePersonaId) == null) activePersonaId = personas[0].id;
            proxyPort = Math.Max(1024, Math.Min(65535, proxyPort));
            temperature = Math.Max(0, Math.Min(2, temperature));
            maxTokens = Math.Max(80, Math.Min(4000, maxTokens));
            fishSampleRate = Math.Max(16000, Math.Min(48000, fishSampleRate));
            fishChunkLength = Math.Max(100, Math.Min(300, fishChunkLength));
            speechSpeed = Math.Max(0.5f, Math.Min(2f, speechSpeed));
            ttsVolume = Math.Max(0f, Math.Min(2f, ttsVolume));
            lipSyncMode = string.Equals(lipSyncMode, "direct", StringComparison.OrdinalIgnoreCase) ? "direct" : "hybrid";
            lipSyncSmoothing = Math.Max(0f, Math.Min(0.9f, lipSyncSmoothing));
            lipSyncGain = Math.Max(0.1f, Math.Min(2f, lipSyncGain));
            lipSyncVolumeInfluence = Math.Max(0f, Math.Min(2f, lipSyncVolumeInfluence));
        }
    }
}
