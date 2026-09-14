using ControllerPlayground.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ControllerPlayground.Services.Steam {
    public interface ISteamService {
        Task<IReadOnlyList<ActivityFeedItem>> GetGameActivityAsync(uint appId, CancellationToken cancellationToken = default);
        Task<SteamStoreDetails> GetGameStoreDetailsAsync(uint appId, CancellationToken cancellationToken = default);
    }
}
