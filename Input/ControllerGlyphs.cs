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
    }
}
