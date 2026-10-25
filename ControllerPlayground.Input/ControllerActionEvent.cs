namespace ControllerPlayground.Input;

public sealed record ControllerDevice(
    ControllerDeviceId Id, ControllerDeviceId RootId, ulong ConnectionGeneration,
    ushort VendorId, ushort ProductId, ControllerFamily Family);

// Sequence is monotonic within CollectorSessionId, not comparable across processes.
// NativeTimestampMicroseconds is the sample timestamp; repeat events may share it.
public sealed record ControllerActionEvent(
    Guid CollectorSessionId, ulong Sequence, ControllerDevice Device,
    ControllerAction Action, ulong NativeTimestampMicroseconds,
    long ObservedAtMilliseconds, bool IsRepeat);

public sealed record ControllerConnectionEvent(
    ControllerDevice Device, bool Connected, ulong NativeTimestampMicroseconds,
    ulong? NativeSequence);
