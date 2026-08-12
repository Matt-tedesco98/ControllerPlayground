using System;

namespace ControllerPlayground.Input;

internal sealed class ControllerService {
    private uint _previousButtons;
    private ControllerAction _previousStickAction = ControllerAction.none;

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

        ControllerAction stickAction = ControllerAction.none;

        const float pressThreshold = 0.65f;
        const float releaseThreshold = 0.30f;

        if (_previousStickAction == ControllerAction.none) {

            if (state.LeftThumbstickX > pressThreshold) {
                _previousStickAction = ControllerAction.NavigateRight;
                return ControllerAction.NavigateRight;
            }

            if (state.LeftThumbstickX < -pressThreshold) {
                _previousStickAction = ControllerAction.NavigateLeft;
                return ControllerAction.NavigateLeft;
            }

            if (state.LeftThumbstickY > pressThreshold) {
                _previousStickAction = ControllerAction.NavigateUp;
                return ControllerAction.NavigateUp;
            }

            if (state.LeftThumbstickY < -pressThreshold) {
                _previousStickAction = ControllerAction.NavigateDown;
                return ControllerAction.NavigateDown;
            }
        } else {
            if (Math.Abs(state.LeftThumbstickX) < releaseThreshold && Math.Abs(state.LeftThumbstickY) < releaseThreshold) {
                _previousStickAction = ControllerAction.none;
            }
        }

        return ControllerAction.none;
    }

}
