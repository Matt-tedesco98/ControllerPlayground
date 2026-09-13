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
        private bool _isSupportPageOpen;
        internal bool IsSupportPageOpen => _isSupportPageOpen;

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
                view.UpdateGameVisuals();
            }
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

            if(!string.IsNullOrWhiteSpace(game.LogoImagePath)) {
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
                        FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions {
                            SearchRoot = PageRoot
                        });
                    }
                    break;
                case ControllerAction.NavigateUp: {
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

            var items = await _steamService.GetGameActivityAsync(appId.Value);

            Game.ActivityData.ActivityFeed.Clear();

            foreach (ActivityFeedItem item in items) {
                Game.ActivityData.ActivityFeed.Add(item);
            }

            Debug.WriteLine($"Steam returned {items.Count} news items.");
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
    }
}
