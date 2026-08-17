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
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class GameContextMenu : UserControl {

        internal event Action<GameContextAction>? ActionRequested;

        public static readonly DependencyProperty GameTitleProperty = DependencyProperty.Register(
            nameof(GameTitle),
            typeof(string),
            typeof(GameContextMenu),
            new PropertyMetadata("Game"));

        public string GameTitle {
            get => (string)GetValue(GameTitleProperty);
            set => SetValue(GameTitleProperty, value);
        }

        public void FocusFirstItem() {
            PlayButton.Focus(FocusState.Programmatic);
        }

        internal void HandleControllerAction(ControllerAction action) {
            var focusoptions = new FindNextElementOptions {
                SearchRoot = MenuRoot
            };
            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up, focusoptions);
                    break;
                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down, focusoptions);
                    break;

                case ControllerAction.Accept: 
                    {
                        var focused = FocusManager.GetFocusedElement(MenuRoot.XamlRoot) as Button;

                        if (focused == PlayButton)
                            ActionRequested?.Invoke(GameContextAction.Play);
                        else if (focused == DetailsButton)
                            ActionRequested?.Invoke(GameContextAction.GameDetails);
                        else if (focused == ManageButton)
                            ActionRequested?.Invoke(GameContextAction.Manage);
                        else if (focused == PropertiesButton)
                            ActionRequested?.Invoke(GameContextAction.Properties);
                    }
                    break;
            }
        }

        private void ContextButton_GotFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255,255,255));
            }
        }

        private void ContextButton_LostFocus(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255,255,255));
            }
        }

        public GameContextMenu() {
            InitializeComponent();
        }
    }
}
