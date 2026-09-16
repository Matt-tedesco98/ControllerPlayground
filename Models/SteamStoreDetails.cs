using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class SteamStoreDetails {
        public string Description { get; set; } = string.Empty;
        public List<string> Genres { get; set; } = new();
        public string ControllerSupport { get; set; } = string.Empty;
        public List<uint> DlcAppIds { get; set; } = new();
    }
}
