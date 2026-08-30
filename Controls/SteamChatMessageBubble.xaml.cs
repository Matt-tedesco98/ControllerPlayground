using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ControllerPlayground.Controls;
using ControllerPlayground.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Controls;

public sealed partial class SteamChatMessageBubble : UserControl
{
    public SteamChatMessageBubble()
    {
        InitializeComponent();
    }

    public SteamChatMessage? Item {
        get => (SteamChatMessage?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(SteamChatMessage),
            typeof(SteamChatMessageBubble),
            new PropertyMetadata(null, OnItemChange));

    private static void OnItemChange(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is SteamChatMessageBubble bubble) {
            bubble.UpdateVisuals();
        }
    }

    private void UpdateVisuals() { 
        SteamChatMessage? item = Item;

        if (item == null) { 
            MessageText.Text = string.Empty;
            return;
        }

        MessageText.Text = item.Text;

        BubbleBorder.HorizontalAlignment = item.IsFromCurrentUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}
