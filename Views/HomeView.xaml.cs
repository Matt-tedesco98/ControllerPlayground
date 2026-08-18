using ControllerPlayground.Controls;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ControllerPlayground.Navigation;
using ControllerPlayground.Overlays;
using ControllerPlayground.Models;
using System.Collections.Generic;

namespace ControllerPlayground.Views {
    public sealed partial class HomeView : UserControl {

        internal event Action<AppScreen>? NavigateRequested;

        private readonly Dictionary<GameTile, GameItem> _gamesByTile;

        internal event Action<GameItem, GamePageTab>? GamePageRequested;
        private void GameTile_Activated(object sender, EventArgs e) {
            if (sender is GameTile gameTile) {
                System.Diagnostics.Debug.WriteLine($"GameTile activated: {gameTile.Title}");
            }
        }

        public ControllerFamily ControllerFamily {
            get => PromptBar.Family;
            set => PromptBar.Family = value;
        }

        internal void HandleControllerAction(ControllerAction action) {
            var focusOptions = new FindNextElementOptions {
                SearchRoot = HomeRoot
            };

            if (_isContextMenuOpen) {
                // If the context menu is open, we want to handle navigation within the context menu
                if(action == ControllerAction.Back || action == ControllerAction.Menu) {
                    CloseGameContextMenu();
                    return;
                }
                GameMenu.HandleControllerAction(action);
                return;
            }

            switch (action) {
                case ControllerAction.NavigateUp:
                    // Move focus up
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Up,
                        focusOptions);
                    break;
                case ControllerAction.NavigateDown:
                    // Move focus down
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Down,
                        focusOptions);
                    break;
                case ControllerAction.NavigateLeft:
                    // Move focus to the left
                    bool moved = FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Left,
                        focusOptions);
                    Debug.WriteLine($"NavigateLeft received - focus moved: {moved}");
                    break;
                case ControllerAction.NavigateRight:
                    // Move focus to the right
                    bool _moved = FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Right,
                        focusOptions);
                    Debug.WriteLine($"NavigateRight received - focus moved: {_moved}");
                    break;


                case ControllerAction.Accept:
                    // Handle accept
                    var focused = FocusManager.GetFocusedElement(HomeRoot.XamlRoot);
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

                case ControllerAction.Back:
                    // Handle back
                    Debug.WriteLine("Back requested");
                    break;

                case ControllerAction.View:
                    // Handle view
                    Debug.WriteLine("View requested");
                    break;

                case ControllerAction.Menu:
                    // Handle menu
                    OpenContextMenu();
                    Debug.WriteLine("Menu requested");
                    break;
            }
        }

        internal void RestoreFocus() {
            // Restore focus to the last focused GameTile
            DispatcherQueue.TryEnqueue(() => {
                (_lastFocusedTitle ?? HaloTile).FocusTile();
            });
        }



        private GameTile? _lastFocusedTitle;

        private void GameTile_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is GameTile gameTile) {
                _lastFocusedTitle = gameTile;
            }
        }

        private bool _isContextMenuOpen;

        private void OpenContextMenu() {
            if (_lastFocusedTitle == null)
                return;
            _isContextMenuOpen = true;
            GameMenu.GameTitle = _lastFocusedTitle.Title;
            ContextMenuLayer.Visibility = Visibility.Visible;

            DispatcherQueue.TryEnqueue(() => {
                GameMenu.FocusFirstItem();
            });
        }

        private void CloseGameContextMenu() {
            _isContextMenuOpen = false;
            ContextMenuLayer.Visibility = Visibility.Collapsed;
            RestoreFocus();
        }

        private void GameMenu_ActionRequested(GameContextAction action) {
            switch (action) {
                case GameContextAction.Play:
                    Debug.WriteLine($"Play requested: {_lastFocusedTitle?.Title}");
                    break;
                case GameContextAction.GameDetails:
                    if(_lastFocusedTitle!= null &&
                        _gamesByTile.TryGetValue(_lastFocusedTitle, out GameItem? game)) {
                        CloseGameContextMenu();
                        GamePageRequested?.Invoke(game, GamePageTab.GameInfo);
                    }
                    Debug.WriteLine($"GameDetails requested: {_lastFocusedTitle?.Title}");
                    break;
                case GameContextAction.Manage:
                    Debug.WriteLine($"Manage requested: {_lastFocusedTitle?.Title}");
                    break;
                case GameContextAction.Properties:
                    Debug.WriteLine($"Properties requested: {_lastFocusedTitle?.Title}");
                    break;
            }
        }
        public HomeView() {
            InitializeComponent();

            _gamesByTile = new Dictionary<GameTile, GameItem> {
                [HaloTile] = new GameItem {
                    Title = "Halo Infinite",
                    Genres = new()
        {
            "Action",
            "Shooter"
        }
                },

                [ForzaTile] = new GameItem {
                    Title = "Forza Horizon 5",
                    HeroImagePath = "ms-appx:///Assets/Games/Forza/Hero.jpg",
                    CoverImagePath = "ms-appx:///Assets/Games/Forza/Cover.jpg",
                    Genres = new()
        {
            "Racing",
            "Open World"
        }
                },

                [MinecraftTile] = new GameItem {
                    Title = "Minecraft",
                    Genres = new()
        {
            "Sandbox",
            "Adventure"
        }
                },

                [SteamTile] = new GameItem {
                    Title = "Steam"
                }
            };

            GameMenu.ActionRequested += GameMenu_ActionRequested;

            Loaded += (_, _) => {
                DispatcherQueue.TryEnqueue(() => {
                    // Set initial focus to the first GameTile
                    HaloTile.FocusTile();
                });
            };
        }
    }
}
