using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;
using System.Diagnostics;

namespace ControllerPlayground.Services.Steam.SteamKit {
    public sealed class SteamAppInfoService {
        private readonly SteamSessionService _session;

        public SteamAppInfoService(SteamSessionService session) {
            _session = session;
        }

        public async Task GetAppInfoAsync(IEnumerable<uint> appIds) {
            var requests = new List<SteamApps.PICSRequest>();
            foreach(uint appId in appIds) {
                requests.Add(new SteamApps.PICSRequest(appId));
            }

            var results = await _session.AppsHandler.PICSGetProductInfo(requests, new List<SteamApps.PICSRequest>());

            foreach(var result in results.Results) {
                foreach(var app in result.Apps.Values) {

                    string name = app.KeyValues["common"]["name"].AsString();

                    string type = app.KeyValues["common"]["type"].AsString();

                    Debug.WriteLine($"Steam App: {app.ID} | {name} | Type: {type}");
                }
            }
        }
    }
}
