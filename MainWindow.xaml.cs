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
using ControllerPlayground.Services.Data;

namespace ControllerPlayground;

public sealed partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        _ = InitializeDatabaseAsync();

        // Guide menu
        GuideMenu.ResumeGameRequested += GuideMenu_ResumeGameRequested;
        //game session service
        _gameSessionService.StateChanged += GameSessionService_StateChanged;
        _GamePageView.SessionService = _gameSessionService;

        _steamLaunchService.SessionService = _gameSessionService;

        // launch monitor
        _steamGameSessionCoordinator = new SteamGameSessionCoordinator(_steamLaunchService, _gameSessionService, _steamLocalService);
        _steamGameSessionCoordinator.GameStarted += SteamGameSessionMonitor_GameStarted;
        _steamGameSessionCoordinator.GameExited += SteamGameSessionMonitor_GameExited;

        //launch service
        _homeView.LaunchService = _steamLaunchService;
        _libraryView.LaunchService = _steamLaunchService;
        _GamePageView.LaunchService = _steamLaunchService;

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

        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ControllerInputNative.ControllerInput_SetWindowHandle(windowHandle);

        bool controllerInit = ControllerInputNative.ControllerInput_Initialize();

        System.Diagnostics.Debug.WriteLine(
            $"ControllerInputNative initialized: {controllerInit}");

        _controllerTimer.Interval = TimeSpan.FromMilliseconds(16);
        _controllerTimer.Tick += ControllerTimer_Tick;
        _controllerTimer.Start();

        Activated += (_, _) => { PlayArea.Focus(FocusState.Programmatic); 

            DispatcherQueue.TryEnqueue(() => {
                RestoreCurrentScreenFocus();
            });

        };

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

    private readonly GameWindowService _gameWindowService = new();

    // Steam services
    private readonly SteamSessionService _steamSessionService = new();
    private readonly SteamLaunchService _steamLaunchService = new();
    private readonly SteamGameSessionCoordinator _steamGameSessionCoordinator;
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

    // Database services
    private readonly ControllerPlaygroundDatabase _database = new();
    private readonly SteamArtworkCacheService _artworkCache = new();

    private void ControllerTimer_Tick(object? sender, object e) {
        ControllerAction action = _controllerService.PollAction();
        if (action == ControllerAction.none) {
            return;
        }
        // guide button overlay
        if (action == ControllerAction.Guide) {
            Debug.WriteLine("Guide requested");

            if (!_isGuideOpen && _gameSessionService.State == GameSessionState.Running) {
                if (AppWindow.Presenter is OverlappedPresenter presenter) {
                    presenter.Restore();
                }
                Activate();

                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

                _gameWindowService.TryFocusWindow(windowHandle);
            }
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

        // Focus Recovery
        object? focusedElement = FocusManager.GetFocusedElement(PlayArea.XamlRoot);

        if (focusedElement == null || focusedElement == PlayArea) { 
            Debug.WriteLine($"Controller focus lost on {_currentScreen}; restoring focus");

            RestoreCurrentScreenFocus();
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
        await _database.UpsertSteamGamesAsync(_steamLibraryService.Games);

        Debug.WriteLine($"Cached Steam games to SQLite: {_steamLibraryService.Games.Count}");

        Debug.WriteLine($"Starting Steam artwork cache for {_steamLibraryService.Games.Count} games");

        _ = _artworkCache.CacheLibraryCapsulesAsync(_steamLibraryService.Games);

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
    private void SteamGameSessionMonitor_GameStarted(uint appId) {
        Debug.WriteLine($"Steam game started: {appId}");
        DispatcherQueue.TryEnqueue(() => {
            if (AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.Minimize();
            }
            _ = RefreshRecentGameAfterLaunchAsync(appId);
            Debug.WriteLine("ControllerPlayground Minimized while game is running.");
        });
    }

    private void SteamGameSessionMonitor_GameExited(uint appId) {
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
        Debug.WriteLine($"Refreshing Steam library after game exit: {appId}");

        await LoadRecentSteamGamesAsync();

        Debug.WriteLine($"Steam recent games refreshed after game exit: {appId}");
    }

    private async Task RefreshRecentGameAfterLaunchAsync(uint appId) {
        for (int attempt = 0; attempt < 10; attempt++) {
            IReadOnlyDictionary<uint, DateTimeOffset> lastPlayed = await _steamLocalService.GetLastPlayedByAppIdAsync();

            if (lastPlayed.TryGetValue(appId, out DateTimeOffset timestamp) && timestamp >= DateTimeOffset.Now.AddMinutes(-2)) {
                Debug.WriteLine($"Steam LastPlayed updated for: {appId}");
                await LoadRecentSteamGamesAsync();
                return;
            }

            await Task.Delay(1000);

        }

        Debug.WriteLine($"Steam LastPlayed update Timed out for: {appId}");
    }
    private void GameSessionService_StateChanged(GameSessionState state, uint? appId) {
        Debug.WriteLine($"Game session state changed: {state} | AppId: {appId}");
        DispatcherQueue.TryEnqueue(() => {
            _GamePageView.UpdateGameSessionState(state, appId);
            string? gameTitle = null;
            if (appId.HasValue) {
                if (_GamePageView.Game?.SteamAppId == appId.Value) {
                    gameTitle = _GamePageView.Game?.Title;
                } else {
                    gameTitle = _steamLibraryService.Games.FirstOrDefault(game => game.AppId == appId.Value)?.Name;
                }
            }
            GuideMenu.UpdateGameSessionState(state, appId, gameTitle);
        });
    }
    private async void GuideMenu_ResumeGameRequested() {
        if (_gameSessionService.State != GameSessionState.Running || !_gameSessionService.CurrentAppId.HasValue)
            return;

        uint addId = _gameSessionService.CurrentAppId.Value;

        string? gamePath = await _steamLocalService.GetInstalledGamePathAsync(addId);

        _isGuideOpen = false;
        OverlayLayer.Visibility = Visibility.Collapsed;

        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.Minimize();
        }

        if (!string.IsNullOrWhiteSpace(gamePath)) {
            _gameWindowService.TryFocusGameWindow(gamePath);
        }
        Debug.WriteLine($"Resumed Game:{addId}");
    }
    private async Task InitializeDatabaseAsync() {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try {
            await _database.InitializeAsync();

            Debug.WriteLine($"ControllerPlayground database ready : {_database.DatabasePath}");

            IReadOnlyList<SteamLibraryGame> cachedGames = await _database.GetSteamGamesAsync();

            foreach (SteamLibraryGame game in cachedGames) {
                game.LibraryCapsuleUrl = _artworkCache.GetLibraryCapsuleSource(game);
            }
            Debug.WriteLine($"Loaded cached Steam games at startup: {cachedGames.Count}" + $"in {stopwatch.ElapsedMilliseconds} ms");

            if (cachedGames.Count == 0) {
                return;
            }

            IReadOnlyCollection<uint> installedAppIds = await _steamLocalService.GetInstalledAppIdsAsync();
            DispatcherQueue.TryEnqueue(() => {
                _libraryView.SetSteamLibraryGames(cachedGames, installedAppIds);
            });

        } catch (Exception ex) {
            Debug.WriteLine($"ControllerPlayground database initialization failed: {ex}");
        }
    }
}