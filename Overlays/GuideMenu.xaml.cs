using ControllerPlayground.Input;
using Microsoft.UI;
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
using Windows.UI;


namespace ControllerPlayground.Overlays {
    public sealed partial class GuideMenu : UserControl {

        public void FocusFirstItem() {
            (_lastFocusedButton ?? LibraryButton).Focus(FocusState.Programmatic);
        }

        private void GuideButton_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                _lastFocusedButton = button;
                button.Background =
                    new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            }
        }

        private void GuideButton_LostFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Background =
                    new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            }
        }

        internal void HandleControllerAction(ControllerAction action) {
            var focusOptions = new FindNextElementOptions {
                SearchRoot = GuideRoot
            };

            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Up,
                        focusOptions);
                    break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Down,
                        focusOptions);
                    break;
            }
        }

        private Button? _lastFocusedButton;

        public GuideMenu() {
            InitializeComponent();
        }
    }
}
