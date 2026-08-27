using ControllerPlayground.Models;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.UserProfile;
using System.Collections.Generic;

namespace ControllerPlayground.Services {
    public class SteamService : ISteamService {

        private readonly HttpClient HttpClient = new();
        public async Task<IReadOnlyList<ActivityFeedItem>> GetGameActivityAsync(uint appId, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            
            string url =
            $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/" +
            $"?appid={appId}&count=10&maxlength=300&format=json";

            using HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            SteamNewsResponse? steamResponse = await JsonSerializer.DeserializeAsync<SteamNewsResponse>(stream, cancellationToken: cancellationToken);

            List<ActivityFeedItem> items = new();

            if (steamResponse == null) 
                return items;

            foreach (SteamNewsItem newsItem in steamResponse.AppNews.NewsItems) { 
                items.Add(new ActivityFeedItem { 
                    Title = newsItem.Title,
                    Subtitle = newsItem.FeedLabel,
                    PublishedAt = DateTimeOffset.FromUnixTimeSeconds(newsItem.Date).DateTime
                });
            }
            return items;
        }
    }
}
