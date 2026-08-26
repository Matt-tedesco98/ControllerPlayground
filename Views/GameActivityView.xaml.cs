using ControllerPlayground.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GameActivityView : UserControl {
        public GameActivityView() {
            InitializeComponent();

            //mock data for testing

            //Recent friends
            RecentFriends.Add(new FriendActivityItem {
                SteamId = "1",
                DisplayName = "Friend 1",
                RecentPlayTimeMinutes = 690
            });
            RecentFriends.Add(new FriendActivityItem {
                SteamId = "2",
                DisplayName = "Friend 2",
                RecentPlayTimeMinutes = 450
            });
            RecentFriends.Add(new FriendActivityItem {
                SteamId = "3",
                DisplayName = "Friend 3",
                RecentPlayTimeMinutes = 120
            });

            //Played previously friends
            PlayedPreviouslyFriends.Add(new FriendActivityItem {
                SteamId = "4",
                DisplayName = "Friend 4"
            });

            PlayedPreviouslyFriends.Add(new FriendActivityItem {
                SteamId = "5",
                DisplayName = "Friend 5"
            });

            //Activity feed
            ActivityFeed.Add(new ActivityFeedItem {
                Title = "Game Update",
                Subtitle = "SMALL UPDATE / PATCH NOTES ",
                PublishedAt = DateTimeOffset.Now
            });

            //
        }

        public ObservableCollection<FriendActivityItem> RecentFriends { get; } = new();

        public ObservableCollection<FriendActivityItem> PlayedPreviouslyFriends { get; } = new();

        public ObservableCollection<ActivityFeedItem> ActivityFeed { get; } = new();
    }
}
