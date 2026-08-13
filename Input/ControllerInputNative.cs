using System.Runtime.InteropServices;

namespace ControllerPlayground.Input;

[StructLayout(LayoutKind.Sequential)]
internal struct ControllerState {
    public uint Buttons;
    public float LeftTrigger;
    public float RightTrigger;
    public float LeftThumbstickX;
    public float LeftThumbstickY;
    public float RightThumbstickX;
    public float RightThumbstickY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ControllerDeviceInfo {
    public ushort VendorId;
    public ushort ProductId;
}

internal static class ControllerInputNative {
    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ControllerInput_Initialize();

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ControllerInput_GetState(out ControllerState state);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ControllerInput_GetDeviceInfo(out ControllerDeviceInfo info);
}
