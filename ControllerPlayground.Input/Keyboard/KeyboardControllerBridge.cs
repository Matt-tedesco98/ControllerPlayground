using System.Collections.Immutable;

namespace ControllerPlayground.Input.Keyboard;

// Delegates marshal to each application's UI thread. No delegate executes a
// controller action unless it invokes the supplied one-shot authorization gate.
public sealed record KeyboardControllerBridge {
    public Func<CancellationToken, Task<ImmutableArray<KeyboardDeviceRef>>>? ShellDevices { get; init; }
    public Func<KeyboardOwnerBinding, CancellationToken, Task<bool>>? ShellOwnerChanged { get; init; }
    public Func<KeyboardOwnerBinding, Func<bool>, CancellationToken, Task>? HostOwnerChanged { get; init; }
    public Func<KeyboardCapturedAction, Func<Action, bool>, CancellationToken, Task<bool>>? HostExecute { get; init; }
}
public sealed record KeyboardCaptureSnapshot(Guid SessionId, Guid CollectorId,
    ImmutableArray<KeyboardDeviceRef> Devices, ImmutableHashSet<KeyboardDeviceRef> Neutral,
    bool Foreground, ImmutableArray<KeyboardCapturedAction> Actions);

public static class KeyboardControllerBinding {
    // Phase 0 verified equality for these package peers. Runtime binding still
    // requires a unique full endpoint AND root match in both inventories. If the
    // app-local identities differ, fail closed; VID/PID/family is never a fallback.
    public static KeyboardOwnerBinding? Resolve(KeyboardDeviceRef source, IEnumerable<KeyboardDeviceRef> shell,
        IEnumerable<KeyboardDeviceRef> capture) {
        static bool Same(KeyboardDeviceRef a, KeyboardDeviceRef b) => a.DeviceId == b.DeviceId && a.RootId == b.RootId && a.Family == b.Family;
        var left = shell.Where(d => Same(d, source)).ToArray();
        var right = capture.Where(d => Same(d, source)).ToArray();
        return left.Length == 1 && right.Length == 1 && (source == left[0] || source == right[0])
            ? new(Guid.NewGuid(), left[0], right[0]) : null;
    }
    public static KeyboardDeviceRef Describe(Guid collector, ControllerDevice device) => new(collector,
        device.Id.ToString(), device.RootId.ToString(), device.ConnectionGeneration, device.Family);
    public static ImmutableArray<KeyboardCapturedAction> OrderBatch(IReadOnlyList<ControllerActionEvent> actions, ref ulong sequence) {
        var result = ImmutableArray.CreateBuilder<KeyboardCapturedAction>();
        // Modifier edges precede this poll's character/space/commit actions.
        // All actions are retained; the original per-device timing is preserved.
        foreach (var action in actions.OrderBy(a => a.Action == ControllerAction.RightTrigger ? 0 : 1))
            result.Add(new(Describe(action.CollectorSessionId, action.Device), ++sequence, action.Action,
                action.IsRepeat, action.NativeTimestampMicroseconds, action.ObservedAtMilliseconds));
        return result.ToImmutable();
    }
}
