namespace ControllerPlayground.Input {
    internal static class ControllerGlyphs {
        public static string GetAcceptGlyph(ControllerFamily family) {
            return family switch {
                ControllerFamily.Xbox => "A", // Xbox A button
                ControllerFamily.PlayStation => "X", // PlayStation Cross button
                _ => "Enter" // Default to Enter key
            };
        }

        public static string GetBackGlyph(ControllerFamily family) {
            return family switch {
                ControllerFamily.Xbox => "B", // Xbox B button
                ControllerFamily.PlayStation => "O", // PlayStation Circle button
                _ => "ESC" // Default to ESC button
            };
        }
    }
}
