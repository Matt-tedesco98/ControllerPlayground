using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ControllerPlayground.Services {
    public interface ISteamSessionService {

        bool IsConnected { get; }
        bool IsAuthenticated { get; }

        event Action<string> QrChallengeChanged;
        event Action? Connected;
        event Action? Authenticated;

        void Connect();
        void Disconnect();

        Task BeginQrAuthenticationAsync(CancellationToken cancellationToken = default);

    }
}
