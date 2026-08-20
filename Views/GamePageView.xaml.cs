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


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GamePageView : UserControl {

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

            GameInfoContent.Visibility = SelectedTab == GamePageTab.GameInfo ? Visibility.Visible : Visibility.Collapsed;
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
                HeroImage = null;
                CoverImage = null;
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

        private void GamePageView_Loaded(object sender, RoutedEventArgs e) {
            if (SelectedTab == GamePageTab.Activity) {
                PlayButton.Focus(FocusState.Programmatic);
            } else {
                GetSelectedTabButton().Focus(FocusState.Programmatic);
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

        public GamePageView() {
            InitializeComponent();

            Loaded += GamePageView_Loaded;
        }
    }
}
