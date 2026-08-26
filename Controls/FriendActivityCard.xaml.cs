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
using ControllerPlayground.Models;

namespace ControllerPlayground.Controls {
    public sealed partial class FriendActivityCard : UserControl {
        public FriendActivityCard() {
            InitializeComponent();
        }

        public FriendActivityItem? Item {
            get => (FriendActivityItem?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
            nameof(Item),
            typeof(FriendActivityItem),
            typeof(FriendActivityCard),
            new PropertyMetadata(null, OnItemChanged)
        );

        private static void OnItemChanged( DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is FriendActivityCard card) {
                card.UpdateVisuals();
            }
        }

        private void UpdateVisuals() {
            FriendActivityItem? item = Item;
            if (item == null) { 
                FriendNameText.Text = string.Empty;
                PlayTimeText.Text = string.Empty;
                AvatarImage.Source = null;
                return;
            }

            FriendNameText.Text = item.DisplayName;
            double hours = item.RecentPlayTimeMinutes / 60.0;

            PlayTimeText.Text = $"{hours:F1} hours played recently";

            AvatarImage.Source = string.IsNullOrWhiteSpace(item.AvatarUrl)
                ? null 
                : new BitmapImage(new Uri(item.AvatarUrl));
        }
    }
}
