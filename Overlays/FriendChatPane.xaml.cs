using ControllerPlayground.Models;
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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class FriendChatPane : UserControl {

        internal event Action<SteamFriend, string>? SendRequested;
        public FriendChatPane() {
            InitializeComponent();

            Messages.Add(new SteamChatMessage {
                SteamId = "friend",
                Text = "Hey, want to play something?",
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = false
            });

            Messages.Add(new SteamChatMessage {
                SteamId = "me",
                Text = "Yeah, give me a minute.",
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = true
            });
        }

        public ObservableCollection<SteamChatMessage> Messages { get; } = new();

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

        private void SendButton_Click(object sender, RoutedEventArgs e) {
            SteamFriend? friend = Friend;
            string text = MessageInput.Text.Trim();

            if (friend == null || string.IsNullOrWhiteSpace(text)) {
                return;
            }

            SendRequested?.Invoke(friend, text);
        }

        internal void ConfirmMessageSent(string text) {
            SteamFriend? friend = Friend;

            if (friend == null)
                return;

            Messages.Add(new SteamChatMessage {
                SteamId = friend.SteamId,
                Text = text,
                Timestamp = DateTimeOffset.Now,
                IsFromCurrentUser = true
            });

            MessageInput.Text = string.Empty;
        }

        internal void AddReceivedMessage(SteamChatMessage message) {
            SteamFriend? friend = Friend;

            if (friend == null)
                return;

            if (string.Equals(message.SteamId, friend.SteamId, StringComparison.Ordinal)) { 
                return;
            }

            Messages.Add(message);
        }
    }
}
