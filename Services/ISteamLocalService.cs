using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ControllerPlayground.Models;
using System.Threading;

namespace ControllerPlayground.Services {
    public interface ISteamLocalService {
        Task<IReadOnlyList<SteamFriend>> GetFriendsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<uint, DateTimeOffset>> GetLastPlayedByAppIdAsync (CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<uint>> GetInstalledAppIdsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<uint, int>> GetPlaytimeByAppIdAsync(CancellationToken cancellationToken = default);
    }
}
