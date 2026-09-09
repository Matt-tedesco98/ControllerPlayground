using ControllerPlayground.Models;
using ControllerPlayground.Services.Steam.SteamKit;
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
            _session.CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMessageHistory);
        }

        private readonly SteamSessionService _session;
        private readonly SteamFriends _steamFriends;
        private readonly Dictionary<string, List<SteamChatMessage>> _messagesByFriend = new();

        public event Action<SteamChatMessage>? MessageReceived;
        public event Action<string>? MessageHistoryUpdated;
        public event Action<string, int>? UnreadCountChanged;


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

            StoreMessage(new SteamChatMessage {
                SteamId = steamId,
                Text = message,
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = true
            });

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

            StoreMessage(message);

            int unreadCount = GetUnreadCount(message.SteamId) + 1;

            _unreadCounts[message.SteamId] = unreadCount;

            UnreadCountChanged?.Invoke(message.SteamId, unreadCount);

            MessageReceived?.Invoke(message);
        }

        public IReadOnlyList<SteamChatMessage> GetMessages(string steamId) { 
            if(_messagesByFriend.TryGetValue(steamId, out List<SteamChatMessage>? messages)) {
                return messages;
            }
            return Array.Empty<SteamChatMessage>();
        }

        private void StoreMessage(SteamChatMessage message) {
            if (!_messagesByFriend.TryGetValue(message.SteamId, out List<SteamChatMessage>? messages)) { 
                messages = new List<SteamChatMessage>();
                _messagesByFriend[message.SteamId] = messages;
            }
            messages.Add(message);
        }

        public void RequestMessageHistory(string steamId) {
            if (!_session.IsAuthenticated)
                return;

            if (!ulong.TryParse(steamId, out ulong steamId64))
                return;

            _steamFriends.RequestMessageHistory(new SteamID(steamId64));
        }

        private void OnFriendMessageHistory(SteamFriends.FriendMsgHistoryCallback callback) {

            Debug.WriteLine(
        $"Steam history result: {callback.Result}");

            Debug.WriteLine(
                $"Steam history friend: {callback.SteamID.ConvertToUInt64()}");

            Debug.WriteLine(
                $"Steam history messages: {callback.Messages.Count}");

            foreach (var historyMessage in callback.Messages) {
                Debug.WriteLine(
                    $"History: {historyMessage.Timestamp} | " +
                    $"{historyMessage.SteamID.ConvertToUInt64()} | " +
                    $"{historyMessage.Message}");
            }

            if (callback.Result != EResult.OK)
                return;

            string friendSteamId = callback.SteamID.ConvertToUInt64().ToString();

            List<SteamChatMessage> messages = new();

            foreach (SteamFriends.FriendMsgHistoryCallback.FriendMessage historyMessage in callback.Messages) { 
                string senderSteamId = historyMessage.SteamID.ConvertToUInt64().ToString();

                messages.Add(new SteamChatMessage {
                    SteamId = friendSteamId,
                    Text = historyMessage.Message,
                    Timestamp = new DateTimeOffset(historyMessage.Timestamp).ToLocalTime(),
                    IsFromCurrentUser = senderSteamId != friendSteamId
                });
            }

            _messagesByFriend[friendSteamId] = messages;
            MessageHistoryUpdated?.Invoke(friendSteamId);
        }

        private readonly Dictionary<string, int> _unreadCounts = new();

        public int GetUnreadCount(string steamId) {
            return _unreadCounts.TryGetValue(steamId, out int count) ? count : 0;
        }

        public void MarkConversationRead(string steamId) { 
            if (!_unreadCounts.TryGetValue(steamId, out int count) || count == 0)
                return;

            _unreadCounts[steamId] = 0;
            UnreadCountChanged?.Invoke(steamId, 0);
        }

#if DEBUG
        public void SimulateIncomingMessage(
            string steamId,
            string text = "Test incoming message") {
            var message = new SteamChatMessage {
                SteamId = steamId,
                Text = text,
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = false
            };

            StoreMessage(message);

            int unreadCount =
                GetUnreadCount(steamId) + 1;

            _unreadCounts[steamId] = unreadCount;

            UnreadCountChanged?.Invoke(
                steamId,
                unreadCount);

            MessageReceived?.Invoke(message);
        }
#endif
    }
}
