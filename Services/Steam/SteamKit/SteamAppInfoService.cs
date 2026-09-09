using ControllerPlayground.Models;
using SteamKit2;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ControllerPlayground.Services.Steam.SteamKit {
    public sealed class SteamAppInfoService {
        private readonly SteamSessionService _session;

        public SteamAppInfoService(SteamSessionService session) {
            _session = session;
        }

        public async Task<IReadOnlyList<SteamAppInfo>> GetAppInfoAsync(IEnumerable<uint> appIds) {
            var requests = new List<SteamApps.PICSRequest>();
            foreach (uint appId in appIds) {
                requests.Add(new SteamApps.PICSRequest(appId));
            }

            var results = await _session.AppsHandler.PICSGetProductInfo(requests, new List<SteamApps.PICSRequest>());

            List<SteamAppInfo> apps = new();

            foreach (var result in results.Results) {
                foreach (var app in result.Apps.Values) {

                    string name = app.KeyValues["common"]["name"].AsString();

                    string type = app.KeyValues["common"]["type"].AsString();

                    string libraryCapsulePath = app.KeyValues["common"]
                        ["library_assets_full"]
                        ["library_capsule"]
                        ["image2x"]
                        ["english"].AsString();

                    if (string.IsNullOrWhiteSpace(libraryCapsulePath)) {
                        libraryCapsulePath = app.KeyValues["common"]
                            ["library_assets_full"]
                            ["library_capsule"]
                            ["image"]
                            ["english"].AsString();
                    }

                    apps.Add(new SteamAppInfo {
                        AppId = app.ID,
                        Name = name,
                        Type = type,
                        LibraryCapsulePath = libraryCapsulePath
                    });
                }
            }
            return apps;
        }
    }
}
