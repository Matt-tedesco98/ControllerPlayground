using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
namespace ControllerPlayground; 
public sealed partial class MainWindow : Window {
    private double _x = 100;
    private double _y = 100; 
    private const double Speed = 10; 
    public MainWindow() {
        InitializeComponent();

        Activated += (_, _) =>
        {
            PlayArea.Focus(FocusState.Programmatic);
        };
    }
    private void PlayArea_KeyDown(object sender, KeyRoutedEventArgs e) { switch (e.Key) { case Windows.System.VirtualKey.Left: _x -= Speed; break; case Windows.System.VirtualKey.Right: _x += Speed; break; case Windows.System.VirtualKey.Up: _y -= Speed; break; case Windows.System.VirtualKey.Down: _y += Speed; break; } Player.Margin = new Thickness(_x, _y, 0, 0); }
}