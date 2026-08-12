using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using Microsoft.UI.Xaml.Media;
using ControllerPlayground.Controls;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    private double _x = 100;
    private double _y = 100;
    private const double Speed = 10;

    private readonly DispatcherTimer _controllerTimer = new();
    private readonly ControllerService _controllerService = new();

    public MainWindow() {
        InitializeComponent();

        PlayArea.Loaded += (_, _) => {
            FirstGameTile.FocusTile();
        };

        bool controllerInit = ControllerInputNative.ControllerInput_Initialize();

        System.Diagnostics.Debug.WriteLine(
            $"ControllerInputNative initialized: {controllerInit}");

        _controllerTimer.Interval = TimeSpan.FromMilliseconds(16);
        _controllerTimer.Tick += ControllerTimer_Tick;
        _controllerTimer.Start();

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

    private void ControllerTimer_Tick(object? sender, object e) {
        if (ControllerInputNative.ControllerInput_GetState(out ControllerState state)) {
            ControllerDebugText.Text =
                $"Buttons: 0x{state.Buttons:X}\n" +
                $"LT: {state.LeftTrigger:F2}   RT: {state.RightTrigger:F2}\n" +
                $"LS: {state.LeftThumbstickX:F2}, {state.LeftThumbstickY:F2}\n" +
                $"RS: {state.RightThumbstickX:F2}, {state.RightThumbstickY:F2}";
        } else {
            ControllerDebugText.Text = "No controller detected";
        }
        ControllerAction action = _controllerService.PollAction();
        if (action != ControllerAction.none) {

            System.Diagnostics.Debug.WriteLine($"Controller Action: {action}");

            var focusOptions = new FindNextElementOptions {
                SearchRoot = PlayArea
            };

            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up, focusOptions);
                    break;
                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down, focusOptions);
                    break;
                case ControllerAction.NavigateLeft:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Left, focusOptions);
                    break;
                case ControllerAction.NavigateRight:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Right, focusOptions);
                    break;
                case ControllerAction.Accept: {
                        var focused = FocusManager.GetFocusedElement(PlayArea.XamlRoot);

                        if (focused is DependencyObject element) {
                            DependencyObject? current = element;

                            while (current != null) {
                                if (current is GameTile gameTile) {
                                    gameTile.Activate();
                                    break;
                                }
                                current = VisualTreeHelper.GetParent(current);
                            }
                        }
                        break;
                    }
            }
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

    private void GameTile_Activated(object sender, EventArgs e) {
        if(sender is GameTile gameTile) {
            System.Diagnostics.Debug.WriteLine($"GameTile activated: {gameTile.Title}");
        }
    }
}