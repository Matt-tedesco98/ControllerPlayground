using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ControllerPlayground.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Windows.Graphics.Imaging;
using System.Threading;

namespace ControllerPlayground.Services.Data {
    public sealed class SteamArtworkCacheService {
        private static readonly HttpClient httpClient = new();

        private readonly string _artworkFolder;

        public SteamArtworkCacheService() {
            _artworkFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerPlayground", "Artwork", "Steam");

            Directory.CreateDirectory(_artworkFolder);
        }
        public async Task<string> GetLibraryCapsuleAsync(SteamLibraryGame game) {
            string gameFolder = Path.Combine(
                _artworkFolder,
                game.AppId.ToString());

            Directory.CreateDirectory(gameFolder);

            string localPath = Path.Combine(
                gameFolder,
                "library_capsule.jpg");

            string missingPath = Path.Combine(
                gameFolder,
                "library_capsule.missing");

            if (File.Exists(localPath)) {
                return new Uri(localPath).AbsoluteUri;
            }

            if (File.Exists(missingPath)) {
                DateTime missingSince =
                    File.GetLastWriteTimeUtc(missingPath);

                if (DateTime.UtcNow - missingSince <
                    TimeSpan.FromDays(7)) {

                    return string.Empty;
                }

                File.Delete(missingPath);
            }

            if (string.IsNullOrWhiteSpace(
                game.LibraryCapsuleUrl)) {

                return string.Empty;
            }

            string[] artworkUrls = {
        game.LibraryCapsuleUrl,

        $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{game.AppId}/library_600x900_2x.jpg",

        $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{game.AppId}/library_600x900.jpg"
    };

            foreach (string artworkUrl in
                artworkUrls
                    .Where(url =>
                        !string.IsNullOrWhiteSpace(url))
                    .Distinct()) {

                try {
                    using HttpResponseMessage response =
                        await httpClient.GetAsync(artworkUrl);

                    if (!response.IsSuccessStatusCode) {
                        continue;
                    }

                    byte[] artwork =
                        await response.Content.ReadAsByteArrayAsync();

                    await File.WriteAllBytesAsync(
                        localPath,
                        artwork);

                    Debug.WriteLine(
                        $"Cached Steam capsule: {game.AppId}");

                    return new Uri(localPath).AbsoluteUri;

                } catch (Exception ex) {
                    Debug.WriteLine(
                        $"Steam capsule attempt failed: " +
                        $"{game.AppId} | {ex.Message}");
                }
            }

            await File.WriteAllTextAsync(
                missingPath,
                DateTime.UtcNow.ToString("O"));

            Debug.WriteLine(
                $"No Steam capsule available: {game.AppId}");

            return string.Empty;
        }
        public string GetLibraryCapsuleSource(SteamLibraryGame game) {
            string gameFolder = Path.Combine(_artworkFolder, game.AppId.ToString());

            string localPath = Path.Combine(gameFolder, "library_capsule.jpg");

            if (File.Exists(localPath)) {
                return new Uri(localPath).AbsoluteUri;
            }

            string missingPath = Path.Combine(gameFolder, "library_capsule.missing");

            if (File.Exists(missingPath)) {
                DateTime missingSince = File.GetLastWriteTimeUtc(missingPath);

                if (DateTime.UtcNow - missingSince < TimeSpan.FromDays(7)) {
                    return string.Empty;
                }
                File.Delete(missingPath);
            }
            return game.LibraryCapsuleUrl;
        }
        public async Task CacheLibraryCapsulesAsync(IEnumerable<SteamLibraryGame> games) {

            List<SteamLibraryGame> gameList = games.ToList();

            using SemaphoreSlim downloadGate = new SemaphoreSlim(4); // Limit to 4 concurrent downloads

            List<Task> downloads = new();

            foreach (SteamLibraryGame game in gameList) {
                await downloadGate.WaitAsync();

                downloads.Add(CacheLibraryCapsulesAsync(game, downloadGate));
            }
            await Task.WhenAll(downloads);
        }
        private async Task CacheLibraryCapsulesAsync(SteamLibraryGame game, SemaphoreSlim downloadGate) {
            try {
                await GetLibraryCapsuleAsync(game);
            } finally {
                downloadGate.Release();
            }
        }
    }
}
