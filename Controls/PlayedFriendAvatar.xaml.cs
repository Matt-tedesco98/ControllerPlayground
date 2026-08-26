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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Controls {
    public sealed partial class PlayedFriendAvatar : UserControl {
        public PlayedFriendAvatar() {
            InitializeComponent();
        }

        public FriendActivityItem? Item {
            get => (FriendActivityItem?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(
                nameof(Item),
                typeof(FriendActivityItem),
                typeof(PlayedFriendAvatar),
                new PropertyMetadata(null, OnItemChanged));

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is PlayedFriendAvatar avatar) {
                avatar.UpdateVisuals();
            }
        }

        private void UpdateVisuals() {
            FriendActivityItem? item = Item;

            AvatarImage.Source = item == null || string.IsNullOrWhiteSpace(item.AvatarUrl)
                ? null
                : new BitmapImage(new Uri(item.AvatarUrl));
        }
    }
}
