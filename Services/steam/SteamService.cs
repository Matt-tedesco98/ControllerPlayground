using ControllerPlayground.Models;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ControllerPlayground.Services.Steam {
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
        public async Task<SteamStoreDetails> GetGameStoreDetailsAsync(
    uint appId,
    CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            SteamStoreDetails details = new();

            string url =
                $"https://store.steampowered.com/api/appdetails" +
                $"?appids={appId}&l=english";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    url,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using JsonDocument document =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

            string appIdKey =
                appId.ToString();

            if (!document.RootElement.TryGetProperty(
                    appIdKey,
                    out JsonElement appEntry)) {
                return details;
            }

            if (!appEntry.TryGetProperty(
                    "success",
                    out JsonElement successElement) ||
                !successElement.GetBoolean()) {
                return details;
            }

            if (!appEntry.TryGetProperty(
                    "data",
                    out JsonElement dataElement)) {
                return details;
            }

            if (dataElement.TryGetProperty(
                    "short_description",
                    out JsonElement descriptionElement)) {
                details.Description =
                    descriptionElement.GetString()
                    ?? string.Empty;
            }

            if (dataElement.TryGetProperty(
                    "genres",
                    out JsonElement genresElement)) {
                foreach (JsonElement genre in
                         genresElement.EnumerateArray()) {
                    if (!genre.TryGetProperty(
                            "description",
                            out JsonElement nameElement)) {
                        continue;
                    }

                    string? name =
                        nameElement.GetString();

                    if (!string.IsNullOrWhiteSpace(name)) {
                        details.Genres.Add(name);
                    }
                }
            }

            if (dataElement.TryGetProperty("controller_support", out JsonElement controllerElement)) {
                string controllerSupport = controllerElement.GetString() ?? string.Empty;
                details.ControllerSupport = controllerSupport.ToLowerInvariant()
                    switch {
                        "full" => "Full Controller Support",
                        "partial" => "Partial Controller Support",
                        "none" => "No Controller Support",
                        _ => controllerSupport
                    };
            }
            return details;
        }
    }
}
