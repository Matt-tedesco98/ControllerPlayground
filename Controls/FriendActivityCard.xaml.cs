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
    public sealed partial class FriendActivityCard : UserControl {
        public FriendActivityCard() {
            InitializeComponent();
        }

        public string FriendName { 
            get => (string)GetValue(FriendNameProperty);
            set => SetValue(FriendNameProperty, value);
        }

        public static readonly DependencyProperty FriendNameProperty = DependencyProperty.Register(
            nameof(FriendName),
            typeof(string),
            typeof(FriendActivityCard),
            new PropertyMetadata(string.Empty)
        );

        public string PlayTimeText {
            get => (string)GetValue(PlayTimeTextProperty);
            set => SetValue(PlayTimeTextProperty, value);
        }

        public static readonly DependencyProperty PlayTimeTextProperty = DependencyProperty.Register(
            nameof(PlayTimeText),
            typeof(string),
            typeof(FriendActivityCard),
            new PropertyMetadata(string.Empty)
        );

        public string AvatarUrl {
            get => (string)GetValue(AvatarUrlProperty);
            set => SetValue(AvatarUrlProperty, value);
        }

        public static readonly DependencyProperty AvatarUrlProperty = DependencyProperty.Register(
            nameof(AvatarUrl),
            typeof(string),
            typeof(FriendActivityCard),
            new PropertyMetadata(string.Empty)
        );

        private static void OnAvatarUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is FriendActivityCard card && e.NewValue is string url && !string.IsNullOrWhiteSpace(url)) {
                card.AvatarImage.Source = new BitmapImage(new Uri(url));
            }
        }
    }
}
