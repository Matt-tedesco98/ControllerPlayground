using ControllerPlayground.Controls;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ControllerPlayground.Views {
    public sealed partial class HomeView : UserControl {

        private void GameTile_Activated(object sender, EventArgs e) {
            if (sender is GameTile gameTile) {
                System.Diagnostics.Debug.WriteLine($"GameTile activated: {gameTile.Title}");
            }
        }

        public ControllerFamily ControllerFamily {
            get =>PromptBar.Family;
            set =>PromptBar.Family = value;
        }

        internal void HandleControllerAction(ControllerAction action) {
            var focusOptions = new FindNextElementOptions {
                SearchRoot = HomeRoot
            };

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
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Left,
                        focusOptions);
                    break;
                case ControllerAction.NavigateRight:
                    // Move focus to the right
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Right,
                        focusOptions);
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
                    Debug.WriteLine("Menu requested");
                    break;
            }
        }
        public HomeView() {
            InitializeComponent();

            Loaded += (_, _) => {
                FirstGameTile.FocusTile();
            };
        }
    }
}
