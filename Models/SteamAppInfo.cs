using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class SteamAppInfo {
        public uint AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LibraryCapsulePath { get; set; } = string.Empty;
        public string LibraryHeroPath { get; set; } = string.Empty;
        public string LibraryLogoPath { get; set; } = string.Empty;
    }
}
