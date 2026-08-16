using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using Microsoft.UI.Xaml.Media;
using ControllerPlayground.Controls;
using ControllerPlayground.Navigation;
using ControllerPlayground.Views;
using Microsoft.UI.Xaml.Controls;
using ControllerPlayground.Overlays;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    private readonly DispatcherTimer _controllerTimer = new();
    private readonly ControllerService _controllerService = new();

    private readonly HomeView _homeView = new();
    private readonly SettingsView _settingsView = new();

    public MainWindow() {
        InitializeComponent();

        _homeView.NavigateRequested += NavigateTo;
        _settingsView.NavigateRequested += NavigateTo;

        GuideMenu.NavigateRequested += GuideMenu_NavigationRequested;

        NavigateTo(AppScreen.Home);


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
        if (action == ControllerAction.none) {
            return;
        }
        if (action == ControllerAction.Guide) {
            System.Diagnostics.Debug.WriteLine("Guide requested");
            ToggleGuide();
            return;
        }
        if (_isGuideOpen) {
            if (action == ControllerAction.Back) {
                ToggleGuide();
                return;
            }

            GuideMenu.HandleControllerAction(action);
            return;
        }
        switch (_currentScreen) {
            case AppScreen.Home:
                _homeView.HandleControllerAction(action);
                break;
            case AppScreen.Settings:
                _settingsView.HandleControllerAction(action);
                break;
        }
    }


    private void ControllerService_ConnectionChanged(bool connected) {
        System.Diagnostics.Debug.WriteLine(connected ? "Controller connected" : "Controller disconnected");
    }

    private void ControllerService_ControllerFamilyChanged(ControllerFamily family) {

        _homeView.ControllerFamily = family;

        System.Diagnostics.Debug.WriteLine($"Controller family changed: {family}");
    }

    private void NavigateTo(AppScreen screen) {
        _currentScreen = screen;
        switch (screen) {
            case AppScreen.Home:
                ScreenHost.Content = _homeView;
                break;

            case AppScreen.Settings:
                ScreenHost.Content = _settingsView;
                break;

        }
    }

    private AppScreen _currentScreen = AppScreen.Home;

    private bool _isGuideOpen;

    private void ToggleGuide() {
        _isGuideOpen = !_isGuideOpen;

        OverlayLayer.Visibility = _isGuideOpen ? Visibility.Visible : Visibility.Collapsed;

        if (_isGuideOpen) {
            DispatcherQueue.TryEnqueue(() => {
                GuideMenu.FocusFirstItem();
            });
        } else {
            OverlayLayer.Visibility = Visibility.Collapsed;
            _homeView.RestoreFocus();
        };
    }

    private void GuideMenu_NavigationRequested(AppScreen screen) {
        _isGuideOpen = false;
        OverlayLayer.Visibility = Visibility.Collapsed;
        NavigateTo(screen);
    }
}