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
        event Action<string>? MessageHistoryUpdated;
        event Action<string, int>? UnreadCountChanged;

        Task<bool> SendMessageAsync(
            string steamId,
            string message,
            CancellationToken cancellationToken = default);

        IReadOnlyList<SteamChatMessage> GetMessages(string steamId);

        void RequestMessageHistory(string steamId);


        int GetUnreadCount(string steamId);

        void MarkConversationRead(string steamId);
    }
}
