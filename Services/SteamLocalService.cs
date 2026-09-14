using ControllerPlayground.Models;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.UserProfile;
using System;
using Windows.Media.Capture;

namespace ControllerPlayground.Services {
    public sealed class SteamLocalService : ISteamLocalService {
        public Task<IReadOnlyList<SteamFriend>> GetFriendsAsync(
    CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            List<SteamFriend> results = new();

            string? configPath = GetActiveUserConfigPath();
            uint? activeUserId = GetActiveUserId();

            if (configPath == null ||
                activeUserId == null ||
                !File.Exists(configPath)) {
                return Task.FromResult<IReadOnlyList<SteamFriend>>(results);
            }

            VdfNode root = VdfParser.ParseFile(configPath);

            if (!root.TryGetChild("UserLocalConfigStore", out VdfNode? userConfig) ||
                userConfig == null ||
                !userConfig.TryGetChild("friends", out VdfNode? friends) ||
                friends == null) {
                return Task.FromResult<IReadOnlyList<SteamFriend>>(results);
            }

            foreach (var entry in friends.Children) {
                if (!uint.TryParse(entry.Key, out uint accountId))
                    continue;

                if (accountId == activeUserId.Value)
                    continue;

                VdfNode friendNode = entry.Value;

                if (!friendNode.TryGetChild("name", out VdfNode? nameNode) ||
                    string.IsNullOrWhiteSpace(nameNode?.Value)) {
                    continue;
                }

                friendNode.TryGetChild("avatar", out VdfNode? avatarNode);


                string avatarUrl = string.Empty;

                if (!string.IsNullOrWhiteSpace(avatarNode?.Value)) {
                    avatarUrl =
                        $"https://avatars.fastly.steamstatic.com/{avatarNode.Value}_medium.jpg";
                }

                string steamId64 = (SteamId64Base + accountId).ToString();

                results.Add(new SteamFriend {
                    SteamId = steamId64,
                    DisplayName = nameNode.Value,
                    AvatarUrl = avatarUrl
                });
            }

            return Task.FromResult<IReadOnlyList<SteamFriend>>(results);
        }

        public string? GetSteamInstallPath() {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }

        public uint? GetActiveUserId() {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");

            object? value = key?.GetValue("ActiveUser");

            if (value is int activeUser && activeUser > 0) {
                return unchecked((uint)activeUser);
            }
            return null;

        }

        public string? GetActiveUserConfigPath() {
            string? steamPath = GetSteamInstallPath();
            uint? activeUserId = GetActiveUserId();

            if (steamPath == null || activeUserId == null) {
                return null;
            }

            return Path.Combine(
                steamPath,
                "userdata",
                activeUserId.ToString(),
                "config",
                "localconfig.vdf");
        }

        public const ulong SteamId64Base = 76561197960265728UL;

        public ulong? GetActiveSteamId64() {
            uint? accountId = GetActiveUserId();

            if (accountId == null) {
                return null;
            }

            return SteamId64Base + accountId.Value;
        }

        public string? GetAvatarPath(string steamId64) {
            string? steamPath = GetSteamInstallPath();

            if (steamPath == null)
                return null;

            string avatarPath = Path.Combine(
                steamPath,
                "config",
                "avatarcache",
                $"{steamId64}.jpg");

            return File.Exists(avatarPath) ? avatarPath : null;
        }

