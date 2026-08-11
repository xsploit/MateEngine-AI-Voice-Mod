using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MateEngine.AIVoiceMod
{
    public sealed class ModelInfo { public string id; public string name; public int? contextWindow; public string[] inputModalities = new string[0]; public string[] outputModalities = new string[0]; public string[] supportedParameters = new string[0]; public bool supportsStructuredOutputs; public bool supportsImplicitCaching; }
    public sealed class ProviderEndpointInfo { public string providerName; public int? status; public double? latencyP50Ms; public double? latencyP95Ms; public double? uptimeLastHour; }

    public sealed class ModelCatalogClient
    {
        public ModelCatalogClient() { }
        public Task<IList<ModelInfo>> FetchModelsAsync(LlmProvider provider, string apiKey, CancellationToken token)
        {
            var url = provider == LlmProvider.OpenRouter ? "https://openrouter.ai/api/v1/models" : "https://ai-gateway.vercel.sh/v1/models";
            return BackgroundTask.Run<IList<ModelInfo>>(() => ParseModels(JObject.Parse(RawHttpClient.Get(url, apiKey, token))), token);
        }
        public Task<IList<ProviderEndpointInfo>> FetchVercelEndpointsAsync(string model, CancellationToken token)
        {
            var parts = (model ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new ArgumentException("Vercel model ID must include creator and model.", "model");
            var url = "https://ai-gateway.vercel.sh/v1/models/" + string.Join("/", parts.Select(RawHttpClient.EscapeSegment).ToArray()) + "/endpoints";
            return BackgroundTask.Run<IList<ProviderEndpointInfo>>(() => ParseEndpoints(JObject.Parse(RawHttpClient.Get(url, null, token))), token);
        }
        internal static IList<ModelInfo> ParseModels(JObject payload)
        {
            var output = new List<ModelInfo>();
            foreach (var entry in payload["data"] as JArray ?? new JArray())
            {
                var id = (string)entry["id"]; if (string.IsNullOrWhiteSpace(id)) continue;
                var type = (string)entry["type"] ?? (string)entry["modelType"] ?? (string)entry["model_type"];
                if (!string.IsNullOrEmpty(type) && type != "language") continue;
                var inputs = Strings(entry.SelectToken("architecture.input_modalities")); var outputs = Strings(entry.SelectToken("architecture.output_modalities"));
                if (outputs.Any(x => (x == "image" || x == "video" || x == "embedding") && !outputs.Contains("text"))) continue;
                var parameters = Strings(entry["supported_parameters"]).Concat(Strings(entry.SelectToken("top_provider.supported_parameters"))).Distinct().ToArray();
                output.Add(new ModelInfo { id = id, name = (string)entry["name"], contextWindow = (int?)entry["context_length"] ?? (int?)entry["context_window"], inputModalities = inputs, outputModalities = outputs, supportedParameters = parameters, supportsStructuredOutputs = parameters.Any(x => x == "structured_outputs" || x == "json_schema" || x == "response_format"), supportsImplicitCaching = (bool?)entry["supports_implicit_caching"] == true });
            }
            return output.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
        }
        internal static IList<ProviderEndpointInfo> ParseEndpoints(JObject payload)
        {
            var array = payload.SelectToken("data.endpoints") as JArray ?? payload["endpoints"] as JArray ?? new JArray();
            var best = new Dictionary<string, ProviderEndpointInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in array)
            {
                var name = (string)entry["provider_name"] ?? (string)entry["provider"]; if (string.IsNullOrWhiteSpace(name)) continue;
                var value = new ProviderEndpointInfo { providerName = name.Trim(), status = (int?)entry["status"], latencyP50Ms = (double?)entry.SelectToken("latency_last_1h.p50"), latencyP95Ms = (double?)entry.SelectToken("latency_last_1h.p95"), uptimeLastHour = (double?)entry["uptime_last_1h"] };
                ProviderEndpointInfo old; if (!best.TryGetValue(value.providerName, out old) || (old.status != 0 && value.status == 0)) best[value.providerName] = value;
            }
            return best.Values.OrderBy(x => x.status != 0).ThenBy(x => x.providerName, StringComparer.Ordinal).ToList();
        }
        private static string[] Strings(JToken token) { return (token as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToArray(); }
    }
}
