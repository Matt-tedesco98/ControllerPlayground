namespace ControllerPlayground.Input.Keyboard;

public static class KeyboardPipeNames {
    public static string Version2 => "ControllerPlayground.KeyboardHost.v2." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    public const string Legacy = "ControllerPlayground.KeyboardHost";
}
