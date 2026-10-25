namespace ControllerPlayground.Input;

// One frame is a complete native device snapshot plus ordered lifecycle changes.
// Input/Guide edges come only from snapshots; replaying their diagnostic events
// as well would duplicate actions. A missing reading is NOT a disconnection.
public sealed record ControllerInputFrame(
    IReadOnlyList<ControllerDeviceSnapshotV2> Devices,
    IReadOnlyList<ControllerCaptureEventV2> ConnectionEvents,
    bool ContinuityLost = false,
    IReadOnlyDictionary<ControllerDeviceId, ControllerState>? NavigationProjections = null,
    bool LegacyGuideRequested = false);

public interface IControllerInputSource {
    ControllerInputFrame ReadFrame();
}
