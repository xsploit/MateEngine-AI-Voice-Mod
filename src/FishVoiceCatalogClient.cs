using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MateEngine.AIVoiceMod
{
    public sealed class FishVoiceInfo { public string id; public string title; public string author; }
    public sealed class FishVoiceCatalogClient
    {
        public FishVoiceCatalogClient() { }
        public Task<IList<FishVoiceInfo>> FetchAsync(string apiKey, bool selfOnly, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Fish Audio API key is not set.");
            var url = "https://api.fish.audio/model?page_size=100&page_number=1&self=" + (selfOnly ? "true" : "false");
            return BackgroundTask.Run<IList<FishVoiceInfo>>(() => Parse(RawHttpClient.Get(url, apiKey, token)), token);
        }
        internal static IList<FishVoiceInfo> Parse(string json)
        {
            var payload = JToken.Parse(json); var array = payload["items"] as JArray ?? payload["models"] as JArray ?? payload as JArray ?? new JArray(); var output = new List<FishVoiceInfo>();
            foreach (var item in array) { var id = (string)item["_id"] ?? (string)item["id"]; if (string.IsNullOrWhiteSpace(id)) continue; output.Add(new FishVoiceInfo { id = id, title = (string)item["title"] ?? (string)item["name"] ?? id, author = (string)item.SelectToken("author.nickname") ?? (string)item["author_name"] ?? "" }); }
            return output;
        }
    }
}
