using System.Collections.Immutable;
using System.Threading.Channels;

namespace ControllerPlayground.Input.Keyboard;

public sealed partial class KeyboardLifecycleTransport {
    private readonly object _modelGate = new();
    private readonly KeyboardControllerBridge? _controllers;
    private readonly Channel<KeyboardExecutionTicket> _executions = Channel.CreateBounded<KeyboardExecutionTicket>(64);
    private ImmutableArray<KeyboardDeviceRef> _captureDevices = ImmutableArray<KeyboardDeviceRef>.Empty;
    private KeyboardCaptureSnapshot? _liveCapture;
    private ulong _inventorySequence, _commandId;
    private long _captureFrame, _ownerFrameFloor;
    private Guid _ownerUiApplied;
    private bool? _publishedForeground;
    public bool Mediated => _controllers != null && _identity?.Capabilities == KeyboardProtocol.RequiredCapabilities;
    public KeyboardOwnerBinding? ControllerOwner { get { lock (_modelGate) return _model?.Owner; } }

    // UI capture can revoke queued tickets immediately, without waiting behind
    // transport events. The actor still publishes the inventory/focus change.
    public bool PublishCapture(KeyboardCaptureSnapshot snapshot) {
        if (!Mediated || snapshot.SessionId != _session || _closing || _stop.IsCancellationRequested) return false;
        bool revoked = false;
        lock (_modelGate) {
            _liveCapture = snapshot;
            if (_model?.Owner is { } owner && _model.Phase is KeyboardSessionPhase.Active or KeyboardSessionPhase.AwaitingNeutral &&
                (!snapshot.Foreground || !snapshot.Devices.Contains(owner.CaptureDevice))) {
                _model.SuspendLocally(!snapshot.Foreground ? KeyboardSuspendReason.FocusLost : KeyboardSuspendReason.OwnerDisconnected);
                revoked = true;
            }
        }
        Post(new(Kind: "capture", Value: (Interlocked.Increment(ref _captureFrame), revoked ? snapshot with { Actions = ImmutableArray<KeyboardCapturedAction>.Empty } : snapshot)));
        return revoked;
    }
    private bool BindingCurrent(Guid id) {
        lock (_modelGate) return !_closing && !_stop.IsCancellationRequested && _model?.Owner?.BindingId == id;
    }
    private async Task AssignController(KeyboardDeviceRef source) {
        var shell = await _controllers!.ShellDevices!(_ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false);
        var binding = KeyboardControllerBinding.Resolve(source, shell, _captureDevices)
            ?? throw new UnauthorizedAccessException("Full controller identity could not be bound uniquely across processes.");
        if (!await _controllers.ShellOwnerChanged!(binding, _ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Controller interaction context expired.");
        Trace($"owner shell={binding.ShellDevice.DeviceId}/{binding.ShellDevice.Generation} capture={binding.CaptureDevice.DeviceId}/{binding.CaptureDevice.Generation} family={binding.ShellDevice.Family}");
        await Send(new KeyboardSetOwner(binding), _model!.OwnershipEpoch + 1).ConfigureAwait(false);
    }
    private async Task AssignOwnerUi(KeyboardOwnerBinding binding) {
        try {
            await _controllers!.HostOwnerChanged!(binding, () => BindingCurrent(binding.BindingId), _ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false);
            Post(new(Kind: "owner-applied", Value: (binding.BindingId, Interlocked.Read(ref _captureFrame))));
        } catch (Exception) { if (BindingCurrent(binding.BindingId)) Post(new(Kind: "controller-failed")); }
    }
    private async Task<bool> ControllerIncoming(KeyboardEnvelope frame, KeyboardSessionDecision decision) {
        switch (frame.Message) {
            case KeyboardSetOwner owner:
                _ownerUiApplied = Guid.Empty; _ = AssignOwnerUi(owner.Binding); return true;
            case KeyboardSuspend:
                _ownerUiApplied = Guid.Empty;
                if (ControllerOwner is { } suspended) _ = AssignOwnerUi(suspended);
                return true;
            case KeyboardOwnerReady: return true;
            case KeyboardDevicesChanged inventory:
                _captureDevices = inventory.Devices; return true;
            case KeyboardCaptureBatch batch:
                if (_role != KeyboardPeer.Shell) throw new KeyboardProtocolException("Capture peer mismatch.");
                if (batch.Actions.Length > 0)
                    Trace($"capture foreground={batch.IsForeground} proposed={decision.Proposals.Length} actions=[{string.Join(",", batch.Actions.Select(a => $"{a.Sequence}:{a.Device.DeviceId}/{a.Device.Generation}:{a.Action}:repeat={a.IsRepeat}"))}]");
                if (_model!.Phase == KeyboardSessionPhase.Active && _model.Owner is { } active && batch.Actions.Length > 0) {
                    var devices = await _controllers!.ShellDevices!(_ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false);
                    if (!devices.Contains(active.ShellDevice)) {
                        await Send(new KeyboardSuspend(KeyboardSuspendReason.OwnerDisconnected), _model.OwnershipEpoch + 1).ConfigureAwait(false);
                        return true;
                    }
                }
                if (_model!.Phase == KeyboardSessionPhase.Suspended || (_model.Owner == null && _request?.ShellOwner == null)) {
                    var claim = batch.Actions.FirstOrDefault(a => a.Action == ControllerAction.Accept && !a.IsRepeat && _captureDevices.Contains(a.Device));
                    if (batch.IsForeground && claim != null) await AssignController(claim.Device).ConfigureAwait(false);
                    return true; // Claim presses never also type or activate a key.
                }
                foreach (var proposal in decision.Proposals)
                    await Send(new KeyboardExecute(++_commandId, proposal)).ConfigureAwait(false);
                return true;
            case KeyboardExecute:
                if (decision.Execution is { } ticket && !_executions.Writer.TryWrite(ticket)) throw new IOException("Execution queue overflow.");
                return true;
            case KeyboardExecuted: return true;
            default: return false;
        }
    }
    private async Task<bool> ControllerEvent(Event value) {
        switch (value.Kind) {
            case "owner-applied":
                var applied = ((Guid Id, long Floor))value.Value!;
                if (BindingCurrent(applied.Id)) { _ownerUiApplied = applied.Id; _ownerFrameFloor = applied.Floor; }
                return true;
            case "capture" when !_closing && _opened:
                var (serial, sample) = ((long, KeyboardCaptureSnapshot))value.Value!;
                if (!_captureDevices.SequenceEqual(sample.Devices)) {
                    _captureDevices = sample.Devices;
                    await Send(new KeyboardDevicesChanged(sample.CollectorId, ++_inventorySequence, sample.Devices)).ConfigureAwait(false);
                }
                var owner = ControllerOwner;
                if (_model!.Phase == KeyboardSessionPhase.AwaitingNeutral && owner != null && _ownerUiApplied == owner.BindingId &&
                    serial > _ownerFrameFloor && sample.Foreground && sample.Neutral.Contains(owner.CaptureDevice))
                    await Send(new KeyboardOwnerReady(owner.BindingId, true, true, sample.Actions.LastOrDefault()?.Sequence ?? _lastCaptureSequence)).ConfigureAwait(false);
                if (sample.Actions.Length > 0 || _publishedForeground != sample.Foreground) {
                    _publishedForeground = sample.Foreground;
                    _lastCaptureSequence = Math.Max(_lastCaptureSequence, sample.Actions.LastOrDefault()?.Sequence ?? 0);
                    await Send(new KeyboardCaptureBatch(sample.CollectorId, sample.Foreground, sample.Actions)).ConfigureAwait(false);
                }
                return true;
            case "executed" when !_closing:
                if (_model?.Phase == KeyboardSessionPhase.Active) await Send(new KeyboardExecuted((ulong)value.Value!)).ConfigureAwait(false);
                return true;
            case "controller-failed" when !_closing: throw new IOException("Controller execution or owner UI unavailable.");
            default: return value.Kind == "capture";
        }
    }
    private ulong _lastCaptureSequence;
    private async Task ExecutionLoop() {
        try {
            await foreach (var ticket in _executions.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false)) {
                bool started = false;
                bool accepted = await _controllers!.HostExecute!(ticket.SourceAction, action => {
                    lock (_modelGate) {
                        if (_closing || _ui.IsCancellationRequested || _liveCapture is not { Foreground: true } latest ||
                            !latest.Devices.Contains(ticket.SourceAction.Device)) return false;
                        bool wasActive = _model?.Phase == KeyboardSessionPhase.Active;
                        if (!_model!.TryBeginExecution(ticket)) {
                            if (wasActive && _model.Phase == KeyboardSessionPhase.Suspended) Post(new(Kind: "controller-failed"));
                            return false;
                        }
                        Trace($"execute command={ticket.CommandId} captureSeq={ticket.SourceAction.Sequence} device={ticket.SourceAction.Device.DeviceId}/{ticket.SourceAction.Device.Generation} action={ticket.SourceAction.Action}");
                        action(); started = true; return true;
                    }
                }, _ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false);
                if (accepted && started) Post(new(Kind: "executed", Value: ticket.CommandId));
                else {
                    lock (_modelGate) {
                        // Disconnect/focus revocations are expected drops. Other UI
                        // failures terminate rather than replaying a command.
                        if (_model?.Phase == KeyboardSessionPhase.Active && _model.OwnershipEpoch == ticket.OwnershipEpoch &&
                            _model.Revision == ticket.Revision) Post(new(Kind: "controller-failed"));
                    }
                }
            }
        } catch (OperationCanceledException) { }
        catch (Exception) { Post(new(Kind: "controller-failed")); }
    }
}