        public Task<IReadOnlyDictionary<uint, DateTimeOffset>>
    GetLastPlayedByAppIdAsync(
        CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<uint, DateTimeOffset> results = new();

            string? configPath = GetActiveUserConfigPath();

            if (configPath == null ||
                !File.Exists(configPath)) {
                Debug.WriteLine(
                    "Steam LastPlayed: localconfig.vdf was not found.");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            VdfNode root =
                VdfParser.ParseFile(configPath);

            if (!root.TryGetChild(
                    "UserLocalConfigStore",
                    out VdfNode? userConfig) ||
                userConfig == null) {
                Debug.WriteLine(
                    "Steam LastPlayed: UserLocalConfigStore not found.");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            if (!userConfig.TryGetChild(
                    "Software",
                    out VdfNode? software) ||
                software == null) {
                Debug.WriteLine(
                    "Steam LastPlayed: Software node not found.");

                Debug.WriteLine(
                    $"UserLocalConfigStore children: " +
                    $"{string.Join(", ", userConfig.Children.Keys)}");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            if (!software.TryGetChild(
                    "Valve",
                    out VdfNode? valve) ||
                valve == null) {
                Debug.WriteLine(
                    "Steam LastPlayed: Valve node not found.");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            if (!valve.TryGetChild(
                    "Steam",
                    out VdfNode? steam) ||
                steam == null) {
                Debug.WriteLine(
                    "Steam LastPlayed: Steam node not found.");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            if (!steam.TryGetChild(
                    "apps",
                    out VdfNode? apps) ||
                apps == null) {
                Debug.WriteLine(
                    "Steam LastPlayed: apps node not found.");

                Debug.WriteLine(
                    $"Steam node children: " +
                    $"{string.Join(", ", steam.Children.Keys)}");

                return Task.FromResult<
                    IReadOnlyDictionary<uint, DateTimeOffset>>(results);
            }

            int sampleCount = 0;

            foreach (var entry in apps.Children) {
                if (sampleCount >= 5)
                    break;

                sampleCount++;
            }

            int appsWithLastPlayed = 0;

            foreach (var entry in apps.Children) {
                if (!uint.TryParse(
                        entry.Key,
                        out uint appId)) {
                    continue;
                }

                if (!entry.Value.TryGetChild(
                        "LastPlayed",
                        out VdfNode? lastPlayedNode)) {
                    continue;
                }

                appsWithLastPlayed++;

                if (!long.TryParse(
                        lastPlayedNode?.Value,
                        out long unixSeconds) ||
                    unixSeconds <= 0) {
                    continue;
                }

                results[appId] =
                    DateTimeOffset.FromUnixTimeSeconds(
                        unixSeconds);
            }

            Debug.WriteLine(
                $"Steam valid LastPlayed timestamps: {results.Count}");

            return Task.FromResult<
                IReadOnlyDictionary<uint, DateTimeOffset>>(results);
        }
        public Task<IReadOnlyCollection<uint>> GetInstalledAppIdsAsync(CancellationToken cancellationToken = default) {
            Debug.WriteLine(">>> ENTERED GetInstalledAppIdsAsync <<<");
            cancellationToken.ThrowIfCancellationRequested();

            HashSet<uint> installedAppIds = new();

            string? steamPath = GetSteamInstallPath();
            Debug.WriteLine($"Installed detection Steam path: {steamPath ?? "<null>"}");

            if (steamPath == null) {
                return Task.FromResult<IReadOnlyCollection<uint>>(installedAppIds);
            }

            string libraryFoldersPath = Path.Combine(
                steamPath,
                "steamapps",
                "libraryfolders.vdf");

            Debug.WriteLine($"Steam libraryfolders path: {libraryFoldersPath}");

            Debug.WriteLine($"Steam libraryfolders exists: {File.Exists(libraryFoldersPath)}");

            if (!File.Exists(libraryFoldersPath)) {
                Debug.WriteLine("Steam libraryfolders.vdf was not found");
                return Task.FromResult<IReadOnlyCollection<uint>>(installedAppIds);
            }

            VdfNode root = VdfParser.ParseFile(libraryFoldersPath);

            Debug.WriteLine($"libraryfolders root children: {string.Join(", ", root.Children.Keys)}");

            if (!root.TryGetChild(
                "libraryfolders",
                out VdfNode? libraryFolders) || libraryFolders == null) {
                return Task.FromResult<IReadOnlyCollection<uint>>(installedAppIds);
            }

            Debug.WriteLine($"Steam library entries: {string.Join(", ", libraryFolders.Children.Keys)}");

            foreach (var libraryEntry in libraryFolders.Children) {
                if (!libraryEntry.Value.TryGetChild(
                "apps",
                out VdfNode? apps) ||
            apps == null) {
                    continue;
                }

                foreach (var appEntry in apps.Children) {
                    Debug.WriteLine($"Library {libraryEntry.Key} children: " + $"{string.Join(", ", libraryEntry.Value.Children.Keys)}");
                    if (uint.TryParse(
                            appEntry.Key,
                            out uint appId)) {
                        installedAppIds.Add(appId);
                    }
                }
            }

            Debug.WriteLine($"Steam installed app IDs: {installedAppIds.Count}");

            return Task.FromResult<IReadOnlyCollection<uint>>(
                installedAppIds);
        }

        public Task<IReadOnlyDictionary<uint, int>> GetPlaytimeByAppIdAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<uint, int> results = new();
            string? configPath = GetActiveUserConfigPath();
            if (configPath == null || !File.Exists(configPath)) {
                return Task.FromResult<IReadOnlyDictionary<uint, int>>(results);
            }
            VdfNode root = VdfParser.ParseFile(configPath);
            if (!root.TryGetChild(
           "UserLocalConfigStore",
           out VdfNode? userConfig) ||
            userConfig == null ||
            !userConfig.TryGetChild(
                "Software",
                out VdfNode? software) ||
                 software == null ||
                !software.TryGetChild(
                    "Valve",
                    out VdfNode? valve) ||
                    valve == null ||
                    !valve.TryGetChild(
                        "Steam",
                        out VdfNode? steam) ||
                        steam == null ||
                        !steam.TryGetChild(
                            "apps",
                            out VdfNode? apps) ||
                            apps == null) {
                return Task.FromResult<IReadOnlyDictionary<uint, int>>(results);
            }
            foreach (var entry in apps.Children) {
                if (!uint.TryParse(entry.Key, out uint appId)) {
                    continue;
                }
                if (!entry.Value.TryGetChild("Playtime", out VdfNode? playtimeNode)) {
                    continue;
                }
                if (!int.TryParse(playtimeNode?.Value, out int minutes) || minutes < 0) {
                    continue;
                }
                results[appId] = minutes;
            }
            Debug.WriteLine($"Steam playtime entries: {results.Count}");
            return Task.FromResult<IReadOnlyDictionary<uint, int>>(results);
        }
    }
}
