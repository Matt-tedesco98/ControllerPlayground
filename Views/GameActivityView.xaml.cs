using ControllerPlayground.Models;
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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class GameActivityView : UserControl {
        public GameActivityView() {
            InitializeComponent();
        }

        public GameActivityData? Data {
            get => (GameActivityData?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data),
            typeof(GameActivityData),
            typeof(GameActivityView),
            new PropertyMetadata(null)
        );
    }
}
