namespace ControllerPlayground.Input;

// Call on one host thread. Notifications execute synchronously on that thread.
public sealed class ControllerService {
    private sealed record KnownConnection(ControllerDevice Device, bool Connected);
    private readonly IControllerInputSource _source;
    private readonly Func<long> _clock;
    private readonly Dictionary<ControllerDeviceId, ControllerDeviceState> _states = new();
    // Tombstones prevent late snapshots/events from reviving retired generations.
    private readonly Dictionary<ControllerDeviceId, KnownConnection> _known = new();
    private ulong _sequence;
    private bool _polling;
    private bool _legacyGuideRequested;
    private ControllerDeviceId? _compatibilityDevice;
    private Action<string>? _diagnosticSink;
    public Action<string>? DiagnosticSink {
        get => _diagnosticSink;
        set { _diagnosticSink = value; if (_source is NativeControllerInputSource native) native.DiagnosticSink = value; }
    }

    public Guid CollectorSessionId { get; } = Guid.NewGuid();
    public bool IsConnected { get; private set; }
    public ControllerFamily Family { get; private set; } = ControllerFamily.Unknown;
    public ControllerDeviceId? CompatibilityDeviceId => _compatibilityDevice;
    // Scalar compatibility fallback has no reliable originating ID. An ownership
    // host may use it only to open an unowned root surface, never bypass a scope.
    public bool UnattributedGuideRequested => _legacyGuideRequested;
    public IReadOnlyCollection<ControllerDevice> Devices => _states.Values.Select(s => s.Device).ToArray();
    public IReadOnlyCollection<ControllerDevice> AwaitingNeutralDevices => _states.Values.Where(s => s.IsAwaitingNeutral).Select(s => s.Device).ToArray();
    public event Action<ControllerFamily>? ControllerFamilyChanged;
    public event Action<bool>? ConnectionChanged;
    public event Action<ControllerConnectionEvent>? DeviceConnectionChanged;
    public event Action<ControllerActionEvent>? ActionReceived;

