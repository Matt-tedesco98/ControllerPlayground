using System;

namespace ControllerPlayground.Input;

internal sealed class ControllerService {
    private uint _previousButtons;
    private ControllerAction _heldStickAction = ControllerAction.none;
    private long _nextStickRepeatTime;
    private const int InitialRepeatDelayMs = 400;
    private const int RepeatIntervalMs = 120;

    public ControllerAction PollAction() {
        if (!ControllerInputNative.ControllerInput_GetState(out ControllerState state)) {
            return ControllerAction.none;
        }

        uint currentButtons = state.Buttons;
        if (currentButtons != _previousButtons) {
            System.Diagnostics.Debug.WriteLine(
                $"RAW: 0x{_previousButtons:X8} -> 0x{currentButtons:X8}"
                );
        }
        uint pressedThisFrame = currentButtons & ~_previousButtons;
        _previousButtons = currentButtons;

        if ((pressedThisFrame & 0x00000040) != 0)
            return ControllerAction.NavigateUp;

        if ((pressedThisFrame & 0x00000080) != 0)
            return ControllerAction.NavigateDown;

        if ((pressedThisFrame & 0x00000100) != 0)
            return ControllerAction.NavigateLeft;

        if ((pressedThisFrame & 0x00000200) != 0)
            return ControllerAction.NavigateRight;

        if ((pressedThisFrame & 0x00000004) != 0)
            return ControllerAction.Accept;

        if ((pressedThisFrame & 0x00000008) != 0)
            return ControllerAction.Back;

        if ((pressedThisFrame & 0x00000001) != 0)
            return ControllerAction.Menu;

        if ((pressedThisFrame & 0x00000002) != 0)
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

        long now = Environment.TickCount64;

        if (stickAction == ControllerAction.none) {
            _heldStickAction = ControllerAction.none;
            _nextStickRepeatTime = 0;
        }
        else if (stickAction != _heldStickAction) {
            // New direction pressed move immediately
            _heldStickAction = stickAction;
            _nextStickRepeatTime = now + InitialRepeatDelayMs;
            return stickAction;
        }
        else if (now >= _nextStickRepeatTime) {
            // Still held after delay, repeat the action
            _nextStickRepeatTime = now + RepeatIntervalMs;
            return stickAction;
        }

        return ControllerAction.none;
    }

}
