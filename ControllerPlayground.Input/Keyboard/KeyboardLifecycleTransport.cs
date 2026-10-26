using System.Diagnostics;
using System.Threading.Channels;

namespace ControllerPlayground.Input.Keyboard;

public enum KeyboardLifecycleStatus { Committed, Cancelled, Failed }
public sealed record KeyboardLifecycleResult(KeyboardLifecycleStatus Status, string? Text = null, string? Failure = null, uint HelperProcessId = 0);
public sealed record KeyboardTransportTimeouts(TimeSpan Startup, TimeSpan Heartbeat, TimeSpan Idle, TimeSpan Close) {
    public static KeyboardTransportTimeouts Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
}

// Phase 2: lifecycle only. It never forwards or executes controller actions.
// A bounded actor queue owns all model changes; IO and UI completion producers
// cannot race sequence/epoch assignment or terminal-result selection.
public sealed class KeyboardLifecycleTransport {
    private sealed record Event(KeyboardEnvelope? Frame = null, string? Kind = null, object? Value = null);
    private readonly KeyboardPeer _role;
    private readonly KeyboardDuplexConnection _pipe;
    private readonly Channel<Event> _events = Channel.CreateBounded<Event>(64);
    private readonly CancellationTokenSource _stop = new(), _ui = new();
    private readonly KeyboardTransportTimeouts _timeouts;
    private readonly Action<string>? _trace;
    private readonly Guid _instance;
    private readonly Func<Guid, KeyboardOpen, CancellationToken, Task<KeyboardReady>>? _open;
    private readonly Func<Guid, CancellationToken, Task>? _close;
    private readonly Func<KeyboardReady, CancellationToken, Task<bool>>? _ready;
    private readonly Func<KeyboardReady, bool>? _validateWindow;
    private readonly KeyboardOpen? _request;
    private readonly KeyboardCapabilities _required;
    private KeyboardEnvelope? _hello;
    private KeyboardConnectionIdentity? _identity;
    private KeyboardSession? _model;
    private Guid _session;
    private ulong _sequence, _ping;
    private long _lastActivity = Stopwatch.GetTimestamp(), _stageStarted = Stopwatch.GetTimestamp(), _lastPing;
    private bool _opened, _closing, _finished;
    private KeyboardLifecycleResult? _terminal, _result;
    public Guid SessionId => _session;

