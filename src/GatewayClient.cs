using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MateEngine.AIVoiceMod
{
    public sealed class ChatMessage { public string role; public string content; }
    public sealed class GatewayChatRequest { public ModSettings settings; public IList<ChatMessage> messages; }

    public sealed class GatewayClient
    {
        public GatewayClient() { }

        public Task<string> StreamChatAsync(GatewayChatRequest request, Action<string> onDelta, CancellationToken token)
        {
            if (request == null || request.settings == null) throw new ArgumentNullException("request");
            var settings = request.settings;
            var apiKey = settings.llmProvider == LlmProvider.OpenRouter ? settings.keys.openRouterApiKey : settings.keys.vercelApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("The selected LLM provider API key is not set.");
            var body = new JObject
            {
                ["model"] = settings.model,
                ["messages"] = JArray.FromObject(request.messages ?? new List<ChatMessage>()),
                ["stream"] = true,
                ["temperature"] = settings.temperature,
                ["max_tokens"] = settings.maxTokens
            };
            ApplyRouting(body, settings);
            var endpoint = settings.llmProvider == LlmProvider.OpenRouter ? "https://openrouter.ai/api/v1/chat/completions" : "https://ai-gateway.vercel.sh/v1/chat/completions";
            return BackgroundTask.Run(() =>
            {
                var result = new StringBuilder();
                RawHttpClient.PostSse(endpoint, apiKey, body.ToString(Formatting.None), data =>
                {
                    if (data == "[DONE]") return;
                    var delta = ExtractDelta(data); if (string.IsNullOrEmpty(delta)) return;
                    result.Append(delta); if (onDelta != null) onDelta(delta);
                }, token);
                return result.ToString();
            }, token);
        }

        internal static void ApplyRouting(JObject body, ModSettings settings)
        {
            if (settings.llmProvider == LlmProvider.OpenRouter)
            {
                var provider = new JObject { ["require_parameters"] = true };
                if (settings.openRouterRoutingMode == OpenRouterRoutingMode.Latency) provider["sort"] = "latency";
                else if (settings.openRouterRoutingMode == OpenRouterRoutingMode.Throughput) provider["sort"] = "throughput";
                else if (settings.openRouterRoutingMode == OpenRouterRoutingMode.Pinned && settings.openRouterProviderSlugs.Length > 0)
                {
                    provider["only"] = JArray.FromObject(settings.openRouterProviderSlugs);
                    provider["allow_fallbacks"] = settings.openRouterAllowFallbacks;
                }
                body["provider"] = provider;
                return;
            }
            var gateway = new JObject { ["caching"] = "auto" };
            if (settings.vercelRoutingMode == VercelRoutingMode.Latency) gateway["sort"] = "ttft";
            else if (settings.vercelRoutingMode == VercelRoutingMode.Throughput) gateway["sort"] = "tps";
            else if (settings.vercelRoutingMode == VercelRoutingMode.Cost) gateway["sort"] = "cost";
            else if (settings.vercelRoutingMode == VercelRoutingMode.Pinned && settings.vercelProviderSlugs.Length > 0) gateway[settings.vercelAllowFallbacks ? "order" : "only"] = JArray.FromObject(settings.vercelProviderSlugs);
            body["providerOptions"] = new JObject { ["gateway"] = gateway };
        }

        internal static string ExtractDelta(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            var parsed = JObject.Parse(json);
            return (string)parsed.SelectToken("choices[0].delta.content") ?? (string)parsed.SelectToken("choices[0].text") ?? (string)parsed.SelectToken("delta.text") ?? "";
        }
    }
}
