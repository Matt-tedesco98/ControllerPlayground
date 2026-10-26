namespace ControllerPlayground.Input;

public enum ControllerScopeKind {
    Guide, Friends, Chat, HomeContextMenu, LibraryContextMenu,
    Support, KnownIssues, Authentication, KeyboardHandoff
}

public sealed class ControllerInputScope {
    public Guid Id { get; } = Guid.NewGuid();
    public ControllerScopeKind Kind { get; }
    public ControllerDevice? Owner { get; internal set; }
    public object? FocusReturnToken { get; }
    public bool BlocksControllerInput => Kind is ControllerScopeKind.Authentication or ControllerScopeKind.KeyboardHandoff;
    internal ControllerInputScope(ControllerScopeKind kind, ControllerDevice? owner, object? focusReturnToken) {
        Kind = kind; Owner = owner; FocusReturnToken = focusReturnToken;
    }
}

public enum ControllerRouteDecision { Accepted, Discarded, Claimed }

// UI-independent, thread-affine permission check. It never queues actions and
// must run BEFORE any shell/global shortcut dispatch. UI focus tokens are opaque.
public sealed class ControllerInputRouter {
    private readonly Guid _collectorSession;
    private readonly Action _suppressHeldControls;
    private readonly List<ControllerInputScope> _scopes = new();
    private Dictionary<ControllerDeviceId, ControllerDevice> _devices = new();
    private ulong _batchMaximum;
    private ulong _blockedThrough;
    private ulong _lastObserved;

    public long Revision { get; private set; }
    public ControllerInputScope? Current => _scopes.LastOrDefault();
    public IReadOnlyList<ControllerInputScope> Scopes => _scopes.ToArray();
    public bool CurrentOwnerAvailable => Current?.Owner is { } owner && IsConnected(owner);
    public bool NeedsRecovery => Current is { BlocksControllerInput: false } scope &&
        (scope.Owner == null || !IsConnected(scope.Owner));
    public event Action? Changed;

    public ControllerInputRouter(Guid collectorSession, Action suppressHeldControls) {
        _collectorSession = collectorSession;
        _suppressHeldControls = suppressHeldControls ?? throw new ArgumentNullException(nameof(suppressHeldControls));
    }
    private bool IsConnected(ControllerDevice device) => _devices.TryGetValue(device.Id, out var actual) &&
        actual.ConnectionGeneration == device.ConnectionGeneration;
    private static bool SameConnection(ControllerDevice? a, ControllerDevice? b) =>
        a != null && b != null && a.Id == b.Id && a.ConnectionGeneration == b.ConnectionGeneration;

    public void BeginBatch(IReadOnlyList<ControllerActionEvent> actions) {
        foreach (var input in actions)
            if (input.CollectorSessionId == _collectorSession) _batchMaximum = Math.Max(_batchMaximum, input.Sequence);
    }

    public void UpdateDevices(IReadOnlyCollection<ControllerDevice> devices) {
        bool wasAvailable = CurrentOwnerAvailable;
        _devices = devices.ToDictionary(d => d.Id);
        if (Current != null && wasAvailable != CurrentOwnerAvailable) Transition();
    }

    private void Transition() {
        ++Revision;
        _blockedThrough = Math.Max(_blockedThrough, _batchMaximum);
        _suppressHeldControls();
        Changed?.Invoke();
    }

    public ControllerInputScope Push(ControllerScopeKind kind, ControllerDevice? owner = null, object? focusReturnToken = null) {
        var parent = Current;
        if (owner != null && !IsConnected(owner)) throw new InvalidOperationException("A stale connection cannot own a scope.");
        if (parent?.Owner != null && owner != null && !SameConnection(parent.Owner, owner))
            throw new InvalidOperationException("Nested interactions must keep the current controller owner.");
        var scope = new ControllerInputScope(kind, owner ?? parent?.Owner, focusReturnToken);
        _scopes.Add(scope);
        Transition();
        return scope;
    }

    public ControllerInputScope? Find(ControllerScopeKind kind) => _scopes.LastOrDefault(s => s.Kind == kind);

    // Called only after the mediated keyboard transport validates a full binding.
    // It cannot transfer an unrelated/newer interaction or revive an old generation.
    public bool TransferKeyboardOwner(Guid handoffId, ControllerDevice device) {
        if (Current is not { Kind: ControllerScopeKind.KeyboardHandoff } scope || scope.Id != handoffId || !IsConnected(device)) return false;
        var previous = scope.Owner;
        foreach (var item in _scopes)
            if (item.Owner == null || SameConnection(item.Owner, previous)) item.Owner = device;
        Transition(); return true;
    }

    // Closing a parent normally unwinds descendants. Asynchronous completion can
    // remove just its scope while preserving a newer blocking surface above it.
    public IReadOnlyList<ControllerInputScope> Remove(Guid id, bool descendants = true) {
        int index = _scopes.FindIndex(s => s.Id == id);
        if (index < 0) return Array.Empty<ControllerInputScope>();
        int count = descendants ? _scopes.Count - index : 1;
        var removed = _scopes.GetRange(index, count);
        _scopes.RemoveRange(index, count);
        removed.Reverse();
        Transition();
        return removed;
    }

    public IReadOnlyList<ControllerInputScope> Clear() {
        var removed = _scopes.AsEnumerable().Reverse().ToArray();
        _scopes.Clear();
        // Also invalidate queued focus work and same-batch input on page changes.
        Transition();
        return removed;
    }

    public ControllerRouteDecision Route(ControllerActionEvent input) {
        if (input.CollectorSessionId != _collectorSession || input.Sequence <= _lastObserved)
            return ControllerRouteDecision.Discarded;
        _lastObserved = input.Sequence;
        if (input.Sequence <= _blockedThrough || !IsConnected(input.Device)) return ControllerRouteDecision.Discarded;
        var scope = Current;
        if (scope == null) return ControllerRouteDecision.Accepted;
        if (scope.BlocksControllerInput) return ControllerRouteDecision.Discarded;
        if (NeedsRecovery) {
            // Managed input supplies fresh actions only after its neutral gate.
            // Accept claims/resumes, but NEVER also activates a UI control.
            if (input.Action != ControllerAction.Accept || input.IsRepeat) return ControllerRouteDecision.Discarded;
            var previous = scope.Owner;
            foreach (var item in _scopes)
                if (item.Owner == null || SameConnection(item.Owner, previous)) item.Owner = input.Device;
            Transition();
            return ControllerRouteDecision.Claimed;
        }
        return SameConnection(scope.Owner, input.Device) ? ControllerRouteDecision.Accepted : ControllerRouteDecision.Discarded;
    }
}
