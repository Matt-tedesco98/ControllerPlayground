using ControllerPlayground.Input;

// Controller/router hardware exercise only. WinUI focus must be tested in the shell.
internal sealed class OwnershipDiagnosticSession {
    private readonly ControllerService _service;
    private readonly Action<string> _log;
    private readonly Dictionary<(ControllerDeviceId, ulong), bool> _neutral = new();
    public ControllerInputRouter Router { get; }

    public OwnershipDiagnosticSession(ControllerService service, Action<string> log, Action<string> title) {
        _service = service; _log = log;
        Router = new(service.CollectorSessionId, service.SuppressForOwnershipTransition);
        Router.Changed += () => {
            var scope = Router.Current;
            string status = scope == null ? "Root: either controller" :
                Router.NeedsRecovery ? $"{scope.Kind}: release controls, then A/Cross to claim" :
                $"{scope.Kind}: {scope.Owner?.Family} generation {scope.Owner?.ConnectionGeneration}";
            _log($"SCOPE revision={Router.Revision} kind={scope?.Kind} owner={scope?.Owner?.Id} gen={scope?.Owner?.ConnectionGeneration} recovery={Router.NeedsRecovery}");
            title(status);
        };
    }
    public bool CloseTop() {
        if (Router.Current == null) return false;
        Router.Remove(Router.Current.Id); return true;
    }
    public void Pump() {
        var actions = _service.PollActions();
        var waiting = _service.AwaitingNeutralDevices.Select(d => (d.Id, d.ConnectionGeneration)).ToHashSet();
        foreach (var device in _service.Devices) {
            var key = (device.Id, device.ConnectionGeneration);
            bool value = waiting.Contains(key);
            if (!_neutral.TryGetValue(key, out var previous) || previous != value) {
                _neutral[key] = value;
                _log($"NEUTRAL id={device.Id} gen={device.ConnectionGeneration} waiting={value}");
            }
        }
        Router.BeginBatch(actions); Router.UpdateDevices(_service.Devices);
        foreach (var input in actions) {
            var decision = Router.Route(input);
            _log($"ROUTE seq={input.Sequence} id={input.Device.Id} gen={input.Device.ConnectionGeneration} action={input.Action} repeat={input.IsRepeat} result={decision} scope={Router.Current?.Kind}");
            if (decision != ControllerRouteDecision.Accepted) continue;
            switch (input.Action) {
                case ControllerAction.Guide:
                    if (Router.Current?.Kind == ControllerScopeKind.Guide) CloseTop();
                    else Router.Push(ControllerScopeKind.Guide, input.Device);
                    break;
                case ControllerAction.View:
                    var friends = Router.Find(ControllerScopeKind.Friends);
                    if (friends != null) Router.Remove(friends.Id);
                    else Router.Push(ControllerScopeKind.Friends, input.Device);
                    break;
                case ControllerAction.Menu:
                    if (Router.Current?.Kind == ControllerScopeKind.HomeContextMenu) CloseTop();
                    else Router.Push(ControllerScopeKind.HomeContextMenu, input.Device);
                    break;
                case ControllerAction.Accept:
                    if (Router.Current?.Kind == ControllerScopeKind.Friends) Router.Push(ControllerScopeKind.Chat, input.Device);
                    break;
                case ControllerAction.Back: CloseTop(); break;
            }
        }
    }
}
