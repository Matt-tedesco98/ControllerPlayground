using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using Microsoft.UI.Xaml.Media;
using ControllerPlayground.Controls;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    private readonly DispatcherTimer _controllerTimer = new();
    private readonly ControllerService _controllerService = new();

    public MainWindow() {
        InitializeComponent();

        _controllerService.ConnectionChanged += ControllerService_ConnectionChanged;

        _controllerService.ControllerFamilyChanged += ControllerService_ControllerFamilyChanged;

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
        if(action != ControllerAction.none) {
            HomeScreen.HandleControllerAction(action);
        }
    }

    private void ControllerService_ConnectionChanged(bool connected) {
        System.Diagnostics.Debug.WriteLine(connected ? "Controller connected" : "Controller disconnected");
    }

    private void ControllerService_ControllerFamilyChanged(ControllerFamily family) {

        HomeScreen.ControllerFamily = family;

        System.Diagnostics.Debug.WriteLine($"Controller family changed: {family}");
    }
}