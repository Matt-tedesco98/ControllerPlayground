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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GameDetailsView : UserControl {

        public static readonly DependencyProperty GameProperty = DependencyProperty.Register(
            nameof(Game),
            typeof(GameItem),
            typeof(GameDetailsView),
            new PropertyMetadata(null));

        public GameItem? Game {
            get => (GameItem?)GetValue(GameProperty);
            set => SetValue(GameProperty, value);
        }
        public GameDetailsView() {
            InitializeComponent();
        }
    }
}
