using ControllerPlayground.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ControllerPlayground.Services {
    public sealed class SteamChatService : ISteamChatService {
        public event Action<SteamChatMessage> MessageReceived;

        public Task<bool> SendMessageAsync(
            string steamId,
            string message,
            CancellationToken cancellationToken = default) { 
            cancellationToken.ThrowIfCancellationRequested();


            //steam transport will go here, for now just return false
            return Task.FromResult(false);
        }
        }
}
