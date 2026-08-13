using System;

namespace ControllerPlayground.Input {
    [Flags]
    internal enum ControllerButtons : uint {
        none = 0x00000000,

        Menu = 0x00000001,
        View = 0x00000002,

        Accept = 0x00000004,
        Back = 0x00000008,
        X = 0x00000010,
        Y = 0x00000020,

        DpadUp = 0x00000040,
        DpadDown = 0x00000080,
        DpadLeft = 0x00000100,
        DpadRight = 0x00000200,

        LeftShoulder = 0x00000400,
        RightShoulder = 0x00000800,

        LeftStick = 0x00001000,
        RightStick = 0x00002000,
    }
}