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
using Windows.Devices.Input.Preview;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class OnScreenKeyboard : UserControl {
        internal event Action<string>? textCommitted;
        internal event Action? Cancelled;

        private string _text = string.Empty;

        private readonly string[] _rows = {
            "QWERTYUIOP",
            "ASDFGHJKL",
            "ZXCVBNM"
        };
        public OnScreenKeyboard() {
            InitializeComponent();

            BuildKeyboard();
        }
        public void SetText(string text) {
            _text = text ?? string.Empty;
            UpdatePreview();   
        }

        public void FocusFirstKey() {
            DispatcherQueue.TryEnqueue(() => {
                if (KeyboardRowsPanel.Children.Count == 0)
                    return;

                if (KeyboardRowsPanel.Children[0] is StackPanel firstRow && firstRow.Children.Count > 0 && firstRow.Children[0] is Button firstButton) {
                    firstButton.Focus(FocusState.Programmatic);
                }
            });
        }

        internal void HandleControllerAction(ControllerAction action) {
            FindNextElementOptions options = new() {
                SearchRoot = KeyboardRoot
            };

            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Up, options);
                    break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down, options);
                    break;

                case ControllerAction.NavigateLeft:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Left, options);
                    break;

                case ControllerAction.NavigateRight:
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Right, options);
                    break;

                case ControllerAction.Accept:
                    if (FocusManager.GetFocusedElement(KeyboardRoot.XamlRoot) is Button button) {

                    ActivateKey(button);
                    }
                    break;

                case ControllerAction.Back:
                    Cancelled?.Invoke();
                    break;
            }
        }

        private void BuildKeyboard() {
            foreach (string row in _rows) {
                StackPanel rowPanel = new() {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 8
                };

                foreach (char character in row) {
                    Button button = new() {
                        Content = character.ToString(),
                        Tag = character.ToString(),
                        Width = 72,
                        Height = 60,
                        FontSize = 22
                    };

                    button.Click += KeyButton_Click;

                    rowPanel.Children.Add(button);
                }

                KeyboardRowsPanel.Children.Add(rowPanel);
            }

            StackPanel actionRow = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };

            actionRow.Children.Add(CreateActionButton("Space", "SPACE", 260));

            actionRow.Children.Add(CreateActionButton("Backspace", "BACKSPACE", 180));

            actionRow.Children.Add(CreateActionButton("Enter", "ENTER", 140));

            KeyboardRowsPanel.Children.Add(actionRow);
        }

        private Button CreateActionButton(string label, string action, double width) {
            Button button = new() {
                Content = label,
                Tag = action,
                Width = width,
                Height = 60,
                FontSize = 20
            };

            button.Click += KeyButton_Click;

            return button;
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button) {
                ActivateKey(button);
            }
        }

        private void ActivateKey(Button button) {
            string? value = button.Tag as string;

            if (string.IsNullOrEmpty(value))
                return;

            switch (value) {
                case "SPACE":
                    _text += " ";
                    break;

                case "BACKSPACE":
                    if (_text.Length > 0) {
                        _text = _text[..^1];
                    }
                    break;

                case "ENTER":
                    textCommitted?.Invoke(_text);
                    return;

                default:
                    _text += value;
                    break;
            }

            UpdatePreview();
        }

        private void UpdatePreview() {
            PreviewText.Text = _text;
        }
    }
}
