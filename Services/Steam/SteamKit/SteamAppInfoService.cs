using ControllerPlayground.Models;
using SteamKit2;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System;

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

                    string libraryHeroPath = app.KeyValues["common"]
                        ["library_assets_full"]
                        ["library_hero"]
                        ["image2x"]
                        ["english"].AsString();

                    if (string.IsNullOrWhiteSpace(libraryHeroPath)) {
                        libraryHeroPath = app.KeyValues["common"]
                            ["library_assets_full"]
                            ["library_hero"]
                            ["image"]
                            ["english"].AsString();
                    }

                    string libraryLogoPath = app.KeyValues["common"]
                        ["library_assets_full"]
                        ["library_logo"]
                        ["image2x"]
                        ["english"].AsString();
                    if (string.IsNullOrWhiteSpace(libraryLogoPath)) {
                        libraryLogoPath = app.KeyValues["common"]
                            ["library_assets_full"]
                            ["library_logo"]
                            ["image"]
                            ["english"].AsString();
                    }

                    KeyValue common = app.KeyValues["common"];
                    string developer = GetAssociationNames(common, "developer");
                    if (string.IsNullOrWhiteSpace(developer)) {
                        developer = app.KeyValues["extended"]["developer"].AsString();
                    }

                    string publisher = GetAssociationNames(common, "publisher");
                    if (string.IsNullOrWhiteSpace(publisher)) {
                        publisher = app.KeyValues["extended"]["publisher"].AsString();
                    }

                    string releaseDate = string.Empty;
                    string releaseTimestamp = common["steam_release_date"].AsString();
                    if (long.TryParse(releaseTimestamp, out long unixSeconds) && unixSeconds > 0) {
                        releaseDate = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToString("MMM d, yyyy");
                    }

                    apps.Add(new SteamAppInfo {
                        AppId = app.ID,
                        Name = name,
                        Type = type,
                        LibraryCapsulePath = libraryCapsulePath,
                        LibraryHeroPath = libraryHeroPath,
                        LibraryLogoPath = libraryLogoPath,
                        Developer = developer,
                        Publisher = publisher,
                        ReleaseDate = releaseDate
                    });
                }
            }
            return apps;
        }

        private static string GetAssociationNames(KeyValue common, string associationType) {
            return string.Join(", ", common["associations"].Children.Where(association => string.Equals(association["type"].AsString(), associationType, StringComparison.OrdinalIgnoreCase)).Select(association => association["name"].AsString()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
        }
    }
}
