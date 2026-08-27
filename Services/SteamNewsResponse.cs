using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ControllerPlayground.Services {
    internal sealed class SteamNewsResponse {
        [JsonPropertyName("appnews")]
        public SteamAppNews AppNews { get; set; } = new();
    }

    internal sealed class SteamAppNews {
        [JsonPropertyName("appid")]
        public uint AppId { get; set; }
        [JsonPropertyName("newsitems")]
        public List<SteamNewsItem> NewsItems { get; set; } = new();
    }

    internal sealed class  SteamNewsItem {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("feedlabel")]
        public string FeedLabel { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public long Date { get; set; }
    }
}
