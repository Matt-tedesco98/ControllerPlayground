using ControllerPlayground.Input;
using ControllerPlayground.Navigation;
using ControllerPlayground.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class SettingsView : UserControl {

        internal event Action<AppScreen>? NavigateRequested;
        public ControllerFamily ControllerFamily { get => PromptBar.Family; set => PromptBar.Family = value; }

        internal AudioService? AudioService { get; set; }

        private bool IsAudioFocused => ReferenceEquals(FocusManager.GetFocusedElement(SettingsRoot.XamlRoot), AudioButton);
        private bool IsPowerFocused => ReferenceEquals(FocusManager.GetFocusedElement(SettingsRoot.XamlRoot), PowerButton);

        private UIElement ActiveFocusRoot => PowerPanel.Visibility == Visibility.Visible ? PowerPanel : SettingsRoot;

        public SettingsView() {
            InitializeComponent();
        }

        internal void HandleControllerAction(ControllerAction action) {
            switch (action) {
                case ControllerAction.Accept:
                    if (IsPowerFocused) {
                        AudioPanel.Visibility = Visibility.Collapsed;
                        PowerPanel.Visibility = Visibility.Visible;

                        SleepButton.Focus(FocusState.Programmatic);

                    }

                    if (IsAudioFocused && AudioService is AudioService audioService) {
                        try {
                            UpdateAudioDisplay(audioService);
                        }catch (Exception ex) {
                            AudioStatusText.Text = $"Audio unavailable: {ex.Message}";
                        }
                    }
                    break;

                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up,
                        new FindNextElementOptions {
                            SearchRoot = ActiveFocusRoot
                        });
                    break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down,
                        new FindNextElementOptions {
                            SearchRoot = ActiveFocusRoot
                        });
                    break;

                case ControllerAction.NavigateLeft:
                    AdjustAudioVolume(-0.05f);
                    break;

                case ControllerAction.NavigateRight:
                    AdjustAudioVolume(0.05f);
                    break;

                case ControllerAction.X:
                    ToggleAudioMute();
                    break;
            }
        }

        internal void RestoreFocus() {
            AudioButton.Focus(FocusState.Programmatic);

            if (AudioService is AudioService audioService) {
                try {
                    UpdateAudioDisplay(audioService);
                } catch (Exception ex) {
                    AudioStatusText.Text = $"Audio unavailable: {ex.Message}";
                }
            }
        }

        private void AdjustAudioVolume(float amount) {
            var audioService = AudioService;
            if (!IsAudioFocused || audioService == null)
                return;

            try {
                audioService.AdjustVolume(amount);

                UpdateAudioDisplay(audioService);
            } catch (Exception ex) { 
                AudioStatusText.Text = $"Audio error: {ex.Message}";
            }
        }

        private void ToggleAudioMute() {
            var audioService = AudioService;
            if (!IsAudioFocused || audioService == null)
                return;

            try {
                audioService.ToggleMute();

                UpdateAudioDisplay(audioService);
            } catch (Exception ex) { 
                AudioStatusText.Text = $"Audio error: {ex.Message}";
            }
        }

        private void UpdateAudioDisplay(AudioService audioService) {
            AudioStatusText.Text = audioService.GetDefaultOutputInfo();

            AudioVolumeBar.Value = audioService.GetDefaultVolumePercent();
        }

        internal bool TryClosePowerPanel() {
            if (PowerPanel.Visibility != Visibility.Visible)
                return false;

            PowerPanel.Visibility = Visibility.Collapsed;
            AudioPanel.Visibility = Visibility.Visible;

            PowerButton.Focus(FocusState.Programmatic);
            return true;
        }

        internal void ResetPanels() {
            PowerPanel.Visibility = Visibility.Collapsed;
            AudioPanel.Visibility = Visibility.Visible;
        }
    }
}
