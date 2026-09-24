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
using ControllerPlayground.Navigation;
using ControllerPlayground.Services.Steam;


namespace ControllerPlayground.Overlays {
    public sealed partial class GuideMenu : UserControl {

        internal event Action<AppScreen>? NavigateRequested;
        private Button? _lastFocusedButton;
        internal event Action? ResumeGameRequested;

        public void FocusFirstItem() {
            (_lastFocusedButton ?? LibraryButton).Focus(FocusState.Keyboard);
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

                case ControllerAction.Accept: {
                        var focused = FocusManager.GetFocusedElement(GuideRoot.XamlRoot) as Button;
                        if (focused == ResumeGameButton) {
                            ResumeGameRequested?.Invoke();
                            break;
                        }
                        if (focused == SettingsButton) {
                            NavigateRequested?.Invoke(AppScreen.Settings);
                        }else if (focused == LibraryButton) {
                            NavigateRequested?.Invoke(AppScreen.Library);
                        }
                    }
                    break;
            }
        }
        internal void UpdateGameSessionState(GameSessionState state, uint? appId, string? gameTitle) {

            bool hasActiveGame = state != GameSessionState.Idle && appId.HasValue;

            CurrentGamePanel.Visibility = hasActiveGame ? Visibility.Visible : Visibility.Collapsed;

            if(!hasActiveGame) {
                CurrentGameTitleText.Text = string.Empty;
                CurrentGameStatusText.Text = string.Empty;
                return;
            }

            CurrentGameTitleText.Text = !string.IsNullOrWhiteSpace(gameTitle) ? gameTitle : $"Steam App {appId}";

            ResumeGameButton.IsEnabled = state == GameSessionState.Running;
        }


        public GuideMenu() {
            InitializeComponent();
        }
    }
}
