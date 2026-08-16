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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class GameContextMenu : UserControl {

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
            }
        }

        public GameContextMenu() {
            InitializeComponent();
        }
    }
}
