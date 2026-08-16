using System;

namespace ControllerPlayground.Input {
    [Flags]
    internal enum  ControllerSystemButtons : uint {
        None = 0x00000000,
        Guide = 0x00000001
    }
}
