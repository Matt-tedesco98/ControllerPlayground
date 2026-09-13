using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using System.Diagnostics;
using SteamKit2.Internal;
using ControllerPlayground.Models;

namespace ControllerPlayground.Services.Steam.SteamKit {
    public sealed class SteamLibraryService {
        private readonly SteamSessionService _session;
        private readonly SteamAppInfoService _appInfoService;

        public SteamLibraryService(SteamSessionService session, SteamAppInfoService appInfoService) {

            _session = session;
            _appInfoService = appInfoService;

            _session.CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
        }

        private IReadOnlyList<SteamApps.LicenseListCallback.License> _licenses = Array.Empty<SteamApps.LicenseListCallback.License>();
        public IReadOnlyList<SteamApps.LicenseListCallback.License> Licenses => _licenses;
        private IReadOnlyCollection<uint> _ownedAppIds = Array.Empty<uint>();
        public IReadOnlyCollection<uint> OwnedAppIds => _ownedAppIds;
        private IReadOnlyList<SteamLibraryGame> _games = Array.Empty<SteamLibraryGame>();
        public IReadOnlyList<SteamLibraryGame> Games => _games;

        public event Action<int>? LibraryLoaded;
        public event Action<int>? LicenseListReceived;
        public event Action<int>? OwnedAppIdsLoaded;

        private async Task LoadOwnedAppIdsAsync() {
            var packagedRequests = _licenses.Select(License => new SteamApps.PICSRequest(License.PackageID, License.AccessToken)).ToList();

            var results = await _session.AppsHandler.PICSGetProductInfo(Array.Empty<SteamApps.PICSRequest>(), packagedRequests);

            var appIds = new HashSet<uint>();

            foreach (var result in results.Results) {
                foreach (var package in result.Packages.Values) {
                    foreach (var appIdValue in package.KeyValues["appids"].Children) {
                        uint appId = appIdValue.AsUnsignedInteger();
                        if (appId != 0) {
                            appIds.Add(appId);
                        }
                    }
                }
            }
            _ownedAppIds = appIds;
            Debug.WriteLine($"Steam app IDs from licenses: {_ownedAppIds.Count}");
            OwnedAppIdsLoaded?.Invoke(_ownedAppIds.Count);
        }

        private void OnLicenseList(SteamApps.LicenseListCallback callback) {
            if (callback.Result != EResult.OK) {
                Debug.WriteLine($"Steam license list failed: {callback.Result}");
                return;
            }
            _licenses = callback.LicenseList;
            Debug.WriteLine($"Steam licenses received: {_licenses.Count}");
            LicenseListReceived?.Invoke(callback.LicenseList.Count);
        }

        public async Task LoadFullLibraryAsync() {
            if (_games.Count > 0) {
                Debug.WriteLine($"Steam library already loaded: {_games.Count}");
                return;
            }

            if (_licenses.Count == 0) {
                Debug.WriteLine("Steam full library requested before liceses were received.");
                return;
            }

            if (_ownedAppIds.Count == 0) {
                await LoadOwnedAppIdsAsync();
            }

            _games = (await GetGamesAsync(OwnedAppIds)).OrderBy(game => game.Name).ToList();

            Debug.WriteLine($"Steam library games loaded: {_games.Count}");
            
            LibraryLoaded?.Invoke(_games.Count);
        }

        public async Task<IReadOnlyList<SteamLibraryGame>> GetGamesAsync(IEnumerable<uint> appIds) {
            IReadOnlyList<SteamAppInfo> apps = await _appInfoService.GetAppInfoAsync(appIds);

            return apps.Where(app => string.Equals(app.Type, "game", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(app.Name)).Select(app => new SteamLibraryGame {
                AppId = app.AppId,
                Name = app.Name,
                LibraryCapsuleUrl =
                !string.IsNullOrWhiteSpace(app.LibraryCapsulePath)
                    ? $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/{app.LibraryCapsulePath}"
                    : $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/library_600x900_2x.jpg",

                HeroImageUrl = !string.IsNullOrWhiteSpace(app.LibraryHeroPath)
                    ? $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/{app.LibraryHeroPath}"
                    : $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/library_hero.jpg",

                LibraryLogoUrl = !string.IsNullOrWhiteSpace(app.LibraryLogoPath)
                    ? $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/{app.LibraryLogoPath}"
                    : $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{app.AppId}/library_logo.jpg"
            }).ToList();
        }
    }
}
