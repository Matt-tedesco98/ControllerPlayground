namespace ControllerPlayground.Input {
    internal static class ControllerGlyphs {
        public static string GetGlyph(ControllerFamily family, ControllerButton button) {
            return (family, button) switch {
                (ControllerFamily.Xbox, ControllerButton.Accept) => "A",
                (ControllerFamily.Xbox, ControllerButton.Back) => "B",

                (ControllerFamily.PlayStation, ControllerButton.Accept) => "X",
                (ControllerFamily.PlayStation, ControllerButton.Back) => "O",

                (_, ControllerButton.Accept) => "Enter",
                (_, ControllerButton.Back) => "Esc",

                _ => "?"
            };
        }

        public static string? GetAssetPath(ControllerFamily family, ControllerButton button) {
            return (family, button) switch {
                // Xbox controller glyphs
                (ControllerFamily.Xbox, ControllerButton.Accept) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_button_a.svg",
                (ControllerFamily.Xbox, ControllerButton.Back) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_button_b.svg",
                (ControllerFamily.Xbox, ControllerButton.View) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_button_select.svg",
                (ControllerFamily.Xbox, ControllerButton.Menu) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_button_start.svg",
                (ControllerFamily.Xbox, ControllerButton.LeftShoulder) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_lb.svg",
                (ControllerFamily.Xbox, ControllerButton.RightShoulder) => "ms-appx:///Assets/ControllerGlyphs/Xbox/xbox_rb.svg",

                // PlayStation controller glyphs
                (ControllerFamily.PlayStation, ControllerButton.Accept) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps_button_x.svg",
                (ControllerFamily.PlayStation, ControllerButton.Back) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps_button_circle.svg",
                (ControllerFamily.PlayStation, ControllerButton.View) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps5_trackpad.svg",
                (ControllerFamily.PlayStation, ControllerButton.Menu) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps5_button_options.svg",
                (ControllerFamily.PlayStation, ControllerButton.LeftShoulder) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps5_l1.svg",
                (ControllerFamily.PlayStation, ControllerButton.RightShoulder) => "ms-appx:///Assets/ControllerGlyphs/PlayStation/ps5_r1.svg",
                _ => null
            };
        }
    }
}
