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
using System.Linq;
using ControllerPlayground.Services.Steam.SteamKit;
using System.Collections.Generic;
using ControllerPlayground.Services.Steam;
using Microsoft.UI.Windowing;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();


        //game session service
        _gameSessionService.StateChanged += GameSessionService_StateChanged;
        _GamePageView.SessionService = _gameSessionService;

        _steamLaunchService.SessionService = _gameSessionService;

        // launch monitor
        _steamGameSessionMonitor = new SteamGameSessionMonitor(_steamLocalService);
        _steamGameSessionMonitor.GameStarted += SteamGameSessionMonitor_GameStarted;
        _steamGameSessionMonitor.GameExited += SteamGameSessionMonitor_GameExited;
        //launch service
        _homeView.LaunchService = _steamLaunchService;
        _libraryView.LaunchService = _steamLaunchService;
        _GamePageView.LaunchService = _steamLaunchService;
        _steamLaunchService.GameLaunchRequested += SteamLaunchService_GameLaunchRequested;

        //views
        _homeView.NavigateRequested += NavigateTo;
        _settingsView.NavigateRequested += NavigateTo;
        _GamePageView.NavigateRequested += NavigateTo;

        GuideMenu.NavigateRequested += GuideMenu_NavigationRequested;
        _homeView.GamePageRequested += HomeView_GamePageRequested;
        _libraryView.GamePageRequested += HomeView_GamePageRequested;

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

        _steamChatService = new SteamChatService(_steamSessionService);

        _steamAppInfoService = new SteamAppInfoService(_steamSessionService);

        _steamLibraryService = new SteamLibraryService(_steamSessionService, _steamAppInfoService);

        _GamePageView.SteamLibraryService = _steamLibraryService;

        _steamLibraryService.LibraryLoaded += SteamLibraryService_LibraryLoaded;

        FriendsOverlay.ChatService = _steamChatService;

        _steamChatService.UnreadCountChanged += SteamChatService_UnreadCountChanged;

        _ = LoadFriendsAsync();

        _steamSessionService.Connected += SteamSessionService_Connected;

        _steamSessionService.QrChallengeChanged += SteamSessionService_QrChallengeChanged;

        _steamSessionService.Authenticated += SteamSessionService_Authenticated;

        _steamSessionService.SavedAuthenticationFailed += SteamSessionService_SavedAuthenticationFailed;

        _steamSessionService.Connect();

    }

    private readonly DispatcherTimer _controllerTimer = new();
    private readonly ControllerService _controllerService = new();
    // Steam services
    private readonly SteamSessionService _steamSessionService = new();
    private readonly SteamLaunchService _steamLaunchService = new();
    private readonly SteamGameSessionMonitor _steamGameSessionMonitor;
    private readonly ISteamChatService _steamChatService;
    private readonly SteamLibraryService _steamLibraryService;
    private readonly SteamAppInfoService _steamAppInfoService;
    private readonly GameSessionService _gameSessionService = new();
    // friends overlay
    private bool _isFriendsOpen;
    private int _totalUnreadMessages;

    // Views
    private readonly HomeView _homeView = new();
    private readonly SettingsView _settingsView = new();
    private readonly GamePageView _GamePageView = new();
    private readonly LibraryView _libraryView = new();

    private void ControllerTimer_Tick(object? sender, object e) {
        ControllerAction action = _controllerService.PollAction();
        if (action == ControllerAction.none) {
            return;
        }
        // guide button overlay
        if (action == ControllerAction.Guide) {
            Debug.WriteLine("Guide requested");
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
        if (_isFriendsOpen) {
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

        // handle back
        if (action == ControllerAction.Back) {
            if (_currentScreen == AppScreen.Home && _homeView.IsContextMenuOpen) {
                _homeView.HandleControllerAction(action);
                return;
            }
            if (_currentScreen == AppScreen.Library && _libraryView.IsContextMenuOpen) {
                _libraryView.HandleControllerAction(action);
                return;
            }
            if (_currentScreen == AppScreen.GamePage && _GamePageView.IsKnownIssuesPageOpen) {
                _GamePageView.CloseKnownIssuesPage();
                return;
            }
            if (_currentScreen == AppScreen.GamePage && _GamePageView.IsSupportPageOpen) {
                _GamePageView.CloseNativeSupportPage();
                return;
            }
            GoBack();
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
            case AppScreen.Library:
                _libraryView.HandleControllerAction(action);
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

    private readonly Stack<AppScreen> _navigationHistory = new();
    private AppScreen _currentScreen = AppScreen.Home;
    private void NavigateTo(AppScreen screen) {
        NavigateTo(screen, true);
    }

    private void NavigateTo(AppScreen screen, bool addToHistory) {
        if (addToHistory && screen != _currentScreen) {
            _navigationHistory.Push(_currentScreen);
        }
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
            case AppScreen.Library:
                ScreenHost.Content = _libraryView;

                if (_steamLibraryService.Games.Count == 0) {
                    _ = _steamLibraryService.LoadFullLibraryAsync();
                }
                break;
        }
    }

    private void GoBack() { 
        if (_navigationHistory.Count == 0)
            return;
        AppScreen previousScreen = _navigationHistory.Pop();
        NavigateTo(previousScreen, false);
    }

    private bool _isGuideOpen;
    
    private void RestoreCurrentScreenFocus() {
        switch (_currentScreen) {
            case AppScreen.Home:
                _homeView.RestoreFocus();
                break;
            case AppScreen.Settings:
                PlayArea.Focus(FocusState.Programmatic);
                break;
            case AppScreen.GamePage:
                _GamePageView.FocusInitialElement();
                break;
            case AppScreen.Library:
                _libraryView.RestoreFocus();
                break;
        }
    }
    private void ToggleGuide() {
        _isGuideOpen = !_isGuideOpen;


        if (_isGuideOpen) {

            OverlayLayer.Visibility = _isGuideOpen ? Visibility.Visible : Visibility.Collapsed;

            DispatcherQueue.TryEnqueue(() => {
                GuideMenu.FocusFirstItem();
            });
        } else {
            OverlayLayer.Visibility = Visibility.Collapsed;
            RestoreCurrentScreenFocus();
        }
        ;
    }

    private void HomeView_GamePageRequested(GameItem game, GamePageTab tab) {
        _GamePageView.Game = game;
        _GamePageView.SelectedTab = tab;
        NavigateTo(AppScreen.GamePage);
    }

    private readonly ISteamLocalService _steamLocalService = new SteamLocalService();

    private async Task LoadFriendsAsync() {
        var friends = await _steamLocalService.GetFriendsAsync();

        FriendsOverlay.Friends.Clear();

        foreach (var friend in friends) {
            FriendsOverlay.Friends.Add(friend);
        }
        //#if DEBUG
        //        if (_steamChatService is SteamChatService steamChatService &&
        //            FriendsOverlay.Friends.Count > 0) {
        //            steamChatService.SimulateIncomingMessage(
        //                FriendsOverlay.Friends[0].SteamId,
        //                "Global unread badge test");
        //        }
        //#endif
    }

    private void ToggleFriendsOverlay() {
        _isFriendsOpen = !_isFriendsOpen;

        if (_isFriendsOpen) {
            FriendsOverlayLayer.Visibility = Visibility.Visible;
            FriendsOverlay.FocusFirstItem();
        } else {
            FriendsOverlayLayer.Visibility = Visibility.Collapsed;
            RestoreCurrentScreenFocus();
        }
    }

    private void CloseFriendsOverlay() {
        _isFriendsOpen = false;
        FriendsOverlayLayer.Visibility = Visibility.Collapsed;
        RestoreCurrentScreenFocus();
    }

    private async void SteamSessionService_Connected() {
        try {
            bool startedSavedLogin = await _steamSessionService.TrySavedAuthenticationAsync();
            if (!startedSavedLogin) {
                await _steamSessionService.BeginQrAuthenticationAsync();
            }
        } catch (Exception ex) {
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
        _ = LoadRecentSteamGamesAsync();
    }

    private async void SteamSessionService_SavedAuthenticationFailed() {
        try {
            if (_steamSessionService.IsConnected) {
                await _steamSessionService.BeginQrAuthenticationAsync();
            }
        } catch (Exception ex) {
            Debug.WriteLine($"Steam QR fallback failed: {ex}");
        }
    }

    private void SteamChatService_UnreadCountChanged(string steamId, int unreadCount) {
        DispatcherQueue.TryEnqueue(() => {
            _totalUnreadMessages = FriendsOverlay.Friends.Sum(friend => friend.UnreadCount);

            SteamUnreadCountText.Text = _totalUnreadMessages.ToString();

            SteamUnreadBadge.Visibility = _totalUnreadMessages > 0 ? Visibility.Visible : Visibility.Collapsed;

            Debug.WriteLine($"Total unread messages: {_totalUnreadMessages}");
        });
    }

    private async Task LoadRecentSteamGamesAsync() {
        try {
            IReadOnlyDictionary<uint, DateTimeOffset> lastPlayedByAppId = await _steamLocalService.GetLastPlayedByAppIdAsync();

            DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-30);

            List<uint> recentAppIds = lastPlayedByAppId.Where(entry => entry.Value >= cutoff).OrderByDescending(entry => entry.Value).Select(entry => entry.Key).ToList();

            Debug.WriteLine($"Recent Steam app IDs in the last 30 days: {recentAppIds.Count}");

            IReadOnlyList<SteamLibraryGame> games = await _steamLibraryService.GetGamesAsync(recentAppIds);

            List<SteamLibraryGame> recentGames = games.OrderByDescending(game => lastPlayedByAppId[game.AppId]).ToList();

            Debug.WriteLine($"Recent Steam Games loaded: {recentGames.Count}");

            foreach (SteamLibraryGame game in recentGames) {
                Debug.WriteLine($"Recent: {game.Name} | " +
                $"{lastPlayedByAppId[game.AppId].LocalDateTime}");
            }

            DispatcherQueue.TryEnqueue(() => {
                _homeView.SetSteamLibraryGames(recentGames);
            });

        } catch (Exception ex) {
            Debug.WriteLine($"Failed to load recent Steam games: {ex}");
        }
    }

    private async void SteamLibraryService_LibraryLoaded(int gameCount) {
        IReadOnlyCollection<uint> installedAppIds = await _steamLocalService.GetInstalledAppIdsAsync();
        DispatcherQueue.TryEnqueue(() => {
            _libraryView.SetSteamLibraryGames(_steamLibraryService.Games, installedAppIds);
        });
    }
    private void GuideMenu_NavigationRequested(AppScreen screen) {
        _isGuideOpen = false;
        OverlayLayer.Visibility = Visibility.Collapsed;
        NavigateTo(screen);
    }
    private async void SteamLaunchService_GameLaunchRequested(uint appId) {
        Debug.WriteLine($"ControllerPlayground observed Steam game launch: {appId}");

        _steamGameSessionMonitor.StartMonitoring(appId);
    }

    private void SteamGameSessionMonitor_GameStarted(uint appId) {
        _gameSessionService.MarkRunning(appId);
        Debug.WriteLine($"Steam game started: {appId}");
        DispatcherQueue.TryEnqueue(() => {
            if (AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.Minimize();
            }
            Debug.WriteLine("ControllerPlayground Minimized while game is running.");
        });
    }

    private void SteamGameSessionMonitor_GameExited(uint appId) {
        _gameSessionService.EndSession(appId);
        Debug.WriteLine($"Steam game exited: {appId}");
        DispatcherQueue.TryEnqueue(() => {
            if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.Restore();
        }
            RestoreCurrentScreenFocus();
            Debug.WriteLine("ControllerPlayground restored after game exited.");
        });
        _ = RefreshAfterGameExitAsync(appId);
    }
    private async Task RefreshAfterGameExitAsync(uint appId) {
        await Task.Delay(2000);
        Debug.WriteLine($"Refreshing Steam library after game exit: {appId}");
        await LoadRecentSteamGamesAsync();
        Debug.WriteLine($"Steam recent games refreshed after game exit: {appId}");
    }
    private void GameSessionService_StateChanged(GameSessionState state, uint? appId) {
        Debug.WriteLine($"Game session state changed: {state} | AppId: {appId}");
        DispatcherQueue.TryEnqueue(() => {
            _GamePageView.UpdateGameSessionState(state, appId);
        });
    }
}