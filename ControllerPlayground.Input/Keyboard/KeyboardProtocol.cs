using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControllerPlayground.Input.Keyboard;

public enum KeyboardPeer { Shell = 1, Host = 2 }
[Flags]
public enum KeyboardCapabilities { None = 0, DeviceAttribution = 1, OwnershipEpochs = 2, ExecutionAcknowledgements = 4, BoundedFrames = 8 }
public enum KeyboardSuspendReason { FocusLost = 1, OwnerDisconnected = 2, SourceChanged = 3, QueueOverflow = 4, SequenceGap = 5 }
public enum KeyboardCloseReason { Finished = 1, Cancelled = 2, TargetGone = 3, Timeout = 4, ProtocolError = 5 }
public enum KeyboardErrorCode { InvalidState = 1, IncompatibleVersion = 2, Busy = 3, UnsupportedCapability = 4 }

public static class KeyboardProtocol {
    public const int Version = 2;
    public const int MaxFrameBytes = 256 * 1024;
    public const int MaxTextCodeUnits = 16 * 1024;
    public const int MaxDevices = 32;
    public const int MaxActions = 64;
    public const int MaxPendingCommands = 64;
    public const KeyboardCapabilities RequiredCapabilities = KeyboardCapabilities.DeviceAttribution |
        KeyboardCapabilities.OwnershipEpochs | KeyboardCapabilities.ExecutionAcknowledgements | KeyboardCapabilities.BoundedFrames;
    public static bool IsNavigation(ControllerAction action) => action is ControllerAction.NavigateUp or ControllerAction.NavigateDown or
        ControllerAction.NavigateLeft or ControllerAction.NavigateRight;
    public static bool IsKeyboardAction(ControllerAction action) => IsNavigation(action) || action is ControllerAction.Accept or
        ControllerAction.Back or ControllerAction.X or ControllerAction.Y or ControllerAction.RightTrigger or ControllerAction.Menu;
}

// Generations are scoped to CollectorId. Neither endpoint nor root ID is assumed
// to have global identity. Only an independently verified binding joins collectors.
public sealed record KeyboardDeviceRef(
    [property: JsonRequired] Guid CollectorId,
    [property: JsonRequired] string DeviceId,
    [property: JsonRequired] string RootId,
    [property: JsonRequired] ulong Generation,
    [property: JsonRequired, JsonConverter(typeof(KeyboardFamilyJsonConverter))] ControllerFamily Family);
public sealed record KeyboardOwnerBinding(
    [property: JsonRequired] Guid BindingId,
    [property: JsonRequired] KeyboardDeviceRef ShellDevice,
    [property: JsonRequired] KeyboardDeviceRef CaptureDevice);
public sealed record KeyboardCapturedAction(
    [property: JsonRequired] KeyboardDeviceRef Device,
    [property: JsonRequired] ulong Sequence,
    [property: JsonRequired, JsonConverter(typeof(KeyboardActionJsonConverter))] ControllerAction Action,
    [property: JsonRequired] bool IsRepeat,
    [property: JsonRequired] ulong NativeTimestampMicroseconds,
    [property: JsonRequired] long ObservedAtMilliseconds);
public sealed record KeyboardHeader(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid ConnectionId,
    [property: JsonRequired] Guid ShellInstanceId,
    [property: JsonRequired] Guid HostInstanceId,
    [property: JsonRequired] Guid SessionId,
    [property: JsonRequired] KeyboardPeer Sender,
    [property: JsonRequired] ulong OwnershipEpoch,
    [property: JsonRequired] ulong Sequence);
public sealed record KeyboardEnvelope(
    [property: JsonRequired] KeyboardHeader Header,
    [property: JsonRequired] KeyboardMessage Message);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyboardHello), "hello")]
[JsonDerivedType(typeof(KeyboardHelloAck), "helloAck")]
[JsonDerivedType(typeof(KeyboardOpen), "open")]
[JsonDerivedType(typeof(KeyboardReady), "ready")]
[JsonDerivedType(typeof(KeyboardSetOwner), "setOwner")]
[JsonDerivedType(typeof(KeyboardOwnerReady), "ownerReady")]
[JsonDerivedType(typeof(KeyboardDevicesChanged), "devicesChanged")]
[JsonDerivedType(typeof(KeyboardCaptureBatch), "captureBatch")]
[JsonDerivedType(typeof(KeyboardExecute), "execute")]
[JsonDerivedType(typeof(KeyboardExecuted), "executed")]
[JsonDerivedType(typeof(KeyboardSuspend), "suspend")]
[JsonDerivedType(typeof(KeyboardDraftSnapshot), "draft")]
[JsonDerivedType(typeof(KeyboardCommit), "commit")]
[JsonDerivedType(typeof(KeyboardCancel), "cancel")]
[JsonDerivedType(typeof(KeyboardClose), "close")]
[JsonDerivedType(typeof(KeyboardClosed), "closed")]
[JsonDerivedType(typeof(KeyboardPing), "ping")]
[JsonDerivedType(typeof(KeyboardPong), "pong")]
[JsonDerivedType(typeof(KeyboardError), "error")]
public abstract record KeyboardMessage;
public sealed record KeyboardHello([property: JsonRequired] int MinimumVersion, [property: JsonRequired] int MaximumVersion,
    [property: JsonRequired] KeyboardCapabilities RequiredCapabilities) : KeyboardMessage;
