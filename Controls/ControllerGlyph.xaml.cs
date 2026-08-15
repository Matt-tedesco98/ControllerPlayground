using ControllerPlayground;
using ControllerPlayground.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

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
                glyph.UpdateVisual();
            }
        }

        private void UpdateGlyph() {
            Glyph = ControllerGlyphs.GetGlyph(Family, Button);
        }

        private void UpdateVisual() {
            string? assetPath = ControllerGlyphs.GetAssetPath(Family, Button);
            if (!string.IsNullOrEmpty(assetPath) && Uri.TryCreate(assetPath, UriKind.Absolute, out Uri? uri)) {
                GlyphImage.Source = new SvgImageSource(uri);
                GlyphImage.Visibility = Visibility.Visible;
                FallbackGlyph.Visibility = Visibility.Collapsed;

            } else {
                Glyph = ControllerGlyphs.GetGlyph(Family, Button);
                GlyphImage.Source = null;
                GlyphImage.Visibility = Visibility.Collapsed;
                FallbackGlyph.Visibility = Visibility.Visible;
            }
        }

        public ControllerGlyph() {
            InitializeComponent();
        }
    }
}
