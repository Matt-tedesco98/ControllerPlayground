using ControllerPlayground.Input;

namespace ControllerPlayground.FeatureTests;

// These are real assertions against the production managed routing class, using
// synthetic attributed actions. They do NOT simulate a Windows HID/GameInput device.
internal static class RouterScenarios {
    internal static IReadOnlyList<FeatureScenario> All { get; } = new[] {
        new FeatureScenario(
            "exit-confirmation.owner-isolation", "exit-confirmation", "managed-router",
            "Only the modal's controller owner can route actions",
            ExitOwnerIsolation),
        new FeatureScenario(
            "exit-confirmation.scope-close-invalidation", "exit-confirmation", "managed-router",
            "Opening and closing the modal invalidate queued actions and preserve the focus token",
            ExitScopeCloseInvalidation),
        new FeatureScenario(
            "guide-overlay.nested-owner-inheritance", "guide-overlay", "managed-router",
            "Nested scopes preserve Guide ownership and unwind together",
            GuideNestedOwner),
        new FeatureScenario(
            "controller-ownership.disconnect-fresh-claim", "controller-ownership", "managed-router",
            "After owner disconnect, a fresh non-repeated Accept claims but does not activate",
            DisconnectFreshClaim),
        new FeatureScenario(
            "controller-ownership.reconnect-generation", "controller-ownership", "managed-router",
            "A new generation must claim ownership; the stale generation cannot act",
            ReconnectGeneration),
        new FeatureScenario(
            "controller-ownership.unowned-modal-claim", "controller-ownership", "managed-router",
            "First Accept on a mouse-opened modal claims ownership without activation",
            UnownedModalClaim)
    };

