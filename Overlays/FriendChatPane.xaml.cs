using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ControllerPlayground.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class FriendChatPane : UserControl {
        public FriendChatPane() {
            InitializeComponent();
        }

        public SteamFriend? Friend { 
            get => (SteamFriend?)GetValue(FriendProperty);
            set => SetValue(FriendProperty, value);
        }

        public static readonly DependencyProperty FriendProperty = DependencyProperty.Register(
            nameof(Friend),
            typeof(SteamFriend),
            typeof(FriendChatPane),
            new PropertyMetadata(null, OnFriendChanged));

        public static void OnFriendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is FriendChatPane pane) {
                pane.UpdateFriend();
            }
        }

        private void UpdateFriend() { 
            SteamFriend? friend = Friend;
            if (friend == null) {
                FriendNameText.Text = string.Empty;
                AvatarImage.Source = null;
                return;
            }

            FriendNameText.Text = friend.DisplayName;
            AvatarImage.Source = string.IsNullOrWhiteSpace(friend.AvatarUrl)
                ? null
                : new BitmapImage(new Uri(friend.AvatarUrl));
        }
    }
}
