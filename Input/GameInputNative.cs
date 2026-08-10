using System;
using System.Runtime.InteropServices;

namespace ControllerPlayground.Input {
    [Flags]
    internal enum GameInputKind : uint {
        unknown = 0x00000000,
        Gamepad = 0x00040000
    }

    [Flags]
    internal enum GameInputGamepadButtons : uint {
        none = 0x00000000,

        menu = 0x00000001,
        view = 0x00000002,

        A = 0x00000004,
        B = 0x00000008,
        X = 0x00000010,
        Y = 0x00000020,

        DpadUp = 0x00000040,
        DpadDown = 0x00000080,
        DpadLeft = 0x00000100,
        DpadRight = 0x00000200,

        LeftShoulder = 0x00000400,
        RightShoulder = 0x00000800,

        LeftThumbStick = 0x00001000,
        RightThumbStick = 0x00002000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputGamepadState {
        public GameInputGamepadButtons Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftThumbstickX;
        public float LeftThumbstickY;
        public float RightThumbstickX;
        public float RightThumbstickY;
    }

    [ComImport]
    [Guid("20EFC1C7-5D9A-43BA-B26F-B807FA48609C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGameInput {
        [PreserveSig]
        ulong GetCurrentTimestamp();

        [PreserveSig]
        int GetCurrentReading(
            GameInputKind inputKind,
            IntPtr device,
            out IGameInputReading reading
        );
    }

    [ComImport]
    [Guid("C81C4CDE-ED1A-4631-A30F-C556A6241A1F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGameInputReading {
        GameInputKind GetInputKind();

        ulong GetTimestamp();

        void GetDevice(out IntPtr device);

        uint GetControllerAxisCount();

        uint GetControllerAxisState(
            uint stateArrayCount,
            IntPtr stateArray
        );

        uint GetControllerButtonCount();

        uint GetControllerButtonState(
            uint stateArrayCount,
            IntPtr stateArray
        );

        uint GetControllerSwitchCount();

        uint GetControllerSwitchState(
            uint stateArrayCount,
            IntPtr stateArray
        );

        uint GetKeyCount();

        uint GetKeyState(
            uint stateArrayCount,
            IntPtr stateArray
        );

        bool GetMouseState(IntPtr state);

        bool GetSensorsState(IntPtr state);

        bool GetArcadeStickState(IntPtr state);

        bool GetFlightStickState(IntPtr state);

        [return: MarshalAs(UnmanagedType.I1)]
        bool GetGamepadState(
            out GameInputGamepadState state
        );
    }
}