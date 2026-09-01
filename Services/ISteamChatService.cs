using ControllerPlayground.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ControllerPlayground.Services {
    public interface ISteamChatService {
        event Action<SteamChatMessage> MessageReceived;

        Task<bool> SendMessageAsync(
            string steamId,
            string message,
            CancellationToken cancellationToken = default);
    }
}
