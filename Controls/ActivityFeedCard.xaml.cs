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
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Controls {
    public sealed partial class ActivityFeedCard : UserControl {
        public ActivityFeedCard() {
            InitializeComponent();
        }

        public ActivityFeedItem? Item {
            get => (ActivityFeedItem?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(
                nameof(Item),
                typeof(ActivityFeedItem),
                typeof(ActivityFeedCard),
                new PropertyMetadata(null, OnItemChanged));

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is ActivityFeedCard card) {
                card.UpdateVisuals();
            }
        }

        private void UpdateVisuals() {
            ActivityFeedItem? item = Item;
            if (item == null) {
                TitleText.Text = string.Empty;
                SubtitleText.Text = string.Empty;
                DateText.Text = string.Empty;
                FeedImage.Source = null;
                return;
            }

            TitleText.Text = item.Title;
            SubtitleText.Text = item.Subtitle;
            DateText.Text = item.PublishedAt.ToLocalTime().ToString("MMMM d, yyyy")
            ;

            FeedImage.Source = string.IsNullOrWhiteSpace(item.ImageUrl)
                ? null
                : new BitmapImage(new Uri(item.ImageUrl));
        }

    }
}
