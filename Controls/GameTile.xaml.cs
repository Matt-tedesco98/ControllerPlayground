using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Controls {
    public sealed partial class GameTile : UserControl {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(GameTile),
                new PropertyMetadata("Game Title"));

        public static readonly DependencyProperty CoverImageUrlProperty = DependencyProperty.Register(nameof(CoverImageUrl),
            typeof(string),
            typeof(GameTile),
            new PropertyMetadata(string.Empty,
                OnCoverImageUrlChanged));

        public string CoverImageUrl {
            get => (string)GetValue(CoverImageUrlProperty);
            set => SetValue(CoverImageUrlProperty, value);
        }

        public string Title {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnCoverImageUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not GameTile tile)
                return;

            string? url = e.NewValue as string;

            if (string.IsNullOrEmpty(url)) {
                tile.CoverImage.Source = null;
                tile.CoverImage.Visibility = Visibility.Collapsed;
                return;
            }

            try {
                tile.CoverImage.Visibility = Visibility.Visible;
                tile.CoverImage.Source = new BitmapImage(new Uri(url)) {
                    DecodePixelWidth = 440
                };
            }
            catch {
                tile.CoverImage.Source = null;
                tile.CoverImage.Visibility = Visibility.Collapsed;
            }
        }

        private void CoverImage_ImageFailed(object sender, ExceptionRoutedEventArgs e) {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine(
        $"Steam cover failed: {Title}");
        }

        private void TileButton_GotFocus(object sender, RoutedEventArgs e) {
            TileButton.Scale = new System.Numerics.Vector3(1.06f, 1.06f, 1);
        }
        private void TileButton_LostFocus(object sender, RoutedEventArgs e) {
            TileButton.Scale = new System.Numerics.Vector3(1, 1, 1);
        }

        public bool FocusTile() {
            return TileButton.Focus(FocusState.Programmatic);
        }

        public event EventHandler? Activated;

        public void Activate() {
            Activated?.Invoke(this, EventArgs.Empty);
        }
        public GameTile() {
            InitializeComponent();

            TileButton.Click += (_, _) => {
                Activated?.Invoke(this, EventArgs.Empty);
            };

        }
    }
}
