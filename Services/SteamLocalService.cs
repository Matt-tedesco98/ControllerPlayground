using ControllerPlayground.Models;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.UserProfile;

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
    }
}
