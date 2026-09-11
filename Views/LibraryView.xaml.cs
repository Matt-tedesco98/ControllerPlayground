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
using System.Collections.ObjectModel;
using ControllerPlayground.Input;
using ControllerPlayground.Controls;
using ControllerPlayground.Services.Steam;
using ControllerPlayground.Overlays;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class LibraryView : UserControl {

        private bool _isContextMenuOpen;
        internal bool IsContextMenuOpen => _isContextMenuOpen;
        private GameTile? _contextMenuTile;
        private SteamLibraryGame? _contextMenuGame;
        private readonly SteamLaunchService _steamLaunchService = new();
        private readonly List<SteamLibraryGame> _allGames = new();
        private readonly HashSet<uint> _installedAppIds = new();
        public ObservableCollection<SteamLibraryGame> Games { get; } = new();
        internal event Action<GameItem, GamePageTab>? GamePageRequested;

        public LibraryView() {
            InitializeComponent();

            Loaded += LibraryView_Loaded;
            GameMenu.ActionRequested += GameMenu_ActionRequested;
        }

        private void LibraryView_Loaded(object sender, RoutedEventArgs e) {
            if (Games.Count > 0) {
                RestoreFocus();
            }
        }

        internal void SetSteamLibraryGames(IReadOnlyList<SteamLibraryGame> games, IReadOnlyCollection<uint> installedAppIds) {
            _allGames.Clear();
            _allGames.AddRange(games);

            _installedAppIds.Clear();

            foreach (uint appId in installedAppIds) {
                _installedAppIds.Add(appId);
            }

            Games.Clear();

            foreach (SteamLibraryGame game in games) {
                Games.Add(game);
            }
            GameCountText.Text = $"{Games.Count} games";

            Debug.WriteLine($"Library recived {_installedAppIds.Count} installed appIDs");

            RestoreFocus();
        }

        internal void HandleControllerAction(ControllerAction action) {
            System.Diagnostics.Debug.WriteLine($"Library controller action: {action}");
            var focusOption  = new FindNextElementOptions {
                SearchRoot = LibraryRoot
            };

            if (_isContextMenuOpen) { 
                if (action== ControllerAction.Back || action == ControllerAction.Menu) {
                    CloseContextMenu();
                    return;
                }
                GameMenu.HandleControllerAction(action);
                return;
            }

            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up, focusOption); break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down, focusOption);
                    break;

                case ControllerAction.NavigateLeft:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Left, focusOption);
                    break;

                case ControllerAction.NavigateRight:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Right, focusOption);
                    break;

                case ControllerAction.Accept: {
                        object? focused = FocusManager.GetFocusedElement(LibraryRoot.XamlRoot);

                        if (focused == AllGamesFilterButton) {
                            ShowAllGames();
                            break;
                        }

                        if (focused == InstalledFilterButton) {
                            ShowInstalledGames();
                            break;
                        }

                        if (focused is DependencyObject element) {
                            DependencyObject? current = element;

                            while (current != null) {
                                if (current is GameTile tile) {
                                    tile.Activate();
                                    break;
                                }
                                current = VisualTreeHelper.GetParent(current);
                            }
                        }
                    }
                    break;

                case ControllerAction.Menu:
                    OpenContextMenu();
                    break;
            }
        }

        internal void RestoreFocus() {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { 
                if (Games.Count == 0)
                    return;

                LibraryRepeater.UpdateLayout();

                if (LibraryRepeater.GetOrCreateElement(0) is not GameTile firstTile)
                   return;

                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
                    bool focused = firstTile.FocusTile();

                    Debug.WriteLine($"Library restore focus: {focused}");
                });
            });
        }

        private void GameTile_Activated(object? sender, EventArgs e) {
            if (sender is not GameTile tile)
                return;

            int index = LibraryRepeater.GetElementIndex(tile);

            if(index < 0 || index >= Games.Count)
                return;

            SteamLibraryGame steamGame = Games[index];

            GameItem game = new() {
                Title = steamGame.Name,
                SteamAppId = steamGame.AppId,
                CoverImagePath = steamGame.LibraryCapsuleUrl
            };

            GamePageRequested?.Invoke(game, GamePageTab.Activity);
        }
        private void OpenContextMenu() {
            object? focused = FocusManager.GetFocusedElement(LibraryRoot.XamlRoot);

            if (focused is not DependencyObject element)
                return;
            DependencyObject? current = element;

            while (current != null) {
                if (current is GameTile tile) {
                    int index = LibraryRepeater.GetElementIndex(tile);
                    if (index < 0 || index >= Games.Count)
                        return;

                    _contextMenuTile = tile;
                    _contextMenuGame = Games[index];

                    GameMenu.GameTitle = _contextMenuGame.Name;

                    _isContextMenuOpen = true;
                    ContextMenuLayer.Visibility = Visibility.Visible;

                    DispatcherQueue.TryEnqueue(() => {
                        GameMenu.FocusFirstItem();
                    });
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        private void CloseContextMenu() { 
            _isContextMenuOpen = false;
            ContextMenuLayer .Visibility = Visibility.Collapsed;

            DispatcherQueue.TryEnqueue(() => {
                _contextMenuTile?.FocusTile();
            });
        }

        private void GameMenu_ActionRequested(GameContextAction action) {
            switch (action) {
                case GameContextAction.Play:
                    if (_contextMenuGame != null) {
                        uint appId = _contextMenuGame.AppId;
                        CloseContextMenu();
                        _ = _steamLaunchService.LaunchGameAsync(appId);
                    }
                    break;
                case GameContextAction.GameDetails:
                    if (_contextMenuGame != null) {
                        SteamLibraryGame steamGame = _contextMenuGame;
                        CloseContextMenu();

                        GameItem game = new() {
                            Title = steamGame.Name,
                            SteamAppId = steamGame.AppId,
                            CoverImagePath = steamGame.LibraryCapsuleUrl
                        };

                        GamePageRequested?.Invoke(game, GamePageTab.GameInfo);
                    }
                    break;
            }
                
        }

        private void AllGamesFilterButton_Click(object sender, RoutedEventArgs e) {
            ShowAllGames();
        }

        private void InstalledFilterButton_Click(object sender, RoutedEventArgs e) {
            ShowInstalledGames();
        }
        
        private void ShowAllGames() {
            Games.Clear();
            foreach (SteamLibraryGame game in _allGames) {
                Games.Add(game);
            }
            GameCountText.Text = $"{Games.Count} games";
            RestoreFocus();
        }

        private void ShowInstalledGames() {
            Games.Clear();
            foreach (SteamLibraryGame game in _allGames) {
                if (_installedAppIds.Contains(game.AppId)) {
                    Games.Add(game);
                }
            }
            GameCountText.Text = $"{Games.Count} games";
            RestoreFocus();
        }
    }
}
