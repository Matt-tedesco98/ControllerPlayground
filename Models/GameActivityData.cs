using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using ControllerPlayground.Views;

namespace ControllerPlayground.Models {
    public sealed class GameActivityData {

        public ObservableCollection<FriendActivityItem> RecentFriends { get; set; } = new ObservableCollection<FriendActivityItem>();
        public ObservableCollection<FriendActivityItem> PlayedPreviouslyFriends { get; set; } = new ObservableCollection<FriendActivityItem>();
        public ObservableCollection<ActivityFeedItem> ActivityFeed { get; set; } = new ObservableCollection<ActivityFeedItem>();

    }
}
