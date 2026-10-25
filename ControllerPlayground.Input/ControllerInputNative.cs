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

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ControllerDeviceSnapshotV2 {
    public uint StructSize;
    public uint Connected;
    public ControllerDeviceId DeviceId;
    public ControllerDeviceId DeviceRootId;
    public ulong ConnectionGeneration;
    public ulong Timestamp;
    public ushort VendorId;
    public ushort ProductId;
    public uint ReadingAvailable;
    public ControllerState State;
    public uint PressedButtons;
    public uint ReleasedButtons;
    public uint GuidePressed;
    public uint HistoryDiscontinuity;
}

public enum ControllerCaptureEventKindV2 : uint {
    Connected = 1, Disconnected = 2, Input = 3, Guide = 4, HistoryReset = 5
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ControllerCaptureEventV2 {
    public uint StructSize;
    public ControllerCaptureEventKindV2 Kind;
    public ulong Sequence;
    public ControllerDeviceSnapshotV2 Device;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ControllerCaptureDiagnosticsV2 {
    public uint StructSize;
    public uint GameInputApiVersion;
    public int InitializationResult;
    public int DeviceCallbackResult;
    public int GuideCallbackResult;
    public uint ConnectedDevices;
    public ulong DroppedEvents;
    public ulong UnattributedRawGuidePresses;
    public ulong HistoryResets;
}

public static class ControllerInputNative {
    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ControllerInput_GetApiVersion();

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ControllerInput_PollDevicesV2(
        [Out] ControllerDeviceSnapshotV2[]? devices, uint capacity, uint stride, out uint required);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ControllerInput_DrainEventsV2(
        [Out] ControllerCaptureEventV2[] events, uint capacity, uint stride, out uint written);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ControllerInput_CopyNavigationProjectionV2(
        in ControllerDeviceId deviceId, ulong generation, out ControllerState state);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ControllerInput_GetDiagnosticsV2(out ControllerCaptureDiagnosticsV2 diagnostics, uint size);

    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ControllerInput_Shutdown();

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
