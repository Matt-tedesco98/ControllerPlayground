using System.Runtime.InteropServices;

namespace ControllerPlayground.Input;

[StructLayout(LayoutKind.Sequential)]
public struct ControllerState {
    public uint Buttons;
    public uint SystemButtons;
    public float LeftTrigger;
    public float RightTrigger;
    public float LeftThumbstickX;
    public float LeftThumbstickY;
    public float RightThumbstickX;
    public float RightThumbstickY;
}

[StructLayout(LayoutKind.Sequential)]
public struct ControllerDeviceInfo {
    public ushort VendorId;
    public ushort ProductId;
}

public static class ControllerInputNative {
    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ControllerInput_Initialize();

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ControllerInput_GetState(out ControllerState state);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ControllerInput_GetDeviceInfo(out ControllerDeviceInfo info);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ControllerInput_SetWindowHandle(nint windowHandle);
}
