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

        _controllerService.ConnectionChanged += ControllerService_ConnectionChanged;

        _controllerService.ControllerFamilyChanged += ControllerService_ControllerFamilyChanged;

        PlayArea.Loaded += (_, _) => {
            FirstGameTile.FocusTile();
        };

        bool controllerInit = ControllerInputNative.ControllerInput_Initialize();

        System.Diagnostics.Debug.WriteLine(
            $"ControllerInputNative initialized: {controllerInit}");

        _controllerTimer.Interval = TimeSpan.FromMilliseconds(16);
        _controllerTimer.Tick += ControllerTimer_Tick;
        _controllerTimer.Start();

        Activated += (_, _) => { PlayArea.Focus(FocusState.Programmatic); };
    }

    private void ControllerTimer_Tick(object? sender, object e) {
        ControllerAction action = _controllerService.PollAction();

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
            case ControllerAction.Back:
                System.Diagnostics.Debug.WriteLine("Back Requested");
                break;

        }
    }

    private void ControllerService_ConnectionChanged(bool connected) {
        System.Diagnostics.Debug.WriteLine(connected ? "Controller connected" : "Controller disconnected");
    }
    private void GameTile_Activated(object sender, EventArgs e) {
        if (sender is GameTile gameTile) {
            System.Diagnostics.Debug.WriteLine($"GameTile activated: {gameTile.Title}");
        }
    }

    private void ControllerService_ControllerFamilyChanged(ControllerFamily family) {

        AcceptPrompt.Text = $"{ControllerGlyphs.GetAcceptGlyph(family)} Select";

        BackPrompt.Text = $"{ControllerGlyphs.GetBackGlyph(family)} Back";

        System.Diagnostics.Debug.WriteLine($"Controller family changed: {family}");
    }
}