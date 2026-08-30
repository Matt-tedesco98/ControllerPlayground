using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class SteamChatMessage {
        public string SteamId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public bool IsFromCurrentUser { get; set; } = false;



    }
}
