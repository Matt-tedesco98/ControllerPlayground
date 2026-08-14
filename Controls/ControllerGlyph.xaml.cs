using ControllerPlayground;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ControllerPlayground.Controls {
    public sealed partial class ControllerGlyph : UserControl {

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
                nameof(Glyph),
                typeof(string),
                typeof(ControllerGlyph),
                new PropertyMetadata("A"));
        public string Glyph {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public static readonly DependencyProperty FamilyProperty =
    DependencyProperty.Register(
        nameof(Family),
        typeof(ControllerFamily),
        typeof(ControllerGlyph),
        new PropertyMetadata(
            ControllerFamily.Unknown,
            OnGlyphPropertyChanged));

        public static readonly DependencyProperty ButtonProperty =
            DependencyProperty.Register(
                nameof(Button),
                typeof(ControllerButton),
                typeof(ControllerGlyph),
                new PropertyMetadata(
                    ControllerButton.Accept,
                    OnGlyphPropertyChanged));

        public ControllerFamily Family {
            get => (ControllerFamily)GetValue(FamilyProperty);
            set => SetValue(FamilyProperty, value);
        }

        public ControllerButton Button {
            get => (ControllerButton)GetValue(ButtonProperty);
            set => SetValue(ButtonProperty, value);
        }

        private static void OnGlyphPropertyChanged(
    DependencyObject d,
    DependencyPropertyChangedEventArgs e) {
            if (d is ControllerGlyph glyph) {
                glyph.UpdateGlyph();
            }
        }

        private void UpdateGlyph() {
            Glyph = ControllerGlyphs.GetGlyph(Family, Button);
        }

        public ControllerGlyph() {
            InitializeComponent();
        }
    }
}
