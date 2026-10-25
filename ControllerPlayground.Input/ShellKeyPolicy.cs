namespace ControllerPlayground.Input;

// Numeric Windows.System.VirtualKey values keep this policy testable without WinUI.
// Key can be remapped to Space/Escape; OriginalKey retains Gamepad*/Navigation*.
public static class ShellKeyPolicy {
    private static bool IsControllerKey(uint key) => key is >= 0xC3 and <= 0xDA or >= 0x88 and <= 0x8F;
    public static bool SuppressUnattributedControllerKey(uint key, uint originalKey) =>
        IsControllerKey(key) || IsControllerKey(originalKey);
}
