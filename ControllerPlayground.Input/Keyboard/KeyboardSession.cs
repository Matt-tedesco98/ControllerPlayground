using System.Collections.Immutable;

namespace ControllerPlayground.Input.Keyboard;

public sealed record KeyboardConnectionIdentity(Guid ConnectionId, Guid ShellInstanceId, Guid HostInstanceId) {
    public static KeyboardConnectionIdentity Negotiate(KeyboardEnvelope hello, KeyboardEnvelope acknowledgement) {
        KeyboardProtocolValidation.Validate(hello); KeyboardProtocolValidation.Validate(acknowledgement);
        if (hello.Message is not KeyboardHello || acknowledgement.Message is not KeyboardHelloAck ||
            hello.Header.ConnectionId != acknowledgement.Header.ConnectionId || hello.Header.ShellInstanceId != acknowledgement.Header.ShellInstanceId)
            throw new KeyboardProtocolException("Handshake correlation mismatch.");
        return new(hello.Header.ConnectionId, hello.Header.ShellInstanceId, acknowledgement.Header.HostInstanceId);
    }
}
public enum KeyboardSessionPhase { Opening, AwaitingReady, AwaitingNeutral, Active, Suspended, Completing, Closing, Closed, Faulted }
public enum KeyboardDisposition { Accepted, Discarded, Suspended, ProtocolFault }
public sealed record KeyboardSessionResult(bool Committed, string? Text);
public sealed record KeyboardSessionDecision(KeyboardDisposition Disposition, string Reason,
    ImmutableArray<KeyboardCapturedAction> Proposals, KeyboardExecutionTicket? Execution = null);
public sealed record KeyboardExecutionTicket {
    internal Guid Issuer { get; }
    public Guid SessionId { get; }
    public ulong OwnershipEpoch { get; }
    public long Revision { get; }
    public ulong CommandId { get; }
    public KeyboardCapturedAction SourceAction { get; }
    internal KeyboardExecutionTicket(Guid issuer, Guid session, ulong epoch, long revision, ulong command, KeyboardCapturedAction source) {
        Issuer = issuer; SessionId = session; OwnershipEpoch = epoch; Revision = revision; CommandId = command; SourceAction = source;
    }
}

// Thread-affine pure model, instantiated separately by each peer. Apply each
// validated outgoing AND incoming session message in local transport order.
// It opens no pipe, invokes no UI, changes no focus and schedules no timers.
public sealed class KeyboardSession {
    private sealed record Proposal(KeyboardCapturedAction Input, long ReceivedAt);
    private sealed class Pending(KeyboardExecutionTicket ticket, long receivedAt) {
        public KeyboardExecutionTicket Ticket { get; } = ticket;
        public long ReceivedAt { get; } = receivedAt;
        public bool Started { get; set; }
    }
    private readonly KeyboardConnectionIdentity _connection;
    private readonly Guid _issuer = Guid.NewGuid();
    private readonly KeyboardPeer _localPeer;
    private readonly TimeProvider _time;
    private readonly TimeSpan _maximumQueueAge;
    private readonly ulong[] _received = new ulong[3];
    private readonly Dictionary<ulong, Proposal> _proposals = new();
    private readonly Dictionary<ulong, Pending> _commands = new();
    private ImmutableHashSet<KeyboardDeviceRef> _devices = ImmutableHashSet<KeyboardDeviceRef>.Empty;
    private Guid _captureCollector;
    private ulong _captureSequence, _snapshotSequence, _commandSequence, _lastStartedCommand;
    public Guid SessionId { get; }
    public KeyboardSessionPhase Phase { get; private set; } = KeyboardSessionPhase.Opening;
    public ulong OwnershipEpoch { get; private set; }
    public long Revision { get; private set; }
    public KeyboardOwnerBinding? Owner { get; private set; }
    public KeyboardDeviceRef? ShellOwner { get; private set; }
    public string InitialText { get; private set; } = "";
    public string LatestDraft { get; private set; } = "";
    public ulong DraftRevision { get; private set; }
    public KeyboardSessionResult? Result { get; private set; }
    public int PendingWork => _proposals.Count + _commands.Count;

