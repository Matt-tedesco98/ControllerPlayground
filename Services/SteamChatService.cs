using ControllerPlayground.Models;
using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ControllerPlayground.Services {
    public sealed class SteamChatService : ISteamChatService {
        public SteamChatService(SteamSessionService session) { 
            _session = session;
            _steamFriends = session.FriendsHandler;
            _session.CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMessage);
        }

        private readonly SteamSessionService _session;
        private readonly SteamFriends _steamFriends;

        public event Action<SteamChatMessage>? MessageReceived;


        public Task<bool> SendMessageAsync(
            string steamId,
            string message,
            CancellationToken cancellationToken = default) { 

            cancellationToken.ThrowIfCancellationRequested();

            if (!_session.IsAuthenticated) 
                return Task.FromResult(false);

            if(!ulong.TryParse(steamId, out ulong steamId64))
                return Task.FromResult(false);

            SteamID target = new(steamId64);

            _steamFriends.SendChatMessage(
                target,
                EChatEntryType.ChatMsg,
                message);

            return Task.FromResult(true);
        }

        private void OnFriendMessage(SteamFriends.FriendMsgCallback callback) {
            if (callback.EntryType != EChatEntryType.ChatMsg || string.IsNullOrWhiteSpace(callback.Message))
                return;

            string senderSteamId = callback.Sender.ConvertToUInt64().ToString();

            SteamChatMessage message = new() {
                SteamId = senderSteamId,
                Text = callback.Message,
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = false
            };

            Debug.WriteLine($"Steam message received from {message.SteamId}: {message.Text}");

            MessageReceived?.Invoke(message);
        }
    }
}
