using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ControllerPlayground.Models;
using System.Collections.ObjectModel;
using ControllerPlayground.Input;
using ControllerPlayground.Controls;
using ControllerPlayground.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class FriendsOverlay : UserControl {
        public FriendsOverlay() {
            InitializeComponent();
            ChatPane.SendRequested += ChatPane_SendRequested;
        }

        public ObservableCollection<SteamFriend> Friends { get; } = new();

        internal void FocusFirstItem() { 
            void OnLayoutUpdated(Object? sender, Object e) { 
                FriendsList.LayoutUpdated -= OnLayoutUpdated;

                if (FocusManager.FindFirstFocusableElement(FriendsList) is Control control) {
                    control.Focus(FocusState.Keyboard);
                }
            }
            FriendsList.LayoutUpdated += OnLayoutUpdated;
        }

        internal void HandleControllerAction(ControllerAction action) {

            if (action == ControllerAction.Accept) { 
                DependencyObject? focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                SteamFriendCard? card = FindAncestor<SteamFriendCard>(focused);
                card?.Activate();
                return;
            }

            FocusNavigationDirection? direction = action switch {
                ControllerAction.NavigateUp => FocusNavigationDirection.Up,
                ControllerAction.NavigateDown => FocusNavigationDirection.Down,
                ControllerAction.NavigateLeft => FocusNavigationDirection.Left,
                ControllerAction.NavigateRight => FocusNavigationDirection.Right,
                _ => null
            };

            if (direction == null) { 
                return; 
            }

            FocusManager.TryMoveFocus(direction.Value, new FindNextElementOptions { SearchRoot = OverlayRoot });
        }

        internal event Action<SteamFriend>? FriendSelected;

        private void FriendCard_Activated(object? sender, EventArgs e) { 
            if(sender is SteamFriendCard card && card.Item is SteamFriend friend) {

                _lastFocusedCard = card;

                ChatPane.Friend = friend;
                ChatPane.Visibility = Visibility.Visible;

                OverlayRoot.Width = 840;

                FriendSelected?.Invoke(friend);
            }
        }

        private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject {
            while (element != null) {
                if (element is T match)
                    return match;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        internal bool IsChatOpen => ChatPane.Visibility == Visibility.Visible;

        internal void CloseChatPane() { 
            ChatPane.Visibility = Visibility.Collapsed;
            ChatPane.Friend = null;
            OverlayRoot.Width = 420;
        }

        private SteamFriendCard? _lastFocusedCard;

        internal void RestoreFriendFocus() { 
            _lastFocusedCard?.Focus(FocusState.Keyboard);
        }

        private readonly ISteamChatService _chatService = new SteamChatService();

        private async void ChatPane_SendRequested(SteamFriend friend, string text) { 
            bool sent = await _chatService.SendMessageAsync(friend.SteamId, text);
            if (sent) { 
                ChatPane.ConfirmMessageSent(text);
            }
        }
    }
}