    public ControllerService() : this(new NativeControllerInputSource()) { }
    public ControllerService(IControllerInputSource source, Func<long>? clock = null) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? (() => Environment.TickCount64);
    }

    private static ControllerDevice Describe(ControllerDeviceSnapshotV2 snapshot) => new(
        snapshot.DeviceId, snapshot.DeviceRootId, snapshot.ConnectionGeneration,
        snapshot.VendorId, snapshot.ProductId, snapshot.VendorId switch {
            0x045E => ControllerFamily.Xbox,
            0x054C => ControllerFamily.PlayStation,
            _ => ControllerFamily.Unknown
        });

    private void ApplyConnection(ControllerDeviceSnapshotV2 snapshot, bool connected,
        ulong? nativeSequence, List<ControllerConnectionEvent> changes) {
        var device = Describe(snapshot);
        if (connected && device.ConnectionGeneration == 0)
            throw new InvalidOperationException("Connected controller has no connection generation.");
        _known.TryGetValue(device.Id, out var previous);
        if (previous != null) {
            if (device.ConnectionGeneration < previous.Device.ConnectionGeneration) return;
            if (device.ConnectionGeneration == previous.Device.ConnectionGeneration) {
                // A disconnect can arrive during native poll -> drain. That
                // frame's older connected snapshot must not resurrect it.
                if (!previous.Connected && connected) return;
                if (previous.Connected == connected) {
                    _known[device.Id] = new(device, connected);
                    if (_states.TryGetValue(device.Id, out var existing)) existing.Device = device;
                    return;
                }
            }
            if (previous.Connected) {
                _states.Remove(device.Id);
                changes.Add(new(previous.Device, false, snapshot.Timestamp, nativeSequence));
            }
        }
        _known[device.Id] = new(device, connected);
        if (connected) {
            _states[device.Id] = new(device, previous != null || device.ConnectionGeneration > 1);
            changes.Add(new(device, true, snapshot.Timestamp, nativeSequence));
        }
    }

    // Subscribe to ActionReceived OR route the returned batch, not both copies.
    // No actions carry over to a later poll.
    public IReadOnlyList<ControllerActionEvent> PollActions() {
        if (_polling) throw new InvalidOperationException("Controller polling cannot be reentered.");
        _polling = true;
        try {
            var frame = _source.ReadFrame();
            _legacyGuideRequested = frame.LegacyGuideRequested;
            long now = _clock();
            List<ControllerConnectionEvent> changes = new();
            foreach (var input in frame.ConnectionEvents)
                ApplyConnection(input.Device, input.Kind == ControllerCaptureEventKindV2.Connected, input.Sequence, changes);
            foreach (var sample in frame.Devices)
                ApplyConnection(sample, sample.Connected != 0, null, changes);
            if (frame.ContinuityLost)
                foreach (var state in _states.Values) state.RequireNeutral();
            if (!_compatibilityDevice.HasValue || !_states.ContainsKey(_compatibilityDevice.Value))
                _compatibilityDevice = _states.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal)
                    .Select(id => (ControllerDeviceId?)id).FirstOrDefault();
            var family = _compatibilityDevice.HasValue ? _states[_compatibilityDevice.Value].Device.Family : ControllerFamily.Unknown;
            List<ControllerActionEvent> actions = new();
            foreach (var sample in frame.Devices) {
                if (!_states.TryGetValue(sample.DeviceId, out var state) || state.Device.ConnectionGeneration != sample.ConnectionGeneration || sample.Connected == 0) continue;
                ControllerState? projection = frame.NavigationProjections != null && frame.NavigationProjections.TryGetValue(sample.DeviceId, out var projected) ? projected : null;
                DiagnosticSink?.Invoke($"SAMPLE id={sample.DeviceId} gen={sample.ConnectionGeneration} nativeTime={sample.Timestamp} observedMs={now} available={sample.ReadingAvailable} buttons=0x{sample.State.Buttons:X8} pressed=0x{sample.PressedButtons:X8} released=0x{sample.ReleasedButtons:X8} guide={sample.GuidePressed} stick=({sample.State.LeftThumbstickX:R},{sample.State.LeftThumbstickY:R}) projected=({projection?.LeftThumbstickX:R},{projection?.LeftThumbstickY:R}) neutralWait={state.IsAwaitingNeutral} historyReset={sample.HistoryDiscontinuity}");
                var translated = state.Process(sample, now, projection);
                DiagnosticSink?.Invoke($"TRANSLATE id={sample.DeviceId} gen={sample.ConnectionGeneration} nativeTime={sample.Timestamp} observedMs={now} actions=[{string.Join(",", translated.Select(a => $"{a.Action}:repeat={a.IsRepeat}"))}] {state.DiagnosticState}");
                foreach (var action in translated)
                    actions.Add(new(CollectorSessionId, ++_sequence, state.Device, action.Action,
                        sample.Timestamp, now, action.IsRepeat));
            }
            bool connected = _states.Count != 0;
            if (IsConnected != connected) { IsConnected = connected; ConnectionChanged?.Invoke(connected); }
            if (Family != family) { Family = family; ControllerFamilyChanged?.Invoke(family); }
            foreach (var change in changes) DeviceConnectionChanged?.Invoke(change);
            foreach (var action in actions) ActionReceived?.Invoke(action);
            return actions.ToArray();
        } finally { _polling = false; }
    }

    // Compatibility only, NOT modal ownership: one stable endpoint until it
    // disconnects, with Guide accepted from any device as in the native ABI.
    // The complete batch is still published through ActionReceived.
    public ControllerAction PollAction() {
        var actions = PollActions();
        // Preserve the native legacy Raw Input fallback without inventing an ID
        // for a pulse whose endpoint could not be correlated.
        if (_legacyGuideRequested) return ControllerAction.Guide;
        var guide = actions.FirstOrDefault(a => a.Action == ControllerAction.Guide);
        if (guide != null) return guide.Action;
        return actions.FirstOrDefault(a => a.Device.Id == _compatibilityDevice)?.Action ?? ControllerAction.none;
    }

    // Existing keyboard focus handoff: suppress held controls until neutral,
    // without consuming the legacy native cursor or changing host IPC.
    public void ResyncInputState() {
        foreach (var state in _states.Values) state.RequireNeutral();
    }

    // Guide is a native edge pulse, not a held-repeat action. Keep that explicit
    // escape available after minimizing to a game, where ordinary neutral samples
    // may be unavailable. Existing reconnect/strict gates remain strict.
    public void SuppressForOwnershipTransition() {
        foreach (var state in _states.Values) state.RequireNeutral(allowFreshGuide: true);
    }
}