    public KeyboardSession(KeyboardConnectionIdentity connection, Guid session, KeyboardPeer localPeer,
        TimeProvider? timeProvider = null, TimeSpan? maximumQueueAge = null) {
        if (connection == null || connection.ConnectionId == Guid.Empty || connection.ShellInstanceId == Guid.Empty ||
            connection.HostInstanceId == Guid.Empty || session == Guid.Empty || !Enum.IsDefined(localPeer))
            throw new ArgumentException("A negotiated connection, unique session and local peer are required.");
        _connection = connection; SessionId = session; _localPeer = localPeer; _time = timeProvider ?? TimeProvider.System;
        _maximumQueueAge = maximumQueueAge ?? TimeSpan.FromMilliseconds(250);
        if (_maximumQueueAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumQueueAge));
    }
    private static KeyboardSessionDecision Decision(KeyboardDisposition disposition, string reason,
        ImmutableArray<KeyboardCapturedAction> proposals = default, KeyboardExecutionTicket? ticket = null) =>
        new(disposition, reason, proposals.IsDefault ? ImmutableArray<KeyboardCapturedAction>.Empty : proposals, ticket);
    private static KeyboardSessionDecision Accept(string reason = "Accepted") => Decision(KeyboardDisposition.Accepted, reason);
    private static KeyboardSessionDecision Discard(string reason) => Decision(KeyboardDisposition.Discarded, reason);
    private void Transition(KeyboardSessionPhase phase) {
        Phase = phase; ++Revision; _proposals.Clear(); _commands.Clear();
        _lastStartedCommand = _commandSequence; // Revoked commands are never replayed in a later epoch.
    }
    private KeyboardSessionDecision Suspend(string reason) {
        Transition(KeyboardSessionPhase.Suspended); return Decision(KeyboardDisposition.Suspended, reason);
    }
    public void SuspendLocally(KeyboardSuspendReason reason) {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (Phase is KeyboardSessionPhase.Active or KeyboardSessionPhase.AwaitingNeutral) Transition(KeyboardSessionPhase.Suspended);
    }
    public void NotifyTransportLost() {
        if (Phase is KeyboardSessionPhase.Closed or KeyboardSessionPhase.Faulted) return;
        Result ??= new(false, null); Transition(KeyboardSessionPhase.Faulted);
    }
    private bool Terminal => Phase is KeyboardSessionPhase.Closed or KeyboardSessionPhase.Faulted;
    private bool BindingCurrent => Owner != null && _devices.Contains(Owner.CaptureDevice);

    public KeyboardSessionDecision Apply(KeyboardEnvelope envelope) {
        try { KeyboardProtocolValidation.Validate(envelope); }
        catch (KeyboardProtocolException) { NotifyTransportLost(); return Decision(KeyboardDisposition.ProtocolFault, "InvalidEnvelope"); }
        var h = envelope.Header;
        if (h.ConnectionId != _connection.ConnectionId || h.ShellInstanceId != _connection.ShellInstanceId ||
            h.HostInstanceId != _connection.HostInstanceId || h.SessionId != SessionId) return Discard("OtherLifetime");
        if (Terminal) return Discard("TerminalSession");
        int peer = (int)h.Sender;
        if (h.Sequence <= _received[peer]) return Discard("DuplicateMessage");
        bool gap = h.Sequence != _received[peer] + 1;
        _received[peer] = h.Sequence;
        if (gap) return Suspend("MessageSequenceGap");
        if (envelope.Message is KeyboardOpen open) {
            if (Phase != KeyboardSessionPhase.Opening || h.OwnershipEpoch != 1) return Discard("AlreadyOpenedOrInvalidInitialEpoch");
            OwnershipEpoch = 1; InitialText = LatestDraft = open.InitialText; ShellOwner = open.ShellOwner;
            Transition(KeyboardSessionPhase.AwaitingReady); return Accept();
        }
        if (Phase == KeyboardSessionPhase.Opening) return Discard("NotOpened");
        if (Phase == KeyboardSessionPhase.Closing && envelope.Message is KeyboardClose) return Discard("AlreadyClosing");
        bool epochControl = envelope.Message is KeyboardSetOwner or KeyboardSuspend or KeyboardClose;
        if (epochControl) {
            if (OwnershipEpoch == ulong.MaxValue || h.OwnershipEpoch != OwnershipEpoch + 1) return Discard("InvalidEpochAdvance");
        } else if (h.OwnershipEpoch != OwnershipEpoch) {
            return h.OwnershipEpoch < OwnershipEpoch ? Discard("StaleEpoch") : Suspend("UnannouncedEpoch");
        }
        switch (envelope.Message) {
            case KeyboardClose:
                OwnershipEpoch = h.OwnershipEpoch; Transition(KeyboardSessionPhase.Closing); return Accept();
            case KeyboardClosed:
                if (Phase != KeyboardSessionPhase.Closing) return Discard("NotClosing");
                Result ??= new(false, null); Transition(KeyboardSessionPhase.Closed); return Accept();
            case KeyboardError:
                Result ??= new(false, null); Transition(KeyboardSessionPhase.Faulted); return Accept();
            case KeyboardPing or KeyboardPong: return Accept();
        }
        if (Phase is KeyboardSessionPhase.Completing or KeyboardSessionPhase.Closing) return Discard("CompletionSealed");
        switch (envelope.Message) {
            case KeyboardReady ready:
                if (Phase != KeyboardSessionPhase.AwaitingReady) return Discard("UnexpectedReady");
                _captureCollector = ready.CaptureCollectorId; _devices = ready.Devices.ToImmutableHashSet();
                Transition(KeyboardSessionPhase.AwaitingNeutral); return Accept();
            case KeyboardSetOwner assign:
                if (_captureCollector == Guid.Empty || assign.Binding.CaptureDevice.CollectorId != _captureCollector ||
                    !_devices.Contains(assign.Binding.CaptureDevice) || Owner?.BindingId == assign.Binding.BindingId)
                    return Discard("UnboundOrRetiredOwner");
                OwnershipEpoch = h.OwnershipEpoch; Owner = assign.Binding; ShellOwner = Owner.ShellDevice;
                Transition(KeyboardSessionPhase.AwaitingNeutral); return Accept();
            case KeyboardSuspend:
                OwnershipEpoch = h.OwnershipEpoch; return Suspend("PeerSuspended");
            case KeyboardOwnerReady armed:
                if (Phase != KeyboardSessionPhase.AwaitingNeutral || !BindingCurrent || armed.BindingId != Owner!.BindingId ||
                    !armed.IsNeutral || !armed.IsForeground || armed.CaptureSequenceFloor < _captureSequence)
                    return Discard("NotNeutralOrStaleBinding");
                _captureSequence = armed.CaptureSequenceFloor;
                Transition(KeyboardSessionPhase.Active); return Accept();
            case KeyboardDevicesChanged inventory:
                if (inventory.CaptureCollectorId != _captureCollector) return Suspend("OtherCaptureCollector");
                if (inventory.SnapshotSequence <= _snapshotSequence) return Discard("StaleInventory");
                _snapshotSequence = inventory.SnapshotSequence; _devices = inventory.Devices.ToImmutableHashSet();
                return Owner != null && !BindingCurrent ? Suspend("OwnerDisconnected") : Accept();
            case KeyboardCaptureBatch batch: return Capture(batch);
            case KeyboardExecute execute: return Issue(execute);
            case KeyboardExecuted executed:
                if (!_commands.TryGetValue(executed.CommandId, out var pending) || (_localPeer == KeyboardPeer.Host && !pending.Started))
                    return Discard("UnknownOrUnexecutedCommand");
                _commands.Remove(executed.CommandId); return Accept();
            case KeyboardDraftSnapshot draft:
                if (_captureCollector == Guid.Empty || draft.DraftRevision <= DraftRevision) return Discard("StaleDraft");
                DraftRevision = draft.DraftRevision; LatestDraft = draft.Text; return Accept();
            case KeyboardCommit commit:
                if (_captureCollector == Guid.Empty) return Discard("NotReady");
                Result = new(true, commit.Text); Transition(KeyboardSessionPhase.Completing); return Accept();
            case KeyboardCancel:
                if (_captureCollector == Guid.Empty) return Discard("NotReady");
                Result = new(false, null); Transition(KeyboardSessionPhase.Completing); return Accept();
            default: return Discard("UnexpectedMessage");
        }
    }
    private KeyboardSessionDecision Capture(KeyboardCaptureBatch batch) {
        if (batch.CaptureCollectorId != _captureCollector) return Suspend("OtherCaptureCollector");
        if (!batch.IsForeground) return Suspend("CaptureFocusLost");
        var accepted = ImmutableArray.CreateBuilder<KeyboardCapturedAction>();
        foreach (var input in batch.Actions) {
            if (input.Sequence <= _captureSequence) continue;
            _captureSequence = input.Sequence;
            if (Phase != KeyboardSessionPhase.Active || !BindingCurrent || input.Device != Owner!.CaptureDevice ||
                !KeyboardProtocol.IsKeyboardAction(input.Action)) continue;
            if (PendingWork >= KeyboardProtocol.MaxPendingCommands) return Suspend("PendingWorkOverflow");
            _proposals.Add(input.Sequence, new(input, _time.GetTimestamp())); accepted.Add(input);
        }
        return Decision(KeyboardDisposition.Accepted, "CaptureFiltered", accepted.ToImmutable());
    }
    private KeyboardSessionDecision Issue(KeyboardExecute execute) {
        if (Phase != KeyboardSessionPhase.Active || !BindingCurrent) return Discard("InactiveOwner");
        if (execute.CommandId <= _commandSequence) return Discard("DuplicateCommand");
        bool gap = _commandSequence == ulong.MaxValue || execute.CommandId != _commandSequence + 1;
        _commandSequence = execute.CommandId;
        if (gap) return Suspend("CommandSequenceGap");
        if (!_proposals.TryGetValue(execute.SourceAction.Sequence, out var proposal) || proposal.Input != execute.SourceAction ||
            _proposals.Keys.Min() != execute.SourceAction.Sequence)
            return Suspend("UnauthorizedSourceAction");
        if (_time.GetElapsedTime(proposal.ReceivedAt) > _maximumQueueAge) return Suspend("ExpiredProposal");
        _proposals.Remove(execute.SourceAction.Sequence);
        var ticket = new KeyboardExecutionTicket(_issuer, SessionId, OwnershipEpoch, Revision, execute.CommandId, execute.SourceAction);
        _commands.Add(execute.CommandId, new(ticket, proposal.ReceivedAt));
        return Decision(KeyboardDisposition.Accepted, "Authorized", ticket: ticket);
    }
    // Host calls this immediately before invoking existing UI behavior, not when
    // first receiving Execute. The shell replica never executes tickets.
    public bool TryBeginExecution(KeyboardExecutionTicket ticket) {
        if (ticket == null || ticket.Issuer != _issuer || _localPeer != KeyboardPeer.Host || Phase != KeyboardSessionPhase.Active || !BindingCurrent ||
            ticket.SessionId != SessionId || ticket.OwnershipEpoch != OwnershipEpoch || ticket.Revision != Revision ||
            !_commands.TryGetValue(ticket.CommandId, out var pending) || pending.Ticket != ticket || pending.Started) return false;
        if (_lastStartedCommand == ulong.MaxValue || ticket.CommandId != _lastStartedCommand + 1) {
            Suspend("ExecutionOrderGap"); return false;
        }
        if (_time.GetElapsedTime(pending.ReceivedAt) > _maximumQueueAge) { Suspend("ExpiredExecution"); return false; }
        _lastStartedCommand = ticket.CommandId; pending.Started = true; return true;
    }
}
