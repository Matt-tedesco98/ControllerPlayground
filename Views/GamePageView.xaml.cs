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
using ControllerPlayground.Navigation;
using ControllerPlayground.Input;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GamePageView : UserControl {

        public static DependencyProperty SelectedTabProperty = DependencyProperty.Register(
            nameof(SelectedTab),
            typeof(GamePageTab),
            typeof(GamePageView),
            new PropertyMetadata(GamePageTab.Activity, OnSelectedTabChange));

        private static void OnSelectedTabChange(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is GamePageView view) {
                view.UpdateSelectedTab();
            }
        }

        private void UpdateSelectedTab() { 
            ActivityTabButton.Opacity = SelectedTab == GamePageTab.Activity ? 1.0 : 0.5;
            YourStuffTabButton.Opacity = SelectedTab == GamePageTab.YourStuff ? 1.0 : 0.5;
            CommunityTabButton.Opacity = SelectedTab == GamePageTab.Community ? 1.0 : 0.5;
            GameInfoTabButton.Opacity = SelectedTab == GamePageTab.GameInfo ? 1.0 : 0.5;
        }

        public GamePageTab SelectedTab {
            get => (GamePageTab)GetValue(SelectedTabProperty);
            set => SetValue(SelectedTabProperty, value);
        }

        public static readonly DependencyProperty GameProperty = DependencyProperty.Register(
            nameof(Game),
            typeof(GameItem),
            typeof(GamePageView),
            new PropertyMetadata(null));

        public GameItem? Game {
            get => (GameItem?)GetValue(GameProperty);
            set => SetValue(GameProperty, value);
        }

        internal event Action<AppScreen>? NavigateRequested;

        public ControllerFamily controllerFamily {
            get => PromptBar.Family;
            set => PromptBar.Family = value;
        }

        internal void HandleControllerAction(ControllerAction action) {
            switch (action) {
                case ControllerAction.PreviousTab:
                    SelectedTab = SelectedTab switch {
                        GamePageTab.YourStuff => GamePageTab.Activity,
                        GamePageTab.Community => GamePageTab.YourStuff,
                        GamePageTab.GameInfo => GamePageTab.Community,
                        _ => SelectedTab
                    };
                    break;
                case ControllerAction.NextTab:
                    SelectedTab = SelectedTab switch {
                        GamePageTab.Activity => GamePageTab.YourStuff,
                        GamePageTab.YourStuff => GamePageTab.Community,
                        GamePageTab.Community => GamePageTab.GameInfo,
                        _ => SelectedTab
                    };
                    break;
                case ControllerAction.Back:
                    NavigateRequested?.Invoke(AppScreen.Home);
                    break;
            }
        }
        public GamePageView() {
            InitializeComponent();
        }
    }
}
