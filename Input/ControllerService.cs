using System;

namespace ControllerPlayground.Input;

internal sealed class ControllerService {
    public bool IsConnected { get; private set; }

    public ControllerFamily Family { get; private set; } = ControllerFamily.Unknown;
    public event Action<ControllerFamily>? ControllerFamilyChanged;

    public event Action<bool>? ConnectionChanged;
    private uint _previousButtons;

    private uint _previousSystemButtons;

    // navigational repeat state
    private ControllerAction _heldNavigationAction = ControllerAction.none;
    private long _nextNavigationRepeatTime;
    private ControllerAction _stickDirection = ControllerAction.none;

    // Constants for repeat behavior
    private const int InitialRepeatDelayMs = 400;
    private const int RepeatIntervalMs = 120;

    public ControllerAction PollAction() {
        if (!ControllerInputNative.ControllerInput_GetState(out ControllerState state)) {
            if (IsConnected) {
                ResetInputState();
            }
            SetConnectionState(false);
            SetControllerFamily(ControllerFamily.Unknown);
            return ControllerAction.none;
        }

        SetConnectionState(true);

        if (Family == ControllerFamily.Unknown) {
            UpdateControllerFamily();
        }

        ControllerButtons currentButtons = (ControllerButtons)state.Buttons;

        ControllerButtons previousButton = (ControllerButtons)_previousButtons;

        ControllerButtons pressedThisFrame = currentButtons & ~previousButton;

        _previousButtons = state.Buttons;


        ControllerSystemButtons currentSystemButtons = (ControllerSystemButtons)state.SystemButtons;

        ControllerSystemButtons previousSystemButtons = (ControllerSystemButtons)_previousSystemButtons;

        ControllerSystemButtons systemPressedThisFrame = currentSystemButtons & ~previousSystemButtons;

        _previousSystemButtons = state.SystemButtons;

        // System buttons get highest priority.
        if ((systemPressedThisFrame & ControllerSystemButtons.Guide) != 0)
            return ControllerAction.Guide;

        // One-shot buttons should be handled before held navigation.
        // Otherwise a D-pad repeat can consume this poll and the button edge is lost.
        if ((pressedThisFrame & ControllerButtons.Accept) != 0)
            return ControllerAction.Accept;

        if ((pressedThisFrame & ControllerButtons.Back) != 0)
            return ControllerAction.Back;

        if ((pressedThisFrame & ControllerButtons.Menu) != 0)
            return ControllerAction.Menu;

        if ((pressedThisFrame & ControllerButtons.View) != 0)
            return ControllerAction.View;

        if ((pressedThisFrame & ControllerButtons.LeftShoulder) != 0)
            return ControllerAction.PreviousTab;

        if ((pressedThisFrame & ControllerButtons.RightShoulder) != 0)
            return ControllerAction.NextTab;


        ControllerAction dpadAction = ControllerAction.none;

        if ((currentButtons & ControllerButtons.DpadUp) != 0)
            dpadAction = ControllerAction.NavigateUp;

        if ((currentButtons & ControllerButtons.DpadDown) != 0)
            dpadAction = ControllerAction.NavigateDown;

        if ((currentButtons & ControllerButtons.DpadLeft) != 0)
            dpadAction = ControllerAction.NavigateLeft;

        if ((currentButtons & ControllerButtons.DpadRight) != 0)
            dpadAction = ControllerAction.NavigateRight;


        const float pressThreshold = 0.65f;
        const float releaseThreshold = 0.30f;

        float x = state.LeftThumbstickX;
        float y = state.LeftThumbstickY;

        ControllerAction stickAction = ControllerAction.none;

        // Stick is pushed far enough to choose a direction
        const float directionSwitchMargin = 0.15f;

        if (Math.Abs(x) <= releaseThreshold &&
            Math.Abs(y) <= releaseThreshold) {

            _stickDirection = ControllerAction.none;
        } else if (Math.Abs(x) >= pressThreshold ||
                   Math.Abs(y) >= pressThreshold) {

            ControllerAction candidateDirection;

            if (Math.Abs(x) >= Math.Abs(y)) {
                candidateDirection =
                    x > 0
                        ? ControllerAction.NavigateRight
                        : ControllerAction.NavigateLeft;
            } else {
                candidateDirection =
                    y > 0
                        ? ControllerAction.NavigateUp
                        : ControllerAction.NavigateDown;
            }

            if (_stickDirection == ControllerAction.none) {
                _stickDirection = candidateDirection;
            } else if (candidateDirection != _stickDirection) {
                bool oppositeDirection =
                    (_stickDirection == ControllerAction.NavigateLeft &&
                     candidateDirection == ControllerAction.NavigateRight) ||
                    (_stickDirection == ControllerAction.NavigateRight &&
                     candidateDirection == ControllerAction.NavigateLeft) ||
                    (_stickDirection == ControllerAction.NavigateUp &&
                     candidateDirection == ControllerAction.NavigateDown) ||
                    (_stickDirection == ControllerAction.NavigateDown &&
                     candidateDirection == ControllerAction.NavigateUp);

                float axisDifference =
                    Math.Abs(Math.Abs(x) - Math.Abs(y));

                if (oppositeDirection ||
                    axisDifference >= directionSwitchMargin) {

                    _stickDirection = candidateDirection;
                }
            }
          }

        stickAction = _stickDirection;

        ControllerAction navigationAction = dpadAction != ControllerAction.none ? dpadAction : stickAction;


        // Handle repeat behavior
        ControllerAction navigationResult = HandleRepeat(navigationAction, ref _heldNavigationAction, ref _nextNavigationRepeatTime);
        if (navigationResult != ControllerAction.none)
            return navigationResult;

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

    private void UpdateControllerFamily() {
        if (!ControllerInputNative.ControllerInput_GetDeviceInfo(out ControllerDeviceInfo info)) {
            SetControllerFamily(ControllerFamily.Unknown);
            return;
        }

        if (info.VendorId == 0x045E) {
            SetControllerFamily(ControllerFamily.Xbox);
        } else if (info.VendorId == 0x054C) {
            SetControllerFamily(ControllerFamily.PlayStation);
        } else {
            SetControllerFamily(ControllerFamily.Unknown);
        }

        System.Diagnostics.Debug.WriteLine(
            $"Controller VID 0x{info.VendorId:X4}, PID 0x{info.ProductId:X4}, Family: {Family}");
    }

    private void SetControllerFamily(ControllerFamily family) {
        if (Family == family)
            return;

        Family = family;
        ControllerFamilyChanged?.Invoke(family);
    }

    private void ResetInputState() {
        _heldNavigationAction = ControllerAction.none;
        _nextNavigationRepeatTime = 0;
        _stickDirection = ControllerAction.none;
    }
}
