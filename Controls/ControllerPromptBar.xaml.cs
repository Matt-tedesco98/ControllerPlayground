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
            DependencyPropertyChangedEventArgs e) {
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

        public static readonly DependencyProperty ShowAcceptProperty = DependencyProperty.Register(
            nameof(ShowAccept),
            typeof(bool),
            typeof(ControllerPromptBar),
            new PropertyMetadata(true, OnPromptVisibilityChanged));


        public static readonly DependencyProperty ShowBackProperty = DependencyProperty.Register(
            nameof(ShowBack),
            typeof(bool),
            typeof(ControllerPromptBar),
            new PropertyMetadata(true, OnPromptVisibilityChanged));

        public static readonly DependencyProperty ShowViewProperty = DependencyProperty.Register(
            nameof(ShowView),
            typeof(bool),
            typeof(ControllerPromptBar),
            new PropertyMetadata(true, OnPromptVisibilityChanged));

        public static readonly DependencyProperty ShowMenuProperty = DependencyProperty.Register(
            nameof(ShowMenu),
            typeof(bool),
            typeof(ControllerPromptBar),
            new PropertyMetadata(true, OnPromptVisibilityChanged));

        public bool ShowAccept {
            get => (bool)GetValue(ShowAcceptProperty);
            set => SetValue(ShowAcceptProperty, value);
        }

        public bool ShowBack {
            get => (bool)GetValue(ShowBackProperty);
            set => SetValue(ShowBackProperty, value);
        }

        public bool ShowView {
            get => (bool)GetValue(ShowViewProperty);
            set => SetValue(ShowViewProperty, value);
        }

        public bool ShowMenu {
            get => (bool)GetValue(ShowMenuProperty);
            set => SetValue(ShowMenuProperty, value);
        }

        private static void OnPromptVisibilityChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e) {
            if (d is ControllerPromptBar bar) {
                bar.UpdatePromptVisibility();
            }
        }

        private void UpdatePromptVisibility() {
            AcceptPrompt.Visibility = ShowAccept ? Visibility.Visible : Visibility.Collapsed;
            BackPrompt.Visibility = ShowBack ? Visibility.Visible : Visibility.Collapsed;
            ViewPrompt.Visibility = ShowView ? Visibility.Visible : Visibility.Collapsed;
            MenuPrompt.Visibility = ShowMenu ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
    
