using ControllerPlayground.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ControllerPlayground.Services.steam {
    public interface ISteamService {
        Task<IReadOnlyList<ActivityFeedItem>> GetGameActivityAsync(uint appId, CancellationToken cancellationToken = default);
    }
}
