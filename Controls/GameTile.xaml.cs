using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

        public string Title {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
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
        public GameTile() {
            InitializeComponent();

        }
    }
}