    private KeyboardLifecycleTransport(KeyboardPeer role, KeyboardDuplexConnection pipe, Guid instance,
        KeyboardOpen? request, Func<Guid, KeyboardOpen, CancellationToken, Task<KeyboardReady>>? open,
        Func<Guid, CancellationToken, Task>? close, Func<KeyboardReady, CancellationToken, Task<bool>>? ready,
        Func<KeyboardReady, bool>? validateWindow, KeyboardCapabilities required,
        KeyboardTransportTimeouts? timeouts, Action<string>? trace) {
        _role = role; _pipe = pipe; _instance = instance; _request = request; _open = open; _close = close;
        _ready = ready; _validateWindow = validateWindow; _required = required;
        _timeouts = timeouts ?? KeyboardTransportTimeouts.Default; _trace = trace;
        if (instance == Guid.Empty || _timeouts.Startup <= TimeSpan.Zero || _timeouts.Heartbeat <= TimeSpan.Zero ||
            _timeouts.Idle <= _timeouts.Heartbeat || _timeouts.Close <= TimeSpan.Zero) throw new ArgumentException("Invalid lifecycle configuration.");
    }
    public static KeyboardLifecycleTransport Client(KeyboardDuplexConnection pipe, Guid shellInstance, KeyboardOpen request,
        Func<KeyboardReady, bool> validateWindow, Func<KeyboardReady, CancellationToken, Task<bool>> ready,
        KeyboardCapabilities required = KeyboardProtocol.LifecycleCapabilities, KeyboardTransportTimeouts? timeouts = null, Action<string>? trace = null) =>
        new(KeyboardPeer.Shell, pipe, shellInstance, request, null, null, ready, validateWindow, required, timeouts, trace);
    public static KeyboardLifecycleTransport Host(KeyboardDuplexConnection pipe, Guid hostInstance,
        Func<Guid, KeyboardOpen, CancellationToken, Task<KeyboardReady>> open, Func<Guid, CancellationToken, Task> close,
        KeyboardTransportTimeouts? timeouts = null, Action<string>? trace = null) =>
        new(KeyboardPeer.Host, pipe, hostInstance, null, open, close, null, null, KeyboardProtocol.LifecycleCapabilities, timeouts, trace);
    private void Trace(string message) => _trace?.Invoke($"V2 role={_role} session={_session} {message}");
    private void Post(Event value) {
        if (!_events.Writer.TryWrite(value)) { _ui.Cancel(); _stop.Cancel(); }
    }
    public void Commit(Guid session, string text) => Post(new(Kind: "commit", Value: (session, text)));
    public void Cancel(Guid session) => Post(new(Kind: "cancel", Value: session));
    private async Task Pump() {
        try {
            await foreach (var frame in _pipe.Incoming.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                await _events.Writer.WriteAsync(new(Frame: frame), _stop.Token).ConfigureAwait(false);
        } catch (Exception error) { Trace("reader=" + error.GetType().Name); }
        finally { _ui.Cancel(); Post(new(Kind: "eof")); }
    }
    private async Task Ticks() {
        try {
            while (!_stop.IsCancellationRequested) {
                await Task.Delay(_timeouts.Heartbeat, _stop.Token).ConfigureAwait(false); Post(new(Kind: "tick"));
            }
        } catch (OperationCanceledException) { }
    }
    private async Task Send(KeyboardMessage message, ulong? epoch = null) {
        if (_identity == null || _model == null) throw new KeyboardProtocolException("Session not established.");
        var envelope = new KeyboardEnvelope(new(2, _identity.ConnectionId, _identity.ShellInstanceId, _identity.HostInstanceId,
            _session, _role, epoch ?? Math.Max(1, _model.OwnershipEpoch), ++_sequence), message);
        if (_model.Apply(envelope).Disposition != KeyboardDisposition.Accepted) throw new KeyboardProtocolException("Invalid outgoing lifecycle transition.");
        Trace("send=" + message.GetType().Name);
        await _pipe.SendAsync(envelope, _stop.Token).ConfigureAwait(false);
    }
    private async Task OpenUi(KeyboardOpen request) {
        try {
            var result = await _open!(_session, request, _ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false);
            Post(new(Kind: "opened", Value: result));
        } catch (Exception error) { Post(new(Kind: "ui-failed", Value: error.GetType().Name)); }
    }
    private async Task ReadyUi(KeyboardReady ready) {
        try { Post(new(Kind: "ready-handled", Value: await _ready!(ready, _ui.Token).WaitAsync(_timeouts.Startup, _ui.Token).ConfigureAwait(false))); }
        catch (Exception) { Post(new(Kind: "ready-handled", Value: false)); }
    }
    private async Task CloseUi() {
        try {
            using var timeout = new CancellationTokenSource(_timeouts.Close);
            await _close!(_session, timeout.Token).WaitAsync(_timeouts.Close, timeout.Token).ConfigureAwait(false);
            Post(new(Kind: "ui-closed"));
        } catch (Exception) { Post(new(Kind: "ui-failed")); }
    }
    private async Task BeginClose(KeyboardCloseReason reason) {
        if (_closing) return;
        _closing = true; _ui.Cancel(); _stageStarted = Stopwatch.GetTimestamp();
        if (_model == null) { _result = new(KeyboardLifecycleStatus.Cancelled); _finished = true; return; }
        await Send(new KeyboardClose(reason), _model.OwnershipEpoch + 1).ConfigureAwait(false);
    }
    private async Task Incoming(KeyboardEnvelope frame) {
        KeyboardProtocolValidation.Validate(frame);
        if (frame.Header.Sender == _role) throw new KeyboardProtocolException("Echoed local sender.");
        if (_identity == null) {
            if (frame.Header.Sequence != 1) throw new KeyboardProtocolException("Invalid handshake sequence.");
            if (_role == KeyboardPeer.Host) {
                if (frame.Message is not KeyboardHello hello || (hello.RequiredCapabilities & KeyboardProtocol.LifecycleCapabilities) != hello.RequiredCapabilities)
                    throw new KeyboardProtocolException("Requested capabilities are unavailable in Phase 2.");
                _hello = frame;
                var ack = new KeyboardEnvelope(frame.Header with { HostInstanceId = _instance, Sender = KeyboardPeer.Host },
                    new KeyboardHelloAck(2, KeyboardProtocol.LifecycleCapabilities));
                _identity = KeyboardConnectionIdentity.Negotiate(frame, ack);
                await _pipe.SendAsync(ack, _stop.Token).ConfigureAwait(false);
            } else {
                _identity = KeyboardConnectionIdentity.Negotiate(_hello!, frame);
                _model = new(_identity, _session, _role);
                await Send(_request!, 1).ConfigureAwait(false);
            }
            _lastActivity = _stageStarted = Stopwatch.GetTimestamp(); Trace("negotiated=lifecycle-only"); return;
        }
        if (_model == null) {
            if (_role != KeyboardPeer.Host || frame.Message is not KeyboardOpen || frame.Header.ConnectionId != _identity.ConnectionId ||
                frame.Header.HostInstanceId != _identity.HostInstanceId || frame.Header.ShellInstanceId != _identity.ShellInstanceId)
                throw new KeyboardProtocolException("Expected correlated Open.");
            _session = frame.Header.SessionId; _model = new(_identity, _session, _role);
        }
        if (frame.Message is not (KeyboardOpen or KeyboardReady or KeyboardCommit or KeyboardCancel or KeyboardClose or KeyboardClosed or KeyboardPing or KeyboardPong or KeyboardError))
            throw new KeyboardProtocolException("Controller execution is not enabled in Phase 2.");
        var decision = _model.Apply(frame);
        Trace($"receive={frame.Message.GetType().Name} decision={decision.Disposition} reason={decision.Reason}");
        if (decision.Disposition == KeyboardDisposition.Discarded) return;
        if (decision.Disposition != KeyboardDisposition.Accepted) throw new KeyboardProtocolException("Invalid incoming lifecycle transition.");
        _lastActivity = Stopwatch.GetTimestamp();
        switch (frame.Message) {
            case KeyboardOpen open:
                _stageStarted = Stopwatch.GetTimestamp(); _ = OpenUi(open); break;
            case KeyboardReady ready:
                if (!_validateWindow!(ready)) throw new UnauthorizedAccessException("Invalid helper window.");
                _opened = true; _ = ReadyUi(ready); break;
            case KeyboardPing ping: await Send(new KeyboardPong(ping.Nonce)).ConfigureAwait(false); break;
            case KeyboardCommit commit:
                _terminal ??= new(KeyboardLifecycleStatus.Committed, commit.Text);
                await BeginClose(KeyboardCloseReason.Finished).ConfigureAwait(false); break;
            case KeyboardCancel:
                _terminal ??= new(KeyboardLifecycleStatus.Cancelled);
                await BeginClose(KeyboardCloseReason.Cancelled).ConfigureAwait(false); break;
            case KeyboardClose:
                _closing = true; _ui.Cancel(); _stageStarted = Stopwatch.GetTimestamp(); _ = CloseUi(); break;
            case KeyboardClosed:
                _result = _terminal ?? new(KeyboardLifecycleStatus.Cancelled); _finished = true; break;
            case KeyboardError: throw new KeyboardProtocolException("Peer lifecycle error.");
        }
    }
    public async Task<KeyboardLifecycleResult> RunAsync(CancellationToken cancellationToken = default) {
        if (_role == KeyboardPeer.Shell) _session = Guid.NewGuid();
        using var registration = cancellationToken.Register(() => Post(new(Kind: "request-cancel")));
        var pump = Pump(); var ticks = Ticks();
        try {
            if (_role == KeyboardPeer.Shell) {
                _hello = new(new(2, Guid.NewGuid(), _instance, Guid.Empty, Guid.Empty, KeyboardPeer.Shell, 0, 1),
                    new KeyboardHello(2, 2, _required));
                await _pipe.SendAsync(_hello, _stop.Token).ConfigureAwait(false);
            }
            while (!_finished) {
                var value = await _events.Reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                if (value.Frame != null) { await Incoming(value.Frame).ConfigureAwait(false); continue; }
                switch (value.Kind) {
                    case "eof": throw new IOException("Peer EOF.");
                    case "request-cancel":
                        if (_role == KeyboardPeer.Shell) { _terminal = new(KeyboardLifecycleStatus.Cancelled); await BeginClose(KeyboardCloseReason.Cancelled).ConfigureAwait(false); }
                        else throw new OperationCanceledException();
                        break;
                    case "opened" when !_closing:
                        await Send((KeyboardReady)value.Value!).ConfigureAwait(false); _opened = true; break;
                    case "ready-handled" when !_closing:
                        if (value.Value is not true) { _terminal = new(KeyboardLifecycleStatus.Failed, Failure: "FocusOrContextRejected"); await BeginClose(KeyboardCloseReason.TargetGone).ConfigureAwait(false); }
                        break;
                    case "commit" when !_closing && _opened:
                        var commit = ((Guid Session, string Text))value.Value!;
                        if (commit.Session == _session && _model?.Phase != KeyboardSessionPhase.Completing) await Send(new KeyboardCommit(commit.Text)).ConfigureAwait(false);
                        break;
                    case "cancel" when !_closing && _opened:
                        if ((Guid)value.Value! == _session && _model?.Phase != KeyboardSessionPhase.Completing) await Send(new KeyboardCancel()).ConfigureAwait(false);
                        break;
                    case "ui-closed" when _closing:
                        await Send(new KeyboardClosed()).ConfigureAwait(false);
                        _result = _model?.Result is { Committed: true } committed ? new(KeyboardLifecycleStatus.Committed, committed.Text) : new(KeyboardLifecycleStatus.Cancelled);
                        _finished = true; break;
                    case "ui-failed" when !_closing: throw new IOException("Helper UI unavailable.");
                    case "tick":
                        if (_closing && Stopwatch.GetElapsedTime(_stageStarted) > _timeouts.Close) throw new TimeoutException("Close deadline.");
                        if (!_opened && !_closing && Stopwatch.GetElapsedTime(_stageStarted) > _timeouts.Startup) throw new TimeoutException("Startup deadline.");
                        if (Stopwatch.GetElapsedTime(_lastActivity) > _timeouts.Idle) throw new TimeoutException("Peer liveness deadline.");
                        if (_model != null && _model.Phase is not (KeyboardSessionPhase.Closed or KeyboardSessionPhase.Faulted) &&
                            Stopwatch.GetElapsedTime(_lastPing) >= _timeouts.Heartbeat) {
                            _lastPing = Stopwatch.GetTimestamp(); await Send(new KeyboardPing(++_ping)).ConfigureAwait(false);
                        }
                        break;
                }
            }
        } catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException or InvalidOperationException) {
            Trace("failure=" + error.GetType().Name);
            _model?.NotifyTransportLost();
            _result = cancellationToken.IsCancellationRequested ? new(KeyboardLifecycleStatus.Cancelled) :
                new(KeyboardLifecycleStatus.Failed, Failure: error.GetType().Name);
        } finally {
            _ui.Cancel(); _stop.Cancel();
            if (_role == KeyboardPeer.Host && _session != Guid.Empty) {
                try { using var cleanup = new CancellationTokenSource(_timeouts.Close); await _close!(_session, cleanup.Token).WaitAsync(_timeouts.Close).ConfigureAwait(false); }
                catch (Exception error) { Trace("cleanup=" + error.GetType().Name); }
            }
            try { await Task.WhenAll(pump, ticks).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        return _result ?? new(KeyboardLifecycleStatus.Failed, Failure: "SessionEnded");
    }
}
