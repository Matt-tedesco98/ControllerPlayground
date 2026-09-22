using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ControllerPlayground.Models;
using ControllerPlayground.Navigation;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ControllerPlayground.Services;
using System.Diagnostics;
using System.Threading.Tasks;
using ControllerPlayground.Services.Steam;
using Windows.System;
using ControllerPlayground.Services.Steam.SteamKit;
using System.Xml.Serialization;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GamePageView : UserControl {
        public GamePageView() {
            InitializeComponent();

            Loaded += GamePageView_Loaded;
        }

        //Services
        public readonly ISteamService _steamService = new SteamService();
        private readonly SteamLaunchService _steamLaunchService = new();
        private readonly SteamLocalService _steamLocalService = new();
        internal SteamLibraryService? SteamLibraryService { get; set; }
        private bool _isSupportPageOpen;
        internal bool IsSupportPageOpen => _isSupportPageOpen;

        //DLC state
        private IReadOnlyList<uint> _currentDlcAppIds = Array.Empty<uint>();
        private uint? _loadedDlcForAppId;
        private bool _isDlcLoading;
        private bool _dlcInfoLoaded;

        internal void FocusInitialElement() {
            DispatcherQueue.TryEnqueue(() => {
                if (SelectedTab == GamePageTab.Activity) {
                    PlayButton.Focus(FocusState.Programmatic);
                } else {
                    GetSelectedTabButton().Focus(FocusState.Programmatic);
                }
            });
        }

        public static DependencyProperty SelectedTabProperty = DependencyProperty.Register(
            nameof(SelectedTab),
            typeof(GamePageTab),
            typeof(GamePageView),
            new PropertyMetadata(GamePageTab.Activity, OnSelectedTabChange));

        private static void OnSelectedTabChange(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is GamePageView view) {
                view.UpdateSelectedTab();
            }
        }

        private void UpdateSelectedTab() {
            ActivityTabButton.Opacity = SelectedTab == GamePageTab.Activity ? 1.0 : 0.5;
            YourStuffTabButton.Opacity = SelectedTab == GamePageTab.YourStuff ? 1.0 : 0.5;
            CommunityTabButton.Opacity = SelectedTab == GamePageTab.Community ? 1.0 : 0.5;
            GameInfoTabButton.Opacity = SelectedTab == GamePageTab.GameInfo ? 1.0 : 0.5;

            ActivityContent.Visibility = SelectedTab == GamePageTab.Activity ? Visibility.Visible : Visibility.Collapsed;

            YourStuffContent.Visibility = SelectedTab == GamePageTab.YourStuff ? Visibility.Visible : Visibility.Collapsed;
            if (SelectedTab == GamePageTab.YourStuff)
                _ = EnsureDlcLoadedAsync();

            CommunityContent.Visibility = SelectedTab == GamePageTab.Community ? Visibility.Visible : Visibility.Collapsed;

            if (SelectedTab != GamePageTab.GameInfo) {
                _isSupportPageOpen = false;
            }
            GameInfoContent.Visibility = SelectedTab == GamePageTab.GameInfo && !_isSupportPageOpen ? Visibility.Visible : Visibility.Collapsed;
            SupportContent.Visibility = SelectedTab == GamePageTab.GameInfo && !_isSupportPageOpen ? Visibility.Collapsed : Visibility.Visible;

            ActivityTabIndicator.Visibility = SelectedTab == GamePageTab.Activity ? Visibility.Visible : Visibility.Collapsed;
            YourStuffTabIndicator.Visibility = SelectedTab == GamePageTab.YourStuff ? Visibility.Visible : Visibility.Collapsed;
            CommunityTabIndicator.Visibility = SelectedTab == GamePageTab.Community ? Visibility.Visible : Visibility.Collapsed;
            GameInfoTabIndicator.Visibility = SelectedTab == GamePageTab.GameInfo ? Visibility.Visible : Visibility.Collapsed;
        }

        public GamePageTab SelectedTab {
            get => (GamePageTab)GetValue(SelectedTabProperty);
            set => SetValue(SelectedTabProperty, value);
        }

        public static readonly DependencyProperty GameProperty = DependencyProperty.Register(
            nameof(Game),
            typeof(GameItem),
            typeof(GamePageView),
            new PropertyMetadata(null, OnGameChanged));

        public GameItem? Game {
            get => (GameItem?)GetValue(GameProperty);
            set => SetValue(GameProperty, value);
        }

        private static void OnGameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is GamePageView view) {
                view.ResetDlcState();
                view.UpdateGameVisuals();
            }
        }

        private void ResetDlcState() {
            _currentDlcAppIds = Array.Empty<uint>();
            _loadedDlcForAppId = null;
            _isDlcLoading = false;
            OwnedDlcList.ItemsSource = null;
            OtherDlcList.ItemsSource = null;
            OtherDlcHeader.Visibility = Visibility.Collapsed;
            OtherDlcList.Visibility = Visibility.Collapsed;
            DlcLoadingPanel.Visibility = Visibility.Collapsed;
            _dlcInfoLoaded = false;
            NoDlcText.Visibility = Visibility.Collapsed;
            OwnedDlcList.Visibility = Visibility.Visible;
            OwnedDlcHeader.Visibility = Visibility.Visible;
        }

        private void UpdateGameVisuals() {
            GameItem? game = Game;

            if (game == null) {
                HeroImage.Source = null;
                CoverImage.Source = null;
                GameLogoImage.Source = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(game.HeroImagePath)) {
                HeroImage.Source = new BitmapImage(new Uri(game.HeroImagePath));

            } else {
                HeroImage.Source = null;
            }

            if (!string.IsNullOrWhiteSpace(game.CoverImagePath)) {
                CoverImage.Source = new BitmapImage(new Uri(game.CoverImagePath));
            } else {
                CoverImage.Source = null;
            }

            GameLogoImage.Source = null;
            GameLogoImage.Visibility = Visibility.Collapsed;
            GameTitleText.Visibility = Visibility.Visible;

            if (!string.IsNullOrWhiteSpace(game.LogoImagePath)) {
                GameLogoImage.Source = new BitmapImage(new Uri(game.LogoImagePath));
            }
        }
        private void GameLogoImage_ImageOpened(object sender, RoutedEventArgs e) {
            GameLogoImage.Visibility = Visibility.Visible;
            GameTitleText.Visibility = Visibility.Collapsed;
        }
        private void GameLogoImage_ImageFailed(object sender, RoutedEventArgs e) {
            GameLogoImage.Source = null;

            GameLogoImage.Visibility = Visibility.Collapsed;
            GameTitleText.Visibility = Visibility.Visible;

            Debug.WriteLine($"Steam library logo failed to load: {Game?.SteamAppId}");
        }

        internal event Action<AppScreen>? NavigateRequested;

        public ControllerFamily controllerFamily {
            get => PromptBar.Family;
            set {
                PromptBar.Family = value;
                LeftShoulderGlyph.Family = value;
                RightShoulderGlyph.Family = value;
            }
        }

        internal void HandleControllerAction(ControllerAction action) {
            switch (action) {
                case ControllerAction.PreviousTab: {
                        SelectedTab = SelectedTab switch {
                            GamePageTab.YourStuff => GamePageTab.Activity,
                            GamePageTab.Community => GamePageTab.YourStuff,
                            GamePageTab.GameInfo => GamePageTab.Community,
                            _ => GamePageTab.Activity
                        };

                        GetSelectedTabButton()
                            .Focus(FocusState.Programmatic);

                        break;
                    }

                case ControllerAction.NextTab: {
                        SelectedTab = SelectedTab switch {
                            GamePageTab.Activity => GamePageTab.YourStuff,
                            GamePageTab.YourStuff => GamePageTab.Community,
                            GamePageTab.Community => GamePageTab.GameInfo,
                            _ => GamePageTab.GameInfo
                        };

                        GetSelectedTabButton()
                            .Focus(FocusState.Programmatic);

                        break;
                    }
                case ControllerAction.Back:
                    NavigateRequested?.Invoke(AppScreen.Home);
                    break;
                case ControllerAction.NavigateDown: {
                        if (MoveDlcFocus(1)) {
                            break;
                        }
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions {
                            SearchRoot = PageRoot
                        });
                    }
                    break;
                case ControllerAction.NavigateUp: {
                        if (MoveDlcFocus(-1)) {
                            break;
                        }
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Up, new FindNextElementOptions {
                            SearchRoot = PageRoot
                        });
                    }
                    break;
                case ControllerAction.NavigateRight: {
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Right, new FindNextElementOptions {
                            SearchRoot = PageRoot
                        });
                    }
                    break;
                case ControllerAction.NavigateLeft: {
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Left, new FindNextElementOptions {
                            SearchRoot = PageRoot
                        });
                    }
                    break;

                case ControllerAction.Accept: {
                        object? focused = FocusManager.GetFocusedElement(PageRoot.XamlRoot);
                        if (focused == PlayButton) {
                            _ = LaunchCurrentGameAsync();
                            break;
                        }
                        if (focused == StorePageButton) {
                            _ = OpenStorePageAsync();
                            break;
                        }
                        if (focused == SupportButton) {
                            OpenNativeSupportPage();
                            break;
                        }
                        if (focused == SteamSupportButton) {
                            _ = OpenSupportPageAsync();
                            break;
                        }
                        if (focused is ListViewItem dlcItem) {
                            int ownedIndex =
                                OwnedDlcList.IndexFromContainer(
                                    dlcItem);

                            if (ownedIndex >= 0 &&
                                OwnedDlcList.Items[ownedIndex]
                                    is SteamDlcItem ownedDlc) {
                                _ = OpenDlcStorePageAsync(
                                    ownedDlc.AppId);

                                break;
                            }

                            int otherIndex =
                                OtherDlcList.IndexFromContainer(
                                    dlcItem);

                            if (otherIndex >= 0 &&
                                OtherDlcList.Items[otherIndex]
                                    is SteamDlcItem otherDlc) {
                                _ = OpenDlcStorePageAsync(
                                    otherDlc.AppId);

                                break;
                            }
                        }
                        if (focused == CommunityHubButton) {
                            _ = OpenComminityHubAsync();
                            break;
                        }
                        if (focused == DiscussionsButton) {
                            _ = OpenDiscussionsAsync();
                            break;
                        }
                        if (focused == GuidesButton) {
                            _ = OpenGuidesAsync();
                            break;
                        }
                        if (focused == VerifyGameFilesButton) {
                            _ = VerifyGameFilesAsync();
                            break;
                        }
                        if (focused == BrowseLocalFilesButton) {
                            _ = BrowseLocalFilesAsync();
                            break;
                        }
                        if(focused == ControllerHelpButton) {
                            OpenControllerTroubleshooting();
                            break;
                        }
                        if (focused is Button button && IsTabButton(button)) {
                            SelectTab(button);
                        }
                    }
                    break;


            }
        }
        private Button GetSelectedTabButton() {
            return SelectedTab switch {
                GamePageTab.Activity => ActivityTabButton,
                GamePageTab.YourStuff => YourStuffTabButton,
                GamePageTab.Community => CommunityTabButton,
                GamePageTab.GameInfo => GameInfoTabButton,
                _ => ActivityTabButton
            };
        }

        private bool IsTabButton(Control control) {
            return control == ActivityTabButton || control == YourStuffTabButton || control == CommunityTabButton || control == GameInfoTabButton;
        }

        private void PlayButton_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Opacity = 1.0;
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255));
            }
        }

        private void PlayButton_LostFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Opacity = 0.8;
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
            }
        }

        private async void GamePageView_Loaded(object sender, RoutedEventArgs e) {
            if (SelectedTab == GamePageTab.Activity) {
                PlayButton.Focus(FocusState.Programmatic);
            } else {
                GetSelectedTabButton().Focus(FocusState.Programmatic);
            }
            _ = LoadSteamServiceAsync();

            SteamLocalService localSteam = new();

            Debug.WriteLine($"Steam install path: {localSteam.GetSteamInstallPath()}");

            Debug.WriteLine($"Steam active user: {localSteam.GetActiveUserId()}");

            string? configPath = localSteam.GetActiveUserConfigPath();

            Debug.WriteLine($"Steam local config: {configPath}");
            Debug.WriteLine($"Config exists: {File.Exists(configPath)}");

            Debug.WriteLine($"SteamID64: {localSteam.GetActiveSteamId64()}");


            if (configPath != null && File.Exists(configPath)) {
                VdfNode root = VdfParser.ParseFile(configPath);

                foreach (string key in root.Children.Keys) {
                    Debug.WriteLine($"VDF root: {key}");
                }

                if (root.TryGetChild("UserLocalConfigStore", out VdfNode? userConfig) &&
    userConfig != null &&
    userConfig.TryGetChild("friends", out VdfNode? friends1) && friends1 != null) {
                    Debug.WriteLine($"Friend entries: {friends1.Children.Count}");

                    foreach (var friend in friends1.Children.Take(3)) {
                        Debug.WriteLine($"Friend ID: {friend.Key}");

                        foreach (string field in friend.Value.Children.Keys) {
                            Debug.WriteLine($"  Field: {field}");
                        }
                    }
                }
            }

            var friends = await localSteam.GetFriendsAsync();

            Debug.WriteLine($"Local Steam friends: {friends.Count}");

            foreach (SteamFriend friend in friends.Take(5)) {
                Debug.WriteLine($"Friend: {friend.DisplayName} ({friend.SteamId})");
                Debug.WriteLine($"Friend: {friend.DisplayName} | Avatar: {friend.AvatarUrl}");
            }
        }

        private void PageActionButton_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Opacity = 1.0;
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255));
            }
        }

        private void PageActionButton_LostFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Opacity = 0.8;
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
            }
        }

        private void UpdateSelectedTabFromFocus() {
            var focused = FocusManager.GetFocusedElement(XamlRoot) as Control;
            if (focused == ActivityTabButton) {
                SelectedTab = GamePageTab.Activity;
            } else if (focused == YourStuffTabButton) {
                SelectedTab = GamePageTab.YourStuff;
            } else if (focused == CommunityTabButton) {
                SelectedTab = GamePageTab.Community;
            } else if (focused == GameInfoTabButton) {
                SelectedTab = GamePageTab.GameInfo;
            }
        }

        private async Task LoadSteamServiceAsync() {
            uint? appId = Game?.SteamAppId;

            if (appId == null)
                return;
            // Load last played date
            IReadOnlyDictionary<uint, DateTimeOffset> lastPlayedByApp = await _steamLocalService.GetLastPlayedByAppIdAsync();
            if (lastPlayedByApp.TryGetValue(appId.Value, out DateTimeOffset lastPlayed)) {
                Game.LastPlayed = $"Last Played: {lastPlayed.LocalDateTime:MMM d, yyyy}";
                LastPlayedText.Text = Game.LastPlayed;
            } else {
                Game.LastPlayed = "Last Played: Never";
                LastPlayedText.Text = Game.LastPlayed;
            }

            // Total playtime
            IReadOnlyDictionary<uint, int> playtimeByApp = await _steamLocalService.GetPlaytimeByAppIdAsync();
            if (playtimeByApp.TryGetValue(appId.Value, out int playtimeMinutes)) {
                int hours = playtimeMinutes / 60;
                int minutes = playtimeMinutes % 60;
                Game.PlayTime = hours > 0
                    ? $"{hours}h {minutes}m played"
                    : $"{minutes}m played";
                PlayTimeText.Text = Game.PlayTime;
            } else {
                Game.PlayTime = "No playtime";
                PlayTimeText.Text = Game.PlayTime;
            }

            var items = await _steamService.GetGameActivityAsync(appId.Value);

            SteamStoreDetails storeDetails = await _steamService.GetGameStoreDetailsAsync(appId.Value);

            //dlc
            _currentDlcAppIds = storeDetails.DlcAppIds;
            _dlcInfoLoaded = true;
            if (_currentDlcAppIds.Count == 0) {
                OwnedDlcHeader.Visibility = Visibility.Collapsed;
                OwnedDlcList.Visibility = Visibility.Collapsed;
                OtherDlcHeader.Visibility = Visibility.Collapsed;
                OtherDlcList.Visibility = Visibility.Collapsed;
                NoDlcText.Visibility = Visibility.Visible;
            }
            if (SelectedTab == GamePageTab.YourStuff) {
                await EnsureDlcLoadedAsync();
            }

            Game.Description = storeDetails.Description;

            Game.Genres.Clear();
            foreach (string genre in storeDetails.Genres) {
                Game.Genres.Add(genre);
            }
            DescriptionText.Text = storeDetails.Description;
            GenresList.ItemsSource = Game.Genres;

            Game.ControllerSupport = storeDetails.ControllerSupport;
            ControllerSupportText.Text = string.IsNullOrWhiteSpace(Game.ControllerSupport)
                ? "Controller support information not available"
                : Game.ControllerSupport;

            Game.ActivityData.ActivityFeed.Clear();

            foreach (ActivityFeedItem item in items) {
                Game.ActivityData.ActivityFeed.Add(item);
            }

            Debug.WriteLine($"Steam returned {items.Count} news items.");
        }
        private async Task LoadDlcAsync(
    IReadOnlyList<uint> dlcAppIds) {
            if (SteamLibraryService == null ||
                dlcAppIds.Count == 0) {
                return;
            }

            IReadOnlyList<SteamDlcItem> dlcItems =
                await SteamLibraryService.GetDlcAsync(
                    dlcAppIds);

            IReadOnlyCollection<uint> ownedAppIds =
                await SteamLibraryService
                    .GetOwnedAppIdsAsync();

            List<SteamDlcItem> ownedDlc =
                dlcItems
                    .Where(dlc =>
                        ownedAppIds.Contains(dlc.AppId))
                    .ToList();

            List<SteamDlcItem> otherDlc =
                dlcItems
                    .Where(dlc =>
                        !ownedAppIds.Contains(dlc.AppId))
                    .ToList();

            OwnedDlcList.ItemsSource =
                ownedDlc;

            OtherDlcList.ItemsSource =
                otherDlc;

            OtherDlcHeader.Visibility =
                otherDlc.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            OtherDlcList.Visibility =
                otherDlc.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        private async Task EnsureDlcLoadedAsync() {
            uint? appId = Game?.SteamAppId;
            if (_dlcInfoLoaded && _currentDlcAppIds.Count == 0) {
                NoDlcText.Visibility =
                    Visibility.Visible;

                return;
            }
            if (appId == null || _currentDlcAppIds.Count == 0 || _isDlcLoading || _loadedDlcForAppId == appId.Value)
                return;
            _isDlcLoading = true;
            DlcLoadingPanel.Visibility = Visibility.Visible;
            OwnedDlcHeader.Visibility = Visibility.Collapsed;
            OwnedDlcList.Visibility = Visibility.Collapsed;
            OtherDlcList.Visibility = Visibility.Collapsed;
            OtherDlcHeader.Visibility = Visibility.Collapsed;
            try {
                await LoadDlcAsync(_currentDlcAppIds);
                _loadedDlcForAppId = appId.Value;
            } finally {
                DlcLoadingPanel.Visibility = Visibility.Collapsed;
                OwnedDlcHeader.Visibility = Visibility.Visible;
                OwnedDlcList.Visibility = Visibility.Visible;
                _isDlcLoading = false;
            }
        }

        private async Task LaunchCurrentGameAsync() {
            uint? addId = Game?.SteamAppId;
            if (addId == null) {
                Debug.WriteLine("Cannot launch game: Steam AppId is missing");
                return;
            }
            await _steamLaunchService.LaunchGameAsync(addId.Value);
        }

        private async Task OpenStorePageAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot open store page: Steam AppId is missing");
                return;
            }
            Uri storeUri = new Uri($"steam://store/{appId.Value}");
            bool opened = await Launcher.LaunchUriAsync(storeUri);

            Debug.WriteLine(
                opened
            ? $"Steam store page opened: {appId.Value}"
            : $"Steam store page failed: {appId.Value}");
        }

        private async Task OpenSupportPageAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot open support page: Steam AppId is missing");
                return;
            }
            Uri supportUrl = new($"https://help.steampowered.com/en/wizard/HelpWithGame/?appid={appId.Value}");
            Uri steamUrl = new($"steam://openurl/{supportUrl}");
            bool opened = await Launcher.LaunchUriAsync(steamUrl);

            Debug.WriteLine(
                opened
                ? $"Steam support page opened: {appId.Value}"
                : $"Steam support page failed: {appId.Value}");
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e) {
            await LaunchCurrentGameAsync();
        }

        private void SelectTab(Button button) {
            if (button == ActivityTabButton) {
                SelectedTab = GamePageTab.Activity;
            } else if (button == YourStuffTabButton) {
                SelectedTab = GamePageTab.YourStuff;
            } else if (button == CommunityTabButton) {
                SelectedTab = GamePageTab.Community;
            } else if (button == GameInfoTabButton) {
                SelectedTab = GamePageTab.GameInfo;
            }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                SelectTab(button);
            }
        }

        private async void StorePageButton_Click(object sender, RoutedEventArgs e) {
            await OpenStorePageAsync();
        }

        private async void SupportButton_Click(object sender, RoutedEventArgs e) {
            OpenNativeSupportPage();
        }

        private async void SteamSupportButton_Click(object sender, RoutedEventArgs e) {
            await OpenSupportPageAsync();
        }

        private void OpenNativeSupportPage() {
            _isSupportPageOpen = true;

            GameInfoContent.Visibility = Visibility.Collapsed;
            SupportContent.Visibility = Visibility.Visible;

            DispatcherQueue.TryEnqueue(() => {
                VerifyGameFilesButton.Focus(FocusState.Programmatic);
            });

        }

        internal void CloseNativeSupportPage() {
            if (!_isSupportPageOpen)
                return;
            _isSupportPageOpen = false;
            SupportContent.Visibility = Visibility.Collapsed;
            GameInfoContent.Visibility = Visibility.Visible;

            DispatcherQueue.TryEnqueue(() => {
                SupportButton.Focus(FocusState.Programmatic);
            });
        }
        private async Task OpenDlcStorePageAsync(uint dlcAppId) {
            Uri storeUri = new($"steam://store/{dlcAppId}");
            bool opened = await Launcher.LaunchUriAsync(storeUri);
            Debug.WriteLine(
                opened
                ? $"Steam DLC store page opened: {dlcAppId}"
                : $"Steam DLC store page failed: {dlcAppId}");
        }
        private bool MoveDlcFocus(int direction) {
            if (SelectedTab != GamePageTab.YourStuff)
                return false;

            object? focused =
                FocusManager.GetFocusedElement(
                    PageRoot.XamlRoot);

            // From the Your Stuff tab, Down enters the DLC list.
            if (focused == YourStuffTabButton) {
                if (direction > 0 &&
                    OwnedDlcList.Items.Count > 0) {
                    FocusDlcItem(
                        OwnedDlcList,
                        0);

                    return true;
                }

                return false;
            }

            if (focused is not ListViewItem focusedItem)
                return false;

            int ownedIndex =
                OwnedDlcList.IndexFromContainer(
                    focusedItem);

            if (ownedIndex >= 0) {
                int nextIndex =
                    ownedIndex + direction;

                if (nextIndex >= 0 &&
                    nextIndex < OwnedDlcList.Items.Count) {
                    FocusDlcItem(
                        OwnedDlcList,
                        nextIndex);

                    return true;
                }

                // Up from the first DLC returns to the tab.
                if (direction < 0 &&
                    ownedIndex == 0) {
                    OwnedDlcList.SelectedIndex = -1;

                    YourStuffTabButton.Focus(
                        FocusState.Programmatic);

                    return true;
                }

                // Down from the final owned DLC
                // enters Other DLC if there is any.
                if (direction > 0 &&
                    OtherDlcList.Items.Count > 0) {
                    FocusDlcItem(
                        OtherDlcList,
                        0);

                    return true;
                }

                return false;
            }

            int otherIndex =
                OtherDlcList.IndexFromContainer(
                    focusedItem);

            if (otherIndex >= 0) {
                int nextIndex =
                    otherIndex + direction;

                if (nextIndex >= 0 &&
                    nextIndex < OtherDlcList.Items.Count) {
                    FocusDlcItem(
                        OtherDlcList,
                        nextIndex);

                    return true;
                }

                // Up from first Other DLC returns
                // to the last owned DLC.
                if (direction < 0 &&
                    otherIndex == 0 &&
                    OwnedDlcList.Items.Count > 0) {
                    FocusDlcItem(
                        OwnedDlcList,
                        OwnedDlcList.Items.Count - 1);

                    return true;
                }

                // No more DLC below us.
                // Let normal Game Page navigation take over.
                return false;
            }

            return false;
        }
        private void FocusDlcItem(ListView list, int index) {
            OwnedDlcList.SelectedIndex = list == OwnedDlcList ? index : -1;

            OtherDlcList.SelectedIndex = list == OtherDlcList ? index : -1;

            object item = list.Items[index];

            list.ScrollIntoView(item);

            DispatcherQueue.TryEnqueue(() => {
                if (list.ContainerFromIndex(index) is ListViewItem container)
                    container.Focus(FocusState.Keyboard);
            });
        }
        private async void DlcList_ItemClick(object sender, ItemClickEventArgs e) {
            if (e.ClickedItem is SteamDlcItem dlc) {
                await OpenDlcStorePageAsync(
                    dlc.AppId);
            }
        }
        private async Task OpenComminityHubAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot open community hub: Steam AppId is missing");
                return;
            }
            Uri communityUrl = new($"https://steamcommunity.com/app/{appId.Value}");
            Uri steamUrl = new($"steam://openurl/{communityUrl}");
            bool opened = await Launcher.LaunchUriAsync(steamUrl);
            Debug.WriteLine(
                opened
                ? $"Steam community hub opened: {appId.Value}"
                : $"Steam community hub failed: {appId.Value}");
        }
        private async void CommunityHubButton_Click(object sender, RoutedEventArgs e) {
            await OpenComminityHubAsync();
        }
        private async Task OpenDiscussionsAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot open discussions: Steam AppId is missing");
                return;
            }
            Uri discussionsUrl = new($"https://steamcommunity.com/app/{appId.Value}/discussions");
            Uri steamUrl = new($"steam://openurl/{discussionsUrl}");
            bool opened = await Launcher.LaunchUriAsync(steamUrl);
            Debug.WriteLine(
                opened
                ? $"Steam discussions opened: {appId.Value}"
                : $"Steam discussions failed: {appId.Value}");
        }
        private async void DiscussionsButton_Click(object sender, RoutedEventArgs e) {
            await OpenDiscussionsAsync();
        }
        private async Task OpenGuidesAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot open guides: Steam AppId is missing");
                return;
            }
            Uri guidesUrl = new($"https://steamcommunity.com/app/{appId.Value}/guides");
            Uri steamUrl = new($"steam://openurl/{guidesUrl}");
            bool opened = await Launcher.LaunchUriAsync(steamUrl);
            Debug.WriteLine(
                opened
                ? $"Steam guides opened: {appId.Value}"
                : $"Steam guides failed: {appId.Value}");
        }
        private async void GuidesButton_Click(object sender, RoutedEventArgs e) {
            await OpenGuidesAsync();
        }
        private async Task VerifyGameFilesAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot verify game files: Steam AppId is missing");
                return;
            }
            Uri verifyUri = new($"steam://validate/{appId.Value}");
            bool opened = await Launcher.LaunchUriAsync(verifyUri);
            Debug.WriteLine(
                opened
                ? $"Steam verify game files opened: {appId.Value}"
                : $"Steam verify game files failed: {appId.Value}");
        }
        private async void VerifyGameFilesButton_Click(object sender, RoutedEventArgs e) {
            await VerifyGameFilesAsync();
        }
        private async Task BrowseLocalFilesAsync() {
            uint? appId = Game?.SteamAppId;
            if (appId == null) {
                Debug.WriteLine("Cannot browse local files: Steam AppId is missing");
                return;
            }
            string? gamePath = await _steamLocalService.GetInstalledGamePathAsync(appId.Value);
            if (string.IsNullOrWhiteSpace(gamePath)) {
                Debug.WriteLine($"Cannot browse local files: Game path is missing for AppId {appId.Value}");
                return;
            }
            Process.Start(new ProcessStartInfo {
                FileName = gamePath,
                UseShellExecute = true
            });
            Debug.WriteLine($"Opened local files for AppId {appId.Value} at path: {gamePath}");
        }
        private async void BrowseLocalFilesButton_Click(object sender, RoutedEventArgs e) {
            await BrowseLocalFilesAsync();
        }
        private void OpenControllerTroubleshooting() {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = "joy.cpl",
                    UseShellExecute = true
                });
                Debug.WriteLine("Opened controller troubleshooting (joy.cpl)");
            } catch (Exception ex) {
                Debug.WriteLine($"Failed to open controller troubleshooting: {ex}");
            }
        }
        private void ControllerHelpButton_Click(object sender, RoutedEventArgs e) {
            OpenControllerTroubleshooting();
        }
    }
}
