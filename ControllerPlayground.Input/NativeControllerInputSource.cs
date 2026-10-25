using System.Runtime.InteropServices;

namespace ControllerPlayground.Input;

// One v2 consumer per process. Initialization/ HWND / focus policy remain owned
// by the existing host. This class never calls Initialize, Shutdown or SetWindowHandle.
public sealed class NativeControllerInputSource : IControllerInputSource {
    private const int InsufficientBuffer = unchecked((int)0x8007007A);
    private ControllerDeviceSnapshotV2[] _devices = new ControllerDeviceSnapshotV2[4];
    private readonly ControllerCaptureEventV2[] _events = new ControllerCaptureEventV2[256];
    private ulong _lastSequence;
    private ulong _lastDrops;
    private ulong _lastUnattributedGuide;
    private bool _validated;

    public ControllerCaptureDiagnosticsV2 Diagnostics { get; private set; }
    public Action<string>? DiagnosticSink { get; set; }

    public ControllerInputFrame ReadFrame() {
        if (!_validated) {
            if (Marshal.SizeOf<ControllerDeviceId>() != 32 || Marshal.SizeOf<ControllerDeviceSnapshotV2>() != 144 ||
                Marshal.SizeOf<ControllerCaptureEventV2>() != 160 || Marshal.SizeOf<ControllerCaptureDiagnosticsV2>() != 48)
                throw new InvalidOperationException("ControllerInput v2 structure layout mismatch.");
            if (ControllerInputNative.ControllerInput_GetApiVersion() != 2)
                throw new NotSupportedException("The device-aware ControllerInput ABI v2 DLL is required.");
            _validated = true;
        }

        uint required = 0;
        bool copied = false;
        for (int attempt = 0; attempt < 8; ++attempt) {
            int hr = ControllerInputNative.ControllerInput_PollDevicesV2(_devices, (uint)_devices.Length, 144, out required);
            if (hr == InsufficientBuffer) {
                if (required > 4096) throw new InvalidOperationException("Unexpected native controller registry size.");
                _devices = new ControllerDeviceSnapshotV2[Math.Max(1, checked((int)required))];
                continue;
            }
            Marshal.ThrowExceptionForHR(hr);
            copied = true;
            break;
        }
        if (!copied) throw new InvalidOperationException("Controller enumeration changed repeatedly during polling.");
        var snapshots = _devices.AsSpan(0, checked((int)required)).ToArray();
        foreach (var snapshot in snapshots)
            if (snapshot.StructSize != 144) throw new InvalidOperationException("Invalid native snapshot size.");

        Dictionary<ControllerDeviceId, ControllerState> projections = new();
        foreach (var snapshot in snapshots) {
            if (snapshot.Connected == 0 || snapshot.ReadingAvailable == 0) continue;
            var id = snapshot.DeviceId;
            int hr = ControllerInputNative.ControllerInput_CopyNavigationProjectionV2(in id, snapshot.ConnectionGeneration, out var projection);
            if (hr == 0) projections[id] = projection;
            // A hotplug between snapshot and query is expected; never substitute
            // another endpoint or connection's projection.
            else if (hr != unchecked((int)0x838A0001) && hr != unchecked((int)0x838A0003)) Marshal.ThrowExceptionForHR(hr);
        }

        bool lost = false;
        List<ControllerCaptureEventV2> lifecycle = new();
        // Bounded work, even if callbacks continue producing events during drain.
        for (int batch = 0; batch < 16; ++batch) {
            Marshal.ThrowExceptionForHR(ControllerInputNative.ControllerInput_DrainEventsV2(_events, 256, 160, out uint written));
            for (int i = 0; i < written; ++i) {
                var input = _events[i];
                if (input.StructSize != 160 || input.Device.StructSize != 144)
                    throw new InvalidOperationException("Invalid native capture event size.");
                if (input.Sequence != _lastSequence + 1) lost = true;
                _lastSequence = input.Sequence;
                DiagnosticSink?.Invoke($"NATIVE_EVENT seq={input.Sequence} kind={input.Kind} id={input.Device.DeviceId} gen={input.Device.ConnectionGeneration} nativeTime={input.Device.Timestamp} buttons=0x{input.Device.State.Buttons:X8} pressed=0x{input.Device.PressedButtons:X8} released=0x{input.Device.ReleasedButtons:X8} stick=({input.Device.State.LeftThumbstickX:R},{input.Device.State.LeftThumbstickY:R})");
                if (input.Kind is ControllerCaptureEventKindV2.Connected or ControllerCaptureEventKindV2.Disconnected)
                    lifecycle.Add(input);
            }
            if (written < 256) break;
            if (batch == 15) lost = true;
        }
        Marshal.ThrowExceptionForHR(ControllerInputNative.ControllerInput_GetDiagnosticsV2(out var diagnostics, 48));
        if (diagnostics.DroppedEvents != _lastDrops) lost = true;
        _lastDrops = diagnostics.DroppedEvents;
        bool legacyGuide = diagnostics.UnattributedRawGuidePresses > _lastUnattributedGuide;
        _lastUnattributedGuide = diagnostics.UnattributedRawGuidePresses;
        Diagnostics = diagnostics;
        return new(snapshots, lifecycle, lost, projections, legacyGuide);
    }
}