public sealed record KeyboardHelloAck([property: JsonRequired] int SelectedVersion,
    [property: JsonRequired] KeyboardCapabilities Capabilities) : KeyboardMessage;
public sealed record KeyboardOpen([property: JsonRequired] string InitialText,
    [property: JsonRequired] KeyboardDeviceRef? ShellOwner) : KeyboardMessage;
public sealed record KeyboardReady([property: JsonRequired] long WindowHandle, [property: JsonRequired] uint ProcessId,
    [property: JsonRequired] Guid CaptureCollectorId, [property: JsonRequired] ImmutableArray<KeyboardDeviceRef> Devices) : KeyboardMessage;
public sealed record KeyboardSetOwner([property: JsonRequired] KeyboardOwnerBinding Binding) : KeyboardMessage;
public sealed record KeyboardOwnerReady([property: JsonRequired] Guid BindingId, [property: JsonRequired] bool IsNeutral,
    [property: JsonRequired] bool IsForeground, [property: JsonRequired] ulong CaptureSequenceFloor) : KeyboardMessage;
public sealed record KeyboardDevicesChanged([property: JsonRequired] Guid CaptureCollectorId,
    [property: JsonRequired] ulong SnapshotSequence, [property: JsonRequired] ImmutableArray<KeyboardDeviceRef> Devices) : KeyboardMessage;
public sealed record KeyboardCaptureBatch([property: JsonRequired] Guid CaptureCollectorId,
    [property: JsonRequired] bool IsForeground, [property: JsonRequired] ImmutableArray<KeyboardCapturedAction> Actions) : KeyboardMessage;
public sealed record KeyboardExecute([property: JsonRequired] ulong CommandId,
    [property: JsonRequired] KeyboardCapturedAction SourceAction) : KeyboardMessage;
public sealed record KeyboardExecuted([property: JsonRequired] ulong CommandId) : KeyboardMessage;
public sealed record KeyboardSuspend([property: JsonRequired] KeyboardSuspendReason Reason) : KeyboardMessage;
public sealed record KeyboardDraftSnapshot([property: JsonRequired] ulong DraftRevision, [property: JsonRequired] string Text) : KeyboardMessage;
public sealed record KeyboardCommit([property: JsonRequired] string Text) : KeyboardMessage;
public sealed record KeyboardCancel : KeyboardMessage;
public sealed record KeyboardClose([property: JsonRequired] KeyboardCloseReason Reason) : KeyboardMessage;
public sealed record KeyboardClosed : KeyboardMessage;
public sealed record KeyboardPing([property: JsonRequired] ulong Nonce) : KeyboardMessage;
public sealed record KeyboardPong([property: JsonRequired] ulong Nonce) : KeyboardMessage;
public sealed record KeyboardError([property: JsonRequired] KeyboardErrorCode Code) : KeyboardMessage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 24)]
[JsonSerializable(typeof(KeyboardEnvelope))]
internal partial class KeyboardJsonContext : JsonSerializerContext;

// Existing managed enum ordinals are not a wire contract. Encode their names,
// rejecting numeric aliases and case/unknown-name coercion.
public sealed class KeyboardActionJsonConverter : JsonConverter<ControllerAction> {
    public override ControllerAction Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } name ||
            !Enum.TryParse<ControllerAction>(name, out var value) || Enum.GetName(value) != name) throw new JsonException();
        return value;
    }
    public override void Write(Utf8JsonWriter writer, ControllerAction value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
public sealed class KeyboardFamilyJsonConverter : JsonConverter<ControllerFamily> {
    public override ControllerFamily Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } name ||
            !Enum.TryParse<ControllerFamily>(name, out var value) || Enum.GetName(value) != name) throw new JsonException();
        return value;
    }
    public override void Write(Utf8JsonWriter writer, ControllerFamily value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
