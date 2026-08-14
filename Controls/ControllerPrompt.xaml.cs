using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ControllerPlayground.Controls {
    public sealed partial class ControllerPrompt : UserControl {

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(ControllerPrompt),
                new PropertyMetadata("Select"));

        public string Label {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
            nameof(Family),
            typeof(ControllerFamily),
            typeof(ControllerPrompt),
            new PropertyMetadata(ControllerFamily.Unknown));

        public static readonly DependencyProperty ButtonProperty = DependencyProperty.Register(
            nameof(Button),
            typeof(ControllerButton),
            typeof(ControllerPrompt),
            new PropertyMetadata(ControllerButton.Accept));

        public ControllerFamily Family {
            get => (ControllerFamily)GetValue(FamilyProperty);
            set => SetValue(FamilyProperty, value);
        }

        public ControllerButton Button {
            get => (ControllerButton)GetValue(ButtonProperty);
            set => SetValue(ButtonProperty, value);
        }

        public ControllerPrompt() {
            InitializeComponent();
        }
    }
}
