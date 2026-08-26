using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Models {
    public sealed class ActivityFeedItem {

        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public DateTimeOffset PublishedAt { get; set; }

        public string ImageUrl { get; set; } = string.Empty;
    }
}
