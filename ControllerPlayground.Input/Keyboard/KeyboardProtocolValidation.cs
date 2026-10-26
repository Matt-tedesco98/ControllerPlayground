using System.Collections.Immutable;
using System.Text;

namespace ControllerPlayground.Input.Keyboard;

public sealed class KeyboardProtocolException : IOException {
    public KeyboardProtocolException(string message) : base(message) { }
}

public static class KeyboardProtocolValidation {
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static void Require(bool condition, string reason) { if (!condition) throw new KeyboardProtocolException(reason); }
    private static void Id(Guid value) => Require(value != Guid.Empty, "Missing identity.");
    private static void OpaqueId(string? value) => Require(value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'), "Device ID must be 32 canonical opaque bytes.");
    private static void Text(string? text) {
        Require(text != null && text.Length <= KeyboardProtocol.MaxTextCodeUnits, "Text exceeds the protocol limit or is missing.");
        try { StrictUtf8.GetByteCount(text!); } catch (EncoderFallbackException) { throw new KeyboardProtocolException("Invalid Unicode text."); }
    }
    private static void Device(KeyboardDeviceRef? device) {
        Require(device != null, "Missing device reference.");
        Id(device!.CollectorId); OpaqueId(device.DeviceId); OpaqueId(device.RootId);
        Require(device.Generation != 0 && Enum.IsDefined(device.Family), "Invalid device metadata.");
    }
    private static void Inventory(Guid collector, ImmutableArray<KeyboardDeviceRef> devices) {
        Id(collector);
        Require(!devices.IsDefault && devices.Length <= KeyboardProtocol.MaxDevices, "Invalid device inventory size.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var device in devices) {
            Device(device);
            Require(device.CollectorId == collector && ids.Add(device.DeviceId), "Inventory contains another collector or duplicate endpoint.");
        }
    }
    private static void Action(KeyboardCapturedAction? input) {
        Require(input != null, "Missing captured action."); Device(input!.Device);
        Require(input.Sequence > 0 && input.ObservedAtMilliseconds >= 0 && Enum.IsDefined(input.Action) && input.Action != ControllerAction.none,
            "Invalid captured action.");
        Require(!input.IsRepeat || KeyboardProtocol.IsNavigation(input.Action), "Only navigation may repeat.");
    }
    public static void Validate(KeyboardEnvelope? envelope) {
        Require(envelope?.Header != null && envelope.Message != null, "Missing envelope/header/message.");
        var h = envelope!.Header;
        Require(h.Version == KeyboardProtocol.Version, "Unsupported protocol version.");
        Id(h.ConnectionId); Id(h.ShellInstanceId);
        Require(Enum.IsDefined(h.Sender) && h.Sequence > 0, "Invalid sender/sequence.");
        bool hello = envelope.Message is KeyboardHello or KeyboardHelloAck;
        if (hello) Require(h.SessionId == Guid.Empty && h.OwnershipEpoch == 0, "Handshake must not claim a session.");
        else { Id(h.SessionId); Id(h.HostInstanceId); Require(h.OwnershipEpoch > 0, "Missing ownership epoch."); }
        bool shell = envelope.Message is KeyboardHello or KeyboardOpen or KeyboardSetOwner or KeyboardExecute or KeyboardSuspend or KeyboardClose;
        bool either = envelope.Message is KeyboardPing or KeyboardPong or KeyboardError;
        Require(either || h.Sender == (shell ? KeyboardPeer.Shell : KeyboardPeer.Host), "Message from wrong peer.");
        switch (envelope.Message) {
            case KeyboardHello m:
                Require(h.HostInstanceId == Guid.Empty && m.MinimumVersion > 0 && m.MaximumVersion >= m.MinimumVersion &&
                    m.MinimumVersion <= KeyboardProtocol.Version && m.MaximumVersion >= KeyboardProtocol.Version,
                    "Incompatible handshake version.");
                Require((m.RequiredCapabilities & KeyboardProtocol.LifecycleCapabilities) == KeyboardProtocol.LifecycleCapabilities &&
                    (m.RequiredCapabilities & ~KeyboardProtocol.RequiredCapabilities) == 0, "Unsupported required capabilities."); break;
            case KeyboardHelloAck m:
                Id(h.HostInstanceId);
                Require(m.SelectedVersion == KeyboardProtocol.Version &&
                    (m.Capabilities & KeyboardProtocol.LifecycleCapabilities) == KeyboardProtocol.LifecycleCapabilities &&
                    (m.Capabilities & ~KeyboardProtocol.RequiredCapabilities) == 0,
                    "Incompatible handshake capabilities/version."); break;
            case KeyboardOpen m: Text(m.InitialText); if (m.ShellOwner != null) Device(m.ShellOwner); break;
            case KeyboardReady m:
                Require(m.WindowHandle > 0 && m.ProcessId > 0, "Invalid helper window/process metadata.");
                Inventory(m.CaptureCollectorId, m.Devices); break;
            case KeyboardSetOwner m:
                Require(m.Binding != null, "Missing owner binding."); Id(m.Binding!.BindingId);
                Device(m.Binding.ShellDevice); Device(m.Binding.CaptureDevice); break;
            case KeyboardOwnerReady m: Id(m.BindingId); break;
            case KeyboardDevicesChanged m:
                Inventory(m.CaptureCollectorId, m.Devices); Require(m.SnapshotSequence > 0, "Invalid inventory sequence."); break;
            case KeyboardCaptureBatch m:
                Id(m.CaptureCollectorId);
                Require(!m.Actions.IsDefault && m.Actions.Length <= KeyboardProtocol.MaxActions, "Invalid capture batch size.");
                ulong sequence = 0;
                foreach (var input in m.Actions) {
                    Action(input); Require(input.Device.CollectorId == m.CaptureCollectorId && input.Sequence > sequence,
                        "Capture batch collector/order mismatch."); sequence = input.Sequence;
                }
                break;
            case KeyboardExecute m:
                Require(m.CommandId > 0, "Invalid command ID."); Action(m.SourceAction);
                Require(KeyboardProtocol.IsKeyboardAction(m.SourceAction.Action), "Shortcut is not a keyboard command."); break;
            case KeyboardExecuted m: Require(m.CommandId > 0, "Invalid acknowledgement."); break;
            case KeyboardSuspend m: Require(Enum.IsDefined(m.Reason), "Unknown suspension reason."); break;
            case KeyboardDraftSnapshot m: Require(m.DraftRevision > 0, "Invalid draft revision."); Text(m.Text); break;
            case KeyboardCommit m: Text(m.Text); break;
            case KeyboardClose m: Require(Enum.IsDefined(m.Reason), "Unknown close reason."); break;
            case KeyboardPing m: Require(m.Nonce > 0, "Invalid ping."); break;
            case KeyboardPong m: Require(m.Nonce > 0, "Invalid pong."); break;
            case KeyboardError m: Require(Enum.IsDefined(m.Code), "Unknown error code."); break;
            case KeyboardCancel or KeyboardClosed: break;
            default: throw new KeyboardProtocolException("Unknown message type.");
        }
    }
}
