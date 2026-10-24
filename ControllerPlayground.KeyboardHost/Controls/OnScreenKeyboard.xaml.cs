using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml.Input;
using System.Collections.Generic;
using ControllerPlayground.KeyboardHost.Models;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ControllerPlayground.KeyboardHost.Controls {
    public sealed partial class OnScreenKeyboard : UserControl {
        public event Action<string>? TextCommitted;
        public event Action? Cancelled;

        public event Action? MoveRequested;
        private string _text = string.Empty;

        private bool _shiftActive;
        private bool _capsLock;

        private readonly List<Button> _letterButtons = new();

        private Button? _shiftButton;
        private Button? _rightShiftButton;
        private Button? _capsButton;
        private Button? _moveButton;

        private bool _symbolMode;
        private Button? _symbolButton;

        private readonly string[] _rows = {
            "QWERTYUIOP",
            "ASDFGHJKL",
            "ZXCVBNM"
        };

        private readonly KeyboardKeyDefinition[] _numberRow = {
            new("`", "~"),
            new("1", "!"),
            new("2", "@"),
            new("3", "#"),
            new("4", "$"),
            new("5", "%"),
            new("6", "^"),
            new("7", "&"),
            new("8", "*"),
            new("9", "("),
            new("0", ")"),
            new("-", "_"),
            new("=", "+")
        };

        private readonly string[] _symbolRows = {
            "1234567890",
            "@#$%&*()-",
            ".,_!?/:"
        };

        public OnScreenKeyboard() {
            InitializeComponent();
            BuildKeyboard();
        }

        public void SetText(string text) {
            _text = text ?? string.Empty;
            UpdatePreview();
        }

        private void BuildKeyboard() {
            StackPanel numberRowPanel = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };

            foreach (KeyboardKeyDefinition definition in _numberRow) {
                Button button = CreateDefinitionButton(definition);

                numberRowPanel.Children.Add(button);
            }

            KeyboardRowsPanel.Children.Add(numberRowPanel);

            // QWERTY row
            StackPanel qwertyRowPanel = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };

            qwertyRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "Tab",
                        Action: "TAB",
                        Width: 120)));

            foreach (char character in _rows[0]) {
                Button button = new() {
                    Content = char.ToLowerInvariant(character).ToString(),
                    Tag = character.ToString(),
                    Width = 92,
                    Height = 68,
                    FontSize = 24
                };

                button.Click += KeyButton_Click;

                _letterButtons.Add(button);

                qwertyRowPanel.Children.Add(button);
            }

            qwertyRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "[",
                        "{")));

            qwertyRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "]",
                        "}")));

            qwertyRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "\\",
                        "|")));

            KeyboardRowsPanel.Children.Add(qwertyRowPanel);

            // Home row
            StackPanel homeRowPanel = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };

            _capsButton =
                CreateActionButton(
                    "Caps",
                    "CAPS",
                    130);

            homeRowPanel.Children.Add(_capsButton);

            foreach (char character in _rows[1]) {
                Button button = new() {
                    Content = char.ToLowerInvariant(character).ToString(),
                    Tag = character.ToString(),
                    Width = 92,
                    Height = 68,
                    FontSize = 24
                };

                button.Click += KeyButton_Click;

                _letterButtons.Add(button);

                homeRowPanel.Children.Add(button);
            }

            homeRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        ";",
                        ":")));

            homeRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "'",
                        "\"")));

            homeRowPanel.Children.Add(
                CreateActionButton(
                    "Enter",
                    "ENTER",
                    150));

            KeyboardRowsPanel.Children.Add(homeRowPanel);

            // Bottom letter row
            StackPanel bottomLetterRowPanel = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };

            _shiftButton =
                CreateActionButton(
                    "Shift",
                    "SHIFT",
                    130);

            bottomLetterRowPanel.Children.Add(
                _shiftButton);

            foreach (char character in _rows[2]) {
                Button button = new() {
                    Content = char.ToLowerInvariant(character).ToString(),
                    Tag = character.ToString(),
                    Width = 92,
                    Height = 68,
                    FontSize = 24
                };

                button.Click += KeyButton_Click;

                _letterButtons.Add(button);

                bottomLetterRowPanel.Children.Add(button);
            }

            bottomLetterRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        ",",
                        "<")));

            bottomLetterRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        ".",
                        ">")));

            bottomLetterRowPanel.Children.Add(
                CreateDefinitionButton(
                    new KeyboardKeyDefinition(
                        "/",
                        "?")));

            _rightShiftButton =
                CreateActionButton(
                    "Shift",
                    "SHIFT",
                    130);

            bottomLetterRowPanel.Children.Add(
                _rightShiftButton);

            KeyboardRowsPanel.Children.Add(
                bottomLetterRowPanel);

            StackPanel actionRow = new() {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                Spacing = 6
            };

            _symbolButton = CreateActionButton(
                "123",
                "SYMBOLS",
                120);

            actionRow.Children.Add(_symbolButton);

            actionRow.Children.Add(
                CreateActionButton(
                    "Space",
                    "SPACE",
                    300));

            actionRow.Children.Add(
                CreateActionButton(
                    "⌫",
                    "BACKSPACE",
                    160));

            actionRow.Children.Add(
                _moveButton =
                CreateActionButton(
                    "Move keyboard",
                    "MOVE",
                    220));

            KeyboardRowsPanel.Children.Add(
                actionRow);
        }

        private Button CreateActionButton(
            string label,
            string action,
            double width) {
            Button button = new() {
                Content = label,
                Tag = action,
                Width = width,
                Height = 68,
                FontSize = 22
            };

            button.Click += KeyButton_Click;

            return button;
        }

        private void KeyButton_Click(
            object sender,
            RoutedEventArgs e) {
            if (sender is Button button) {
                ActivateKey(button);
            }
        }

        private void ActivateKey(Button button) {
            if (button.Tag is KeyboardKeyDefinition definition) {
                if (definition.Action == "TAB") {
                    _text += "\t";
                    UpdatePreview();
                    return;
                }

                string outputValue =
                    _shiftActive &&
                    !string.IsNullOrEmpty(definition.Secondary)
                        ? definition.Secondary
                        : definition.Primary;

                _text += outputValue;

                if (_shiftActive) {
                    _shiftActive = false;
                    UpdateLetterKeys();
                }

                UpdatePreview();
                return;
            }

            string? value =
                button.Tag as string;

            if (string.IsNullOrEmpty(value))
                return;

            switch (value) {
                case "SPACE":
                    _text += " ";
                    break;

                case "BACKSPACE":
                    if (_text.Length > 0) {
                        _text =
                            _text[..^1];
                    }
                    break;

                case "ENTER":
                    TextCommitted?.Invoke(_text);
                    return;

                case "MOVE":
                    MoveRequested?.Invoke();
                    return;

                case "SHIFT":
                    _shiftActive = !_shiftActive;
                    UpdateLetterKeys();
                    break;

                case "CAPS":
                    _capsLock = !_capsLock;
                    UpdateLetterKeys();
                    break;

                case "SYMBOLS":
                    _symbolMode = !_symbolMode;
                    _shiftActive = false;
                    UpdateKeyboardLayout();
                    break;

                default:
                    if (value.Length == 1 &&
                        char.IsLetter(value[0])) {
                        bool uppercase =
                            _capsLock ^ _shiftActive;

                        char character =
                            uppercase
                                ? char.ToUpperInvariant(value[0])
                                : char.ToLowerInvariant(value[0]);

                        _text += character;

                        if (_shiftActive) {
                            _shiftActive = false;
                            UpdateLetterKeys();
                        }
                    } else {
                        _text += value;
                    }
                    break;
            }

            UpdatePreview();
        }

        private void UpdatePreview() {
            PreviewText.Text = _text;
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

        public void HandleControllerAction(ControllerAction action) {
            FindNextElementOptions options = new() {
                SearchRoot = KeyboardRoot
            };

            switch (action) {
                case ControllerAction.NavigateUp:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Up,
                        options);
                    break;

                case ControllerAction.NavigateDown:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Down,
                        options);
                    break;

                case ControllerAction.NavigateLeft:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Left,
                        options);
                    break;

                case ControllerAction.NavigateRight:
                    FocusManager.TryMoveFocus(
                        FocusNavigationDirection.Right,
                        options);
                    break;

                case ControllerAction.Accept:
                    if (FocusManager.GetFocusedElement(
                            KeyboardRoot.XamlRoot)
                        is Button button) {
                        ActivateKey(button);
                    }
                    break;
                case ControllerAction.Back:
                    Cancelled?.Invoke();
                    break;
            }
        }

        private void UpdateLetterKeys() {
            if (_symbolMode) {
                return;
            }

            bool uppercase =
                _capsLock ^ _shiftActive;

            foreach (Button button in _letterButtons) {
                if (button.Tag is not string value ||
                    value.Length != 1) {
                    continue;
                }

                char character = value[0];

                button.Content =
                    uppercase
                        ? char.ToUpperInvariant(character).ToString()
                        : char.ToLowerInvariant(character).ToString();
            }

            if (_shiftButton != null) {
                _shiftButton.Content =
                    _shiftActive
                        ? "Shift ✓"
                        : "Shift";
            }

            if (_rightShiftButton != null) {
                _rightShiftButton.Content =
                    _shiftActive
                        ? "Shift ✓"
                        : "Shift";
            }

            if (_capsButton != null) {
                _capsButton.Content =
                    _capsLock
                        ? "Caps ✓"
                        : "Caps";
            }

            SetActionButtonActive(
                _shiftButton,
                _shiftActive);

            SetActionButtonActive(
                _rightShiftButton,
                _shiftActive);

            SetActionButtonActive(
                _capsButton,
                _capsLock);
        }

        private void UpdateKeyboardLayout() {
            string[] layout =
                _symbolMode
                    ? _symbolRows
                    : _rows;

            int buttonIndex = 0;

            foreach (string row in layout) {
                foreach (char character in row) {
                    if (buttonIndex >= _letterButtons.Count)
                        return;

                    Button button =
                        _letterButtons[buttonIndex];

                    button.Tag =
                        character.ToString();

                    if (_symbolMode) {
                        button.Content =
                            character.ToString();
                    } else {
                        bool uppercase =
                            _capsLock ^ _shiftActive;

                        button.Content =
                            uppercase
                                ? char.ToUpperInvariant(character).ToString()
                                : char.ToLowerInvariant(character).ToString();
                    }

                    buttonIndex++;
                }
            }

            if (_symbolButton != null) {
                _symbolButton.Content =
                    _symbolMode
                        ? "ABC"
                        : "123";
            }

            if (_shiftButton != null) {
                _shiftButton.IsEnabled =
                    !_symbolMode;
            }

            if (_rightShiftButton != null) {
                _rightShiftButton.IsEnabled =
                    !_symbolMode;
            }

            if (_capsButton != null) {
                _capsButton.IsEnabled =
                    !_symbolMode;
            }
        }

        private Button CreateDefinitionButton( KeyboardKeyDefinition definition) {
            StackPanel content = new() {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(definition.Secondary)) {
                content.Children.Add(
                    new TextBlock {
                        Text = definition.Secondary,
                        FontSize = 13,
                        Opacity = 0.55,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
            }

            content.Children.Add(
                new TextBlock {
                    Text = definition.Primary,
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

            Button button = new() {
                Content = content,
                Tag = definition,
                Width = definition.Width,
                Height = 68
            };

            button.Click += KeyButton_Click;

            return button;
        }

        public void SetMoveModeActive(bool active) {
            if (_moveButton == null)
                return;

            _moveButton.Content =
                active
                    ? "Moving…"
                    : "Move keyboard";

            SetActionButtonActive(
                _moveButton,
                active);
        }

        private static void SetActionButtonActive(Button? button, bool active) {
            if (button == null)
                return;

            button.Background =
                new SolidColorBrush(
                    active
                        ? Color.FromArgb(255, 70, 110, 180)
                        : Color.FromArgb(255, 45, 45, 45));

            button.BorderThickness =
                active
                    ? new Thickness(3)
                    : new Thickness(1);
        }
    }
}