    private static void Assert(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static ControllerDevice Device(byte id, ulong generation = 1) {
        var bytes = new byte[32];
        bytes[^1] = id;
        var deviceId = ControllerDeviceId.FromBytes(bytes);
        return new ControllerDevice(
            deviceId, deviceId, generation,
            id == 2 ? (ushort)0x054C : (ushort)0x045E, 1,
            id == 2 ? ControllerFamily.PlayStation : ControllerFamily.Xbox);
    }

    private sealed class Fixture {
        private ulong _sequence;
        internal Guid Session { get; } = Guid.NewGuid();
        internal ControllerInputRouter Router { get; }
        internal ControllerDevice Xbox { get; } = Device(1);
        internal ControllerDevice DualSense { get; } = Device(2);

        internal Fixture() {
            Router = new ControllerInputRouter(Session, () => { });
            Router.UpdateDevices(new[] { Xbox, DualSense });
        }

        internal ControllerActionEvent Action(
            byte id, ControllerAction action = ControllerAction.Accept,
            ulong generation = 1, bool repeat = false) {
            ulong sequence = ++_sequence;
            return new ControllerActionEvent(Session, sequence, Device(id, generation),
                action, sequence, (long)sequence, repeat);
        }

        internal ControllerRouteDecision Send(
            byte id, ControllerAction action = ControllerAction.Accept,
            ulong generation = 1, bool repeat = false) {
            var input = Action(id, action, generation, repeat);
            Router.BeginBatch(new[] { input });
            return Router.Route(input);
        }
    }

    private static void ExitOwnerIsolation() {
        var f = new Fixture();
        f.Router.Push(ControllerScopeKind.ExitConfirmation, f.Xbox);
        Assert(f.Send(2, ControllerAction.Guide) == ControllerRouteDecision.Discarded,
            "Non-owner Guide must be rejected before shortcut dispatch");
        Assert(f.Send(2) == ControllerRouteDecision.Discarded,
            "Non-owner must not confirm exit");
        Assert(f.Send(1) == ControllerRouteDecision.Accepted,
            "Owner's Accept must be allowed through the router");
    }

    private static void ExitScopeCloseInvalidation() {
        var f = new Fixture();
        var queued = f.Action(1);
        f.Router.BeginBatch(new[] { queued });
        object focusToken = new();
        var scope = f.Router.Push(ControllerScopeKind.ExitConfirmation, f.Xbox, focusToken);
        Assert(f.Router.Route(queued) == ControllerRouteDecision.Discarded,
            "Input queued before opening a modal must not activate inside it");
        long revision = f.Router.Revision;
        var closed = f.Router.Remove(scope.Id);
        Assert(closed.Count == 1 && ReferenceEquals(closed[0].FocusReturnToken, focusToken),
            "Focus-return token must be preserved for the shell to restore focus");
        Assert(f.Router.Current == null && f.Router.Revision > revision,
            "Closing a modal must clear scope and invalidate focus work");
        Assert(f.Router.Route(queued) == ControllerRouteDecision.Discarded,
            "Already-observed input must never replay after closing");
        Assert(f.Send(2) == ControllerRouteDecision.Accepted,
            "After closing, unscoped input should be permitted again");
    }

    private static void GuideNestedOwner() {
        var f = new Fixture();
        var guide = f.Router.Push(ControllerScopeKind.Guide, f.Xbox);
        var nested = f.Router.Push(ControllerScopeKind.Friends);
        Assert(nested.Owner == f.Xbox, "Nested overlay must inherit parent owner");
        Assert(f.Send(2, ControllerAction.Back) == ControllerRouteDecision.Discarded,
            "Non-owner must not close a nested scope");
        Assert(f.Send(1, ControllerAction.Back) == ControllerRouteDecision.Accepted,
            "Owner must retain navigation permission");
        var closed = f.Router.Remove(guide.Id);
        Assert(closed.Count == 2 && f.Router.Current == null,
            "Closing the parent must unwind nested scopes");
    }

    private static void DisconnectFreshClaim() {
        var f = new Fixture();
        f.Router.Push(ControllerScopeKind.Guide, f.Xbox);
        f.Router.UpdateDevices(new[] { f.DualSense });
        Assert(f.Router.NeedsRecovery, "Disconnected owner must require recovery");
        Assert(f.Send(2, ControllerAction.Guide) == ControllerRouteDecision.Discarded,
            "Non-Accept input cannot claim ownership");
        Assert(f.Send(2, repeat: true) == ControllerRouteDecision.Discarded,
            "Held/repeated Accept cannot claim ownership");
        Assert(f.Send(2) == ControllerRouteDecision.Claimed,
            "A new Accept must claim, not activate");
        Assert(f.Router.Current?.Owner == f.DualSense, "Owner was not transferred");
        Assert(f.Send(2) == ControllerRouteDecision.Accepted,
            "A subsequent fresh action from the owner must be allowed");
    }

    private static void ReconnectGeneration() {
        var f = new Fixture();
        f.Router.Push(ControllerScopeKind.Guide, f.Xbox);
        f.Router.UpdateDevices(new[] { Device(1, 2), f.DualSense });
        Assert(f.Router.NeedsRecovery, "New generation cannot inherit an old lease");
        Assert(f.Send(1) == ControllerRouteDecision.Discarded,
            "Old generation must be rejected");
        Assert(f.Send(1, ControllerAction.Guide, generation: 2) == ControllerRouteDecision.Discarded,
            "Guide cannot silently reclaim a disconnected lease");
        Assert(f.Send(1, generation: 2) == ControllerRouteDecision.Claimed,
            "Fresh Accept must claim the new generation only");
        Assert(f.Router.Current?.Owner?.ConnectionGeneration == 2,
            "Ownership generation was not updated");
    }

    private static void UnownedModalClaim() {
        var f = new Fixture();
        f.Router.Push(ControllerScopeKind.Guide);
        Assert(f.Router.NeedsRecovery, "Mouse-opened modal should begin unowned");
        Assert(f.Send(2) == ControllerRouteDecision.Claimed,
            "First Accept must claim without triggering the UI control");
        Assert(f.Router.Current?.Owner == f.DualSense,
            "The claiming controller must become the owner");
        Assert(f.Send(2) == ControllerRouteDecision.Accepted,
            "A later independent Accept may activate a control");
    }
}
