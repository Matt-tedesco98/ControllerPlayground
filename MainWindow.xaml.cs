using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    private double _x = 100;
    private double _y = 100;
    private const double Speed = 10;

    public MainWindow() {
        InitializeComponent();

        bool controllerInit = ControllerInputNative.ControllerInput_Initialize();

        System.Diagnostics.Debug.WriteLine(
            $"ControllerInputNative initialized: {controllerInit}");

        //_gameInputService = new GameInputService();
        //System.Diagnostics.Debug.WriteLine(
        //    $"GameInputService initialized: {_gameInputService.IsInitialized}");
        //_gamepadTimer = new DispatcherTimer();
        //_gamepadTimer.Interval = TimeSpan.FromMilliseconds(16);
        //_gamepadTimer.Tick += GamepadTimer_Tick;
        //_gamepadTimer.Start();

        Activated += (_, _) => { PlayArea.Focus(FocusState.Programmatic); };
    }

    private void PlayArea_KeyDown(object sender, KeyRoutedEventArgs e) {
        switch (e.Key) {
            case Windows.System.VirtualKey.Left:
                _x -= Speed;
                break;
            case Windows.System.VirtualKey.Right:
                _x += Speed;
                break;
            case Windows.System.VirtualKey.Up:
                _y -= Speed;
                break;
            case Windows.System.VirtualKey.Down:
                _y += Speed;
                break;
        }

        Player.Margin = new Thickness(_x, _y, 0, 0);
    }

    //private void GamepadTimer_Tick(object? sender, object e) {
    //    if (!_gameInputService.TryGetGamepadState(out var state)) {
    //        ControllerStatus.Text = "No controller detected";
    //        return;
    //    }

    //    bool aPressed =
    //        (state.Buttons & GameInputGamepadButtons.A) != 0;

    //    ControllerStatus.Text =
    //        $"Controller detected\n" +
    //        $"A: {aPressed}\n" +
    //        $"Left Stick: {state.LeftThumbstickX:F2}, {state.LeftThumbstickY:F2}\n" +
    //        $"LT: {state.LeftTrigger:F2}   RT: {state.RightTrigger:F2}";
    //}


    //private readonly GameInputService _gameInputService;
    //private readonly DispatcherTimer _gamepadTimer;
}