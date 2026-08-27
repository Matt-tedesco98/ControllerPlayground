using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class GameItem {
        public string Title { get; set; } = string.Empty;

        public string HeroImagePath { get; set; } = string.Empty;
        public string CoverImagePath { get; set; } = string.Empty;

        public string LastPlayed { get; set; } = string.Empty;
        public string PlayTime { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Developer { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public List<string> Genres { get; set; } = new();
        public string ReleaseDate { get; set; } = string.Empty;

        public bool SupportsXboxController { get; set; }
        public bool SupportsDualShock { get; set; }
        public bool SupportsDualSense { get; set; }
        public GameActivityData ActivityData { get; set; } = new();
        public uint? SteamAppId { get; set; }

    }
}
