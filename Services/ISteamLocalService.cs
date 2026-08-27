using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ControllerPlayground.Models;
using System.Threading;

namespace ControllerPlayground.Services {
    public interface ISteamLocalService {
        Task<IReadOnlyList<SteamFriend>> GetFriendsAsync(CancellationToken cancellationToken = default);
    }
}
