using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class SteamLibraryGame {
        public uint AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LibraryCapsuleUrl { get; set; } = string.Empty;
    }
}
