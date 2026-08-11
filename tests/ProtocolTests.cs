using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MateEngine.AIVoiceMod;
using Newtonsoft.Json.Linq;

internal static class ProtocolTests
{
    private static int failures;

    private static void Check(bool condition, string name)
    {
        if (!condition) { failures++; Console.Error.WriteLine("FAIL " + name); }
        else Console.WriteLine("PASS " + name);
    }

    public static int Main()
    {
        TestFishMessagePack();
        TestRouting();
        TestDelta();
        TestCatalogs();
        TestSse();
        TestWarmupSuppression();
        TestChunker();
        TestSpeechTuningBounds();
        TestSettingsSaveReplacement();
        return failures == 0 ? 0 : 1;
    }

    private static void TestFishMessagePack()
    {
        var source = new Dictionary<string, object>
        {
            ["event"] = "audio",
            ["audio"] = new byte[] { 0, 1, 127, 255 },
            ["request"] = new Dictionary<string, object> { ["sample_rate"] = 24000, ["normalize"] = true }
        };
        var decoded = FishMessagePack.DecodeMap(FishMessagePack.Encode(source));
        Check((string)decoded["event"] == "audio", "Fish event roundtrip");
        Check(((byte[])decoded["audio"]).Length == 4 && ((byte[])decoded["audio"])[3] == 255, "Fish binary roundtrip");
        var request = (IDictionary<string, object>)decoded["request"];
        Check((long)request["sample_rate"] == 24000 && (bool)request["normalize"], "Fish nested request roundtrip");
    }

    private static void TestRouting()
    {
        var openRouter = new ModSettings { llmProvider = LlmProvider.OpenRouter, openRouterRoutingMode = OpenRouterRoutingMode.Pinned, openRouterProviderSlugs = new[] { "fireworks", "together" }, openRouterAllowFallbacks = false };
        var body = new JObject(); GatewayClient.ApplyRouting(body, openRouter);
        Check((string)body.SelectToken("provider.only[0]") == "fireworks" && (bool)body.SelectToken("provider.allow_fallbacks") == false, "OpenRouter pinned routing");

        var vercel = new ModSettings { llmProvider = LlmProvider.VercelGateway, vercelRoutingMode = VercelRoutingMode.Cost };
        body = new JObject(); GatewayClient.ApplyRouting(body, vercel);
        Check((string)body.SelectToken("providerOptions.gateway.sort") == "cost", "Vercel cost routing");
    }

    private static void TestDelta()
    {
        Check(GatewayClient.ExtractDelta("{\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}") == "hello", "OpenAI delta extraction");
    }

    private static void TestCatalogs()
    {
        var payload = JObject.Parse("{\"data\":[{\"id\":\"text/model\",\"type\":\"language\",\"architecture\":{\"input_modalities\":[\"text\"],\"output_modalities\":[\"text\"]},\"supported_parameters\":[\"json_schema\"]},{\"id\":\"image/model\",\"architecture\":{\"output_modalities\":[\"image\"]}}]}");
        var models = ModelCatalogClient.ParseModels(payload);
        Check(models.Count == 1 && models[0].id == "text/model" && models[0].supportsStructuredOutputs, "Language catalog filtering");
        var endpoints = ModelCatalogClient.ParseEndpoints(JObject.Parse("{\"data\":{\"endpoints\":[{\"provider_name\":\"fireworks\",\"status\":2},{\"provider_name\":\"fireworks\",\"status\":0,\"latency_last_1h\":{\"p50\":20}}]}}"));
        Check(endpoints.Count == 1 && endpoints[0].status == 0 && endpoints[0].latencyP50Ms == 20, "Vercel endpoint selection");
    }

    private static void TestSse()
    {
        var bytes = Encoding.UTF8.GetBytes("event: delta\ndata: {\"text\":\"a\"}\n\nevent: done\ndata: {}\n\n");
        var names = new List<string>();
        SseEventReader.ReadAsync(new MemoryStream(bytes), value => names.Add(value.Name), CancellationToken.None).GetAwaiter().GetResult();
        Check(names.Count == 2 && names[0] == "delta" && names[1] == "done", "SSE framing");
    }

    private static void TestWarmupSuppression()
    {
        Check(LoopbackLlmProxy.IsWarmupRequest(JObject.Parse("{\"n_predict\":0,\"stream\":true}")), "LLMUnity warmup suppression");
        Check(!LoopbackLlmProxy.IsWarmupRequest(JObject.Parse("{\"n_predict\":256,\"stream\":true}")), "User completion is not suppressed");
    }

    private static void TestChunker()
    {
        var chunker = new SpeechTextChunker(FishChunkingStrategy.SafePhrase, 160);
        Check(chunker.Push("Hello there. ").Count == 1, "Fish safe sentence flush");
        Check(chunker.Push("unfinished").Count == 0 && chunker.Complete().Count == 1, "Fish final flush");
    }

    private static void TestSettingsSaveReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mateengine-aivoice-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var first = new ModSettings { model = "first/model" };
            first.Save(path);
            var second = new ModSettings { model = "second/model" };
            second.Save(path);
            Check(ModSettings.Load(path).model == "second/model", "Settings save replaces current file");
            Check(File.Exists(path + ".bak") && ModSettings.Load(path + ".bak").model == "first/model", "Settings save preserves backup");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void TestSpeechTuningBounds()
    {
        var value = new ModSettings
        {
            ttsVolume = 9f,
            lipSyncMode = "unknown",
            lipSyncSmoothing = 1f,
            lipSyncGain = 0f,
            lipSyncVolumeInfluence = -1f
        };
        value.Normalize();
        Check(value.ttsVolume == 2f, "Playback volume follows supported bounds");
        Check(value.lipSyncMode == "hybrid", "Unknown lip sync mode falls back to hybrid");
        Check(value.lipSyncSmoothing == 0.9f && value.lipSyncGain == 0.1f && value.lipSyncVolumeInfluence == 0f, "Lip sync tuning follows supported bounds");
    }
}
