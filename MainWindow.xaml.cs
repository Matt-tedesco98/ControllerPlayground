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
using ControllerPlayground.Models;
using ControllerPlayground.Services;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        _homeView.NavigateRequested += NavigateTo;
        _settingsView.NavigateRequested += NavigateTo;
        _GamePageView.NavigateRequested += NavigateTo;

        GuideMenu.NavigateRequested += GuideMenu_NavigationRequested;
        _homeView.GamePageRequested += HomeView_GamePageRequested;

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

        _ = LoadFriendsAsync();

        _steamSessionService.Connected += SteamSessionService_Connected;

        _steamSessionService.QrChallengeChanged += SteamSessionService_QrChallengeChanged;

        _steamSessionService.Authenticated += SteamSessionService_Authenticated;

        _steamSessionService.Connect();

        _steamChatService = new SteamChatService(_steamSessionService);

        FriendsOverlay.ChatService = _steamChatService;
    }

    private readonly DispatcherTimer _controllerTimer = new();
    private readonly ControllerService _controllerService = new();
    private readonly SteamSessionService _steamSessionService = new();
    private readonly ISteamChatService _steamChatService;
    private bool _isFriendsOpen;

    // Views
    private readonly HomeView _homeView = new();
    private readonly SettingsView _settingsView = new();
    private readonly GamePageView _GamePageView = new();

    private void ControllerTimer_Tick(object? sender, object e) {
        ControllerAction action = _controllerService.PollAction();
        if (action == ControllerAction.none) {
            return;
        }
        // guide button overlay
        if (action == ControllerAction.Guide) {
            System.Diagnostics.Debug.WriteLine("Guide requested");
            ToggleGuide();
            return;
        }
        if (_isGuideOpen) {
            if (action == ControllerAction.Back || action == ControllerAction.Guide) {
                ToggleGuide();
                return;
            }

            GuideMenu.HandleControllerAction(action);
            return;
        }
        // friends overlay
        if (action == ControllerAction.View) {
            ToggleFriendsOverlay();
            return;
        }
        if(_isFriendsOpen) {
            if (action == ControllerAction.Back) {
                if (FriendsOverlay.IsChatOpen) {
                    FriendsOverlay.CloseChatPane();
                    FriendsOverlay.RestoreFriendFocus();

                } else { 
                    CloseFriendsOverlay();
                }
                return;
            }

            if (action == ControllerAction.View) {
                ToggleFriendsOverlay();
                return;
            }
            FriendsOverlay.HandleControllerAction(action);
            return;
        }


        switch (_currentScreen) {
            case AppScreen.Home:
                _homeView.HandleControllerAction(action);
                break;
            case AppScreen.Settings:
                _settingsView.HandleControllerAction(action);
                break;
            case AppScreen.GamePage:
                _GamePageView.HandleControllerAction(action);
                break;
        }
    }


    private void ControllerService_ConnectionChanged(bool connected) {
        System.Diagnostics.Debug.WriteLine(connected ? "Controller connected" : "Controller disconnected");
    }

    private void ControllerService_ControllerFamilyChanged(ControllerFamily family) {

        _homeView.ControllerFamily = family;
        _GamePageView.controllerFamily = family;

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
            case AppScreen.GamePage:
                ScreenHost.Content = _GamePageView;
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

    private void HomeView_GamePageRequested(GameItem game, GamePageTab tab ) {
        _GamePageView.Game = game;
        _GamePageView.SelectedTab= tab;
        NavigateTo(AppScreen.GamePage);
    }

    private readonly ISteamLocalService _steamLocalService = new SteamLocalService();

    private async Task LoadFriendsAsync() { 
        var friends = await _steamLocalService.GetFriendsAsync();

        FriendsOverlay.Friends.Clear();

        foreach (var friend in friends) { 
            FriendsOverlay.Friends.Add(friend);
        }
    }

    private void ToggleFriendsOverlay() {
        _isFriendsOpen = !_isFriendsOpen;

        if (_isFriendsOpen) {
            FriendsOverlayLayer.Visibility = Visibility.Visible;
            FriendsOverlay.FocusFirstItem();
        } else { 
            FriendsOverlayLayer.Visibility = Visibility.Collapsed;
            _homeView.RestoreFocus();
        }
    }

    private void CloseFriendsOverlay() { 
        _isFriendsOpen = false;
        FriendsOverlayLayer.Visibility = Visibility.Collapsed;
        _homeView.RestoreFocus();
    }

    private async void SteamSessionService_Connected() {
        try {
            await _steamSessionService.BeginQrAuthenticationAsync();
        } catch(Exception ex) { 
            Debug.WriteLine($"Steam QR Authentication failed: {ex}");
        }
    }

    private void SteamSessionService_QrChallengeChanged(string challangeUrl) {
        DispatcherQueue.TryEnqueue(async () => {
            SteamQrOverlayLayer.Visibility = Visibility.Visible;
            await SteamQrLoginOverlay.SetChallangeUrlAsync(challangeUrl);
        });
        Debug.WriteLine($"Steam QR Challenge changed: {challangeUrl}");
    }

    private void SteamSessionService_Authenticated() {
        DispatcherQueue.TryEnqueue(() => {
            SteamQrOverlayLayer.Visibility = Visibility.Collapsed;
        });
    }
}