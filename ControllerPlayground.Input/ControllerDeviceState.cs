namespace ControllerPlayground.Input;

internal readonly record struct DeviceAction(ControllerAction Action, bool IsRepeat = false);

internal sealed class ControllerDeviceState {
    private uint _previousButtons;
    private bool _rightTriggerPressed;
    private ControllerAction _heldNavigation = ControllerAction.none;
    private ControllerAction _stickDirection = ControllerAction.none;
    private long _nextRepeat;
    private ulong _lastTimestamp;
    private bool _hasTimestamp;
    private bool _awaitingNeutral;
    private bool _allowFreshGuide;

    public ControllerDevice Device { get; set; }
    public bool IsAwaitingNeutral => _awaitingNeutral;
    internal string DiagnosticState => $"previousButtons=0x{_previousButtons:X8} stickDirection={_stickDirection} heldNavigation={_heldNavigation} nextRepeatMs={_nextRepeat} neutralWait={_awaitingNeutral} freshGuideAllowed={_allowFreshGuide} lastNativeTime={_lastTimestamp}";
    public ControllerDeviceState(ControllerDevice device, bool requireNeutral) {
        Device = device;
        _awaitingNeutral = requireNeutral;
    }

    public void RequireNeutral(bool allowFreshGuide = false) {
        // A scope transition must never weaken an already-strict reconnect gate.
        _allowFreshGuide = allowFreshGuide && (!_awaitingNeutral || _allowFreshGuide);
        _awaitingNeutral = true;
        _previousButtons = 0;
        _rightTriggerPressed = false;
        ResetNavigation();
    }

    private void ResetNavigation() {
        _heldNavigation = _stickDirection = ControllerAction.none;
        _nextRepeat = 0;
    }

    private static bool IsNeutral(ControllerDeviceSnapshotV2 sample) {
        var s = sample.State;
        return s.Buttons == 0 && s.SystemButtons == 0 && sample.PressedButtons == 0 && sample.GuidePressed == 0 &&
            s.LeftTrigger <= 0.30f && s.RightTrigger <= 0.30f &&
            Math.Abs(s.LeftThumbstickX) <= 0.30f && Math.Abs(s.LeftThumbstickY) <= 0.30f &&
            Math.Abs(s.RightThumbstickX) <= 0.30f && Math.Abs(s.RightThumbstickY) <= 0.30f;
    }

    private static readonly (ControllerButtons Button, ControllerAction Action)[] OneShotMappings = {
        (ControllerButtons.Accept, ControllerAction.Accept), (ControllerButtons.Back, ControllerAction.Back),
        (ControllerButtons.X, ControllerAction.X), (ControllerButtons.Y, ControllerAction.Y),
        (ControllerButtons.Menu, ControllerAction.Menu), (ControllerButtons.View, ControllerAction.View),
        (ControllerButtons.LeftShoulder, ControllerAction.PreviousTab),
        (ControllerButtons.RightShoulder, ControllerAction.NextTab)
    };

    public IReadOnlyList<DeviceAction> Process(ControllerDeviceSnapshotV2 sample, long now, ControllerState? projection = null) {
        List<DeviceAction> actions = new();
        if (_hasTimestamp && sample.Timestamp < _lastTimestamp) return actions;
        if (_awaitingNeutral) {
            if (sample.ReadingAvailable != 0 && IsNeutral(sample)) {
                _awaitingNeutral = false;
                _lastTimestamp = sample.Timestamp;
                _hasTimestamp = true;
                _allowFreshGuide = false;
            }
            else if (_allowFreshGuide && sample.GuidePressed != 0)
                actions.Add(new(ControllerAction.Guide));
            return actions;
        }
        // A consumed-on-poll native Guide pulse may share the last sample timestamp.
        if (sample.GuidePressed != 0) actions.Add(new(ControllerAction.Guide));
        if (sample.ReadingAvailable == 0) {
            ResetNavigation();
            return actions;
        }
        var state = sample.State;
        bool fresh = !_hasTimestamp || sample.Timestamp > _lastTimestamp;
        uint pressed = state.Buttons & ~_previousButtons;
        if (fresh) pressed |= sample.PressedButtons;
        _previousButtons = state.Buttons;
        _lastTimestamp = sample.Timestamp;
        _hasTimestamp = true;
        bool trigger = state.RightTrigger >= 0.60f;
        bool triggerEdge = trigger && !_rightTriggerPressed;
        _rightTriggerPressed = trigger;
        foreach (var mapping in OneShotMappings)
            if ((pressed & (uint)mapping.Button) != 0) actions.Add(new(mapping.Action));
        if (triggerEdge) actions.Add(new(ControllerAction.RightTrigger));
        // Preserve priority, but retain all simultaneous one-shot presses in the stream.
        if (actions.Count != 0) return actions;

        if (sample.HistoryDiscontinuity != 0) ResetNavigation();
        ControllerAction dpad = ControllerAction.none;
        var buttons = (ControllerButtons)(state.Buttons | pressed);
        if ((buttons & ControllerButtons.DpadUp) != 0) dpad = ControllerAction.NavigateUp;
        if ((buttons & ControllerButtons.DpadDown) != 0) dpad = ControllerAction.NavigateDown;
        if ((buttons & ControllerButtons.DpadLeft) != 0) dpad = ControllerAction.NavigateLeft;
        if ((buttons & ControllerButtons.DpadRight) != 0) dpad = ControllerAction.NavigateRight;
        float x = fresh ? projection?.LeftThumbstickX ?? state.LeftThumbstickX : state.LeftThumbstickX;
        float y = fresh ? projection?.LeftThumbstickY ?? state.LeftThumbstickY : state.LeftThumbstickY;
        if (Math.Abs(x) <= 0.30f && Math.Abs(y) <= 0.30f) {
            _stickDirection = ControllerAction.none;
        } else if (Math.Abs(x) >= 0.65f || Math.Abs(y) >= 0.65f) {
            ControllerAction candidate = Math.Abs(x) >= Math.Abs(y)
                ? (x > 0 ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft)
                : (y > 0 ? ControllerAction.NavigateUp : ControllerAction.NavigateDown);
            bool opposite = (_stickDirection, candidate) is
                (ControllerAction.NavigateLeft, ControllerAction.NavigateRight) or
                (ControllerAction.NavigateRight, ControllerAction.NavigateLeft) or
                (ControllerAction.NavigateUp, ControllerAction.NavigateDown) or
                (ControllerAction.NavigateDown, ControllerAction.NavigateUp);
            if (_stickDirection == ControllerAction.none || opposite || Math.Abs(Math.Abs(x) - Math.Abs(y)) >= 0.15f)
                _stickDirection = candidate;
        }
        ControllerAction navigation = dpad != ControllerAction.none ? dpad : _stickDirection;
        if (navigation == ControllerAction.none) {
            _heldNavigation = ControllerAction.none;
            _nextRepeat = 0;
        } else if (navigation != _heldNavigation) {
            _heldNavigation = navigation;
            _nextRepeat = now + 400;
            actions.Add(new(navigation));
        } else if (now >= _nextRepeat) {
            _nextRepeat = now + 120;
            actions.Add(new(navigation, true));
        }
        return actions;
    }
}
