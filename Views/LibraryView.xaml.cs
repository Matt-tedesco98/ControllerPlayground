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
using System.Collections.ObjectModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Views {
    public sealed partial class LibraryView : UserControl {

        public ObservableCollection<SteamLibraryGame> Games { get; } = new();
        public LibraryView() {
            InitializeComponent();
        }

        internal void SetSteamLibraryGames(IReadOnlyList<SteamLibraryGame> games) {
            Games.Clear();
            foreach (SteamLibraryGame game in games) {
                Games.Add(game);
            }
            GameCountText.Text = $"{Games.Count} games";
        }
    }
}
