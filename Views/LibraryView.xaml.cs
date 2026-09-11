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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class LibraryView : UserControl {

        private bool _isContextMenuOpen;
        internal bool IsContextMenuOpen => _isContextMenuOpen;
        private GameTile? _contextMenuTile;
        private SteamLibraryGame? _contextMenuGame;
        public ObservableCollection<SteamLibraryGame> Games { get; } = new();
        internal event Action<GameItem, GamePageTab>? GamePageRequested;

        public LibraryView() {
            InitializeComponent();

            Loaded += LibraryView_Loaded;
        }

        private void LibraryView_Loaded(object sender, RoutedEventArgs e) {
            if (Games.Count > 0) {
                RestoreFocus();
            }
        }

        internal void SetSteamLibraryGames(IReadOnlyList<SteamLibraryGame> games) {
            Games.Clear();
            foreach (SteamLibraryGame game in games) {
                Games.Add(game);
            }
            GameCountText.Text = $"{Games.Count} games";

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
            DispatcherQueue.TryEnqueue(() => { 
                if (Games.Count == 0)
                    return;
                if (LibraryRepeater.GetOrCreateElement(0) is GameTile firstTile)
                    firstTile.FocusTile();
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

    }
}
