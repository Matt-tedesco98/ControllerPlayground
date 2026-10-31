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
using ControllerPlayground.Input;
using ControllerPlayground.Navigation;
using ControllerPlayground.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class SettingsView : UserControl {

        internal event Action<AppScreen>? NavigateRequested;
        public ControllerFamily ControllerFamily { get => PromptBar.Family; set => PromptBar.Family = value; }

        private readonly AudioService _audioService = new();

        public SettingsView() {
            InitializeComponent();
        }

        internal void HandleControllerAction(ControllerAction action) {
            switch (action) {
                case ControllerAction.Accept:
                    if (FocusManager.GetFocusedElement(SettingsRoot.XamlRoot) == AudioButton) {
                        try {
                            AudioStatusText.Text = _audioService.GetDefaultOutputInfo();
                        }catch (Exception ex) {
                            AudioStatusText.Text = $"Audio unavailable: {ex.Message}";
                        }
                    }
                    break;

                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up,
                        new FindNextElementOptions {
                            SearchRoot = SettingsRoot
                    });
                    break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down,
                        new FindNextElementOptions {
                            SearchRoot = SettingsRoot
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

                case ControllerAction.Back:
                    NavigateRequested?.Invoke(AppScreen.Home);
                    break;
            }
        }

        internal void RestoreFocus() {
            AudioButton.Focus(FocusState.Programmatic);
        }

        private void AdjustAudioVolume(float amount) {
            if (FocusManager.GetFocusedElement(SettingsRoot.XamlRoot) != AudioButton)
                return;

            try {
                _audioService.AdjustVolume(amount);

                AudioStatusText.Text = _audioService.GetDefaultOutputInfo();
            } catch (Exception ex) { 
                AudioStatusText.Text = $"Audio error: {ex.Message}";
            }
        }

        private void ToggleAudioMute() {
            if (FocusManager.GetFocusedElement(SettingsRoot.XamlRoot) != AudioButton)
                return;

            try {
                _audioService.ToggleMute();

                AudioStatusText.Text = _audioService.GetDefaultOutputInfo();
            } catch (Exception ex) { 
                AudioStatusText.Text = $"Audio error: {ex.Message}";
            }
        }
    }
}
