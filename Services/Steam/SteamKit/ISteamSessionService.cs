using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ControllerPlayground.Services.Steam.SteamKit {
    public interface ISteamSessionService {

        bool IsConnected { get; }
        bool IsAuthenticated { get; }

        event Action<string> QrChallengeChanged;
        event Action? Connected;
        event Action? Authenticated;
        event Action? SavedAuthenticationFailed;

        void Connect();
        void Disconnect();

        Task<bool> TrySavedAuthenticationAsync(CancellationToken cancellationToken = default);
        Task BeginQrAuthenticationAsync(CancellationToken cancellationToken = default);
    }
}
