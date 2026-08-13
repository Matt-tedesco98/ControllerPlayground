using System;

namespace ControllerPlayground.Input;

internal sealed class ControllerService {
    public bool IsConnected { get; private set; }

    public event Action<bool>? ConnectionChanged;
    private uint _previousButtons;

    //Controller Dpad handling variables
    private ControllerAction _heldDpadAction = ControllerAction.none;
    private long _nextDpadRepeatTime;

    //Controller Stick handling variables
    private ControllerAction _heldStickAction = ControllerAction.none;
    private long _nextStickRepeatTime;

    // Constants for repeat behavior
    private const int InitialRepeatDelayMs = 400;
    private const int RepeatIntervalMs = 120;

    public ControllerAction PollAction() {
        if (!ControllerInputNative.ControllerInput_GetState(out ControllerState state)) {
            SetConnectionState(false);
            return ControllerAction.none;
        }

        SetConnectionState(true);

        ControllerButtons currentButtons = (ControllerButtons)state.Buttons;    

        ControllerButtons previousButton = (ControllerButtons)_previousButtons;

        ControllerButtons pressedThisFrame = currentButtons & ~previousButton;

        _previousButtons = state.Buttons;

        ControllerAction dpadAction = ControllerAction.none;

        if ((currentButtons & ControllerButtons.DpadUp) != 0)
            dpadAction = ControllerAction.NavigateUp;

        if ((currentButtons & ControllerButtons.DpadDown) != 0)
            dpadAction = ControllerAction.NavigateDown;

        if ((currentButtons & ControllerButtons.DpadLeft) != 0)
            dpadAction = ControllerAction.NavigateLeft;

        if ((currentButtons & ControllerButtons.DpadRight) != 0)
            dpadAction = ControllerAction.NavigateRight;

        // Handle Dpad repeat behavior
        ControllerAction dpadResult = HandleRepeat(dpadAction, ref _heldDpadAction, ref _nextDpadRepeatTime);

        if (dpadResult != ControllerAction.none)
            return dpadResult;

        if ((pressedThisFrame & ControllerButtons.Accept) != 0)
            return ControllerAction.Accept;

        if ((pressedThisFrame & ControllerButtons.Back) != 0)
            return ControllerAction.Back;

        if ((pressedThisFrame & ControllerButtons.Menu) != 0)
            return ControllerAction.Menu;

        if ((pressedThisFrame & ControllerButtons.View) != 0)
            return ControllerAction.View;


        const float pressThreshold = 0.65f;
        const float releaseThreshold = 0.30f;

        float x = state.LeftThumbstickX;
        float y = state.LeftThumbstickY;

        ControllerAction stickAction = ControllerAction.none;

        // Stick is pushed far enough to choose a direction
        if (Math.Abs(x) >= pressThreshold || Math.Abs(y) >= pressThreshold) {
            // Using the angle of the stick to determine the direction or switch randomly if the stick is in a diagonal position
            if(Math.Abs(x) >= Math.Abs(y)) {
                stickAction = x > 0 ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft;
            } else {
                stickAction = y > 0 ? ControllerAction.NavigateUp : ControllerAction.NavigateDown;
            }
        }

        //Stick returned close enough to center to release the action
        else if (Math.Abs(x) <= releaseThreshold && Math.Abs(y) <= releaseThreshold) {
            stickAction = ControllerAction.none;
        } else {
            // Stick is still held in a direction, but not far enough to trigger a new action
            stickAction = _heldStickAction;
        }


        // Handle Stick repeat behavior
        ControllerAction stickResult = HandleRepeat(stickAction, ref _heldStickAction, ref _nextStickRepeatTime);
        if (stickResult != ControllerAction.none)
            return stickResult;

        return ControllerAction.none;
    }

    private void SetConnectionState(bool connected) {
        if (IsConnected == connected)
            return;

        IsConnected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    private static ControllerAction HandleRepeat(
        ControllerAction action,
        ref ControllerAction heldAction,
        ref long nextRepeatTime) {

        long now = Environment.TickCount64;

        if (action == ControllerAction.none) {
            heldAction = ControllerAction.none;
            nextRepeatTime = 0;
            return ControllerAction.none;
        }

        if (action != heldAction) {
            // New direction pressed move immediately
            heldAction = action;
            nextRepeatTime = now + InitialRepeatDelayMs;
            return action;
        }

        if (now >= nextRepeatTime) {
            // Still held after delay, repeat the action
            nextRepeatTime = now + RepeatIntervalMs;
            return action;
        }

        return ControllerAction.none;
    }

}
