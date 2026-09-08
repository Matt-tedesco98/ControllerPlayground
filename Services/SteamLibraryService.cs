using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using System.Diagnostics;
using SteamKit2.Internal;

namespace ControllerPlayground.Services {
    public sealed class SteamLibraryService {
        private readonly SteamSessionService _session;

        public SteamLibraryService(SteamSessionService session) {
            _session = session;

            _session.CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
        }

        private IReadOnlyList<SteamApps.LicenseListCallback.License> _licenses = Array.Empty<SteamApps.LicenseListCallback.License>();
        public IReadOnlyList<SteamApps.LicenseListCallback.License> Licenses => _licenses;
        private IReadOnlyCollection<uint> _ownedAppIds = Array.Empty<uint>();
        public IReadOnlyCollection<uint> OwnedAppIds => _ownedAppIds;

        public event Action<int>? LicenseListReceived;

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
        }

        private void OnLicenseList(SteamApps.LicenseListCallback callback) {
            if(callback.Result != EResult.OK) {
                Debug.WriteLine($"Steam license list failed: {callback.Result}");
                return;
            }
            _licenses = callback.LicenseList;
            Debug.WriteLine($"Steam licenses received: {_licenses.Count}");
            LicenseListReceived?.Invoke(callback.LicenseList.Count);
            _ = LoadOwnedAppIdsAsync();
        }
    }
}
