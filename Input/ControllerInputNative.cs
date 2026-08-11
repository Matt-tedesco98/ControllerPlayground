using System.Runtime.InteropServices;

namespace ControllerPlayground.Input;

internal static class ControllerInputNative {
    [DllImport("ControllerInput.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ControllerInput_Initialize();
}
