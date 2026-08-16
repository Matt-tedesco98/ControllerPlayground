using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ControllerPlayground.Input;

namespace ControllerPlayground.Controls {
    public sealed partial class ControllerPromptBar : UserControl {
        public ControllerPromptBar() {
            InitializeComponent();
        }

        public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
            nameof(Family),
            typeof(ControllerFamily),
            typeof(ControllerPromptBar),
            new PropertyMetadata(ControllerFamily.Unknown, OnFamilyChanged));

        public ControllerFamily Family {
            get => (ControllerFamily)GetValue(FamilyProperty);
            set => SetValue(FamilyProperty, value);
        }

        private static void OnFamilyChanged(
            DependencyObject d, 
            DependencyPropertyChangedEventArgs e ) {
            if (d is ControllerPromptBar bar) {
                bar.UpdatePrompt();
            }
        }

        private void UpdatePrompt() {
            // Update the prompt based on the Family property
            AcceptPrompt.Family = Family;
            BackPrompt.Family = Family;
            ViewPrompt.Family = Family;
            MenuPrompt.Family = Family;
        }
    }
}
