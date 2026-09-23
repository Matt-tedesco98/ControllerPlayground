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
using ControllerPlayground.Services.Steam;

namespace ControllerPlayground.Views {
    public sealed partial class HomeView : UserControl {
        public HomeView() {
            InitializeComponent();

            GameMenu.ActionRequested += GameMenu_ActionRequested;
            Loaded += HomeView_Loaded;

        }

        internal event Action<AppScreen>? NavigateRequested;

        private readonly Dictionary<GameTile, GameItem> _gamesByTile = new();
        internal SteamLaunchService LaunchService {get; set;} = new();

        internal event Action<GameItem, GamePageTab>? GamePageRequested;

        private void HomeView_Loaded(object sender,RoutedEventArgs e) {
            RestoreFocus();
        }
        private void GameTile_Activated(object sender, EventArgs e) {
            if (sender is GameTile gameTile && _gamesByTile.TryGetValue(gameTile, out var game)) {
                GamePageRequested?.Invoke(game, GamePageTab.Activity);
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
                if (action == ControllerAction.Back || action == ControllerAction.Menu) {
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
                if (_lastFocusedTitle != null) {
                    _lastFocusedTitle.FocusTile();
                    return;
                }
                if (GameTitlePanel.Children.Count > 0 && GameTitlePanel.Children[0] is GameTile firstTile) {
                    firstTile.FocusTile();
                }
            });
        }



        private GameTile? _lastFocusedTitle;

        private void GameTile_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is GameTile gameTile) {
                _lastFocusedTitle = gameTile;
            }
        }

        private bool _isContextMenuOpen;
        internal bool IsContextMenuOpen => _isContextMenuOpen;

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
                case GameContextAction.Play: {
                        if (_lastFocusedTitle != null && _gamesByTile.TryGetValue(_lastFocusedTitle, out GameItem? playGame) && playGame.SteamAppId.HasValue) {
                            _ = LaunchService.LaunchGameAsync(playGame.SteamAppId.Value);
                            break;
                        }
                    }
                    break;
                case GameContextAction.GameDetails:
                    if (_lastFocusedTitle != null &&
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

        internal void SetSteamLibraryGames(IReadOnlyList<SteamLibraryGame> steamGames) {
            GameTitlePanel.Children.Clear();
            _gamesByTile.Clear();
            _lastFocusedTitle = null;

            foreach (SteamLibraryGame steamGame in steamGames) {
                GameItem game = new() {
                    Title = steamGame.Name,
                    SteamAppId = steamGame.AppId,
                    CoverImagePath = steamGame.LibraryCapsuleUrl,
                    HeroImagePath = steamGame.HeroImageUrl,
                    LogoImagePath = steamGame.LibraryLogoUrl,
                    Developer = steamGame.Developer,
                    Publisher = steamGame.Publisher,
                    ReleaseDate = steamGame.ReleaseDate
                };

                GameTile tile = new() {
                    Title = game.Title,
                    CoverImageUrl = game.CoverImagePath
                };

                tile.Activated += GameTile_Activated;
                tile.GotFocus += GameTile_GotFocus;

                _gamesByTile[tile] = game;

                GameTitlePanel.Children.Add(tile);
            }

            DispatcherQueue.TryEnqueue(() => {
                if (GameTitlePanel.Children.Count > 0 && GameTitlePanel.Children[0] is GameTile firstTile) {
                    _lastFocusedTitle = firstTile;
                    firstTile.FocusTile();
                }
            });
            Debug.WriteLine($"HomeView loaded {steamGames.Count} Steam games.");
        }
    }
}
