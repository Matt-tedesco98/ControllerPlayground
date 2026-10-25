using ControllerPlayground.Input;

internal static class OwnershipTests {
    private static int _passed;
    private static void Check(bool condition, string message = "ownership assertion failed") {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Test(string name, Action test) { test(); ++_passed; Console.WriteLine($"PASS ownership: {name}"); }
    private static ControllerDevice Device(byte id, ulong generation = 1) => new(
        Program.Id(id), Program.Id(id), generation, id == 2 ? (ushort)0x054C : (ushort)0x045E, 1,
        id == 2 ? ControllerFamily.PlayStation : ControllerFamily.Xbox);
    private sealed class Fixture {
        public readonly Guid Session = Guid.NewGuid();
        public readonly ControllerInputRouter Router;
        public int Suppressions;
        private ulong _sequence;
        public Fixture() {
            Router = new(Session, () => ++Suppressions);
            Router.UpdateDevices(new[] { Device(1), Device(2) });
        }
        public ControllerActionEvent Input(byte id, ControllerAction action = ControllerAction.Accept, ulong gen = 1, bool repeat = false) =>
            new(Session, ++_sequence, Device(id, gen), action, _sequence, (long)_sequence, repeat);
        public ControllerRouteDecision Route(ControllerActionEvent input) {
            Router.BeginBatch(new[] { input }); return Router.Route(input);
        }
    }
    private sealed class Source : IControllerInputSource {
        public ControllerInputFrame Frame = new(Array.Empty<ControllerDeviceSnapshotV2>(), Array.Empty<ControllerCaptureEventV2>());
        public ControllerInputFrame ReadFrame() => Frame;
    }
    private sealed class Integration {
        public readonly Source Source = new();
        public readonly ControllerService Service;
        public readonly ControllerInputRouter Router;
        public Integration() {
            Service = new(Source, () => 0);
            Router = new(Service.CollectorSessionId, Service.SuppressForOwnershipTransition);
        }
        public List<(ControllerActionEvent Input, ControllerRouteDecision Decision)> Pump(params ControllerDeviceSnapshotV2[] samples) {
            Source.Frame = new(samples, Array.Empty<ControllerCaptureEventV2>());
            var actions = Service.PollActions();
            Router.BeginBatch(actions); Router.UpdateDevices(Service.Devices);
            return actions.Select(a => (a, Router.Route(a))).ToList();
        }
    }
    public static int Run() {
        _passed = 0;
        Test("root navigation accepts either connected controller", () => {
            var f = new Fixture();
            Check(f.Route(f.Input(1, ControllerAction.NavigateLeft)) == ControllerRouteDecision.Accepted);
            Check(f.Route(f.Input(2, ControllerAction.NavigateRight)) == ControllerRouteDecision.Accepted);
        });
        Test("ownership precedes Guide/View and all non-owner actions", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Guide, Device(1));
            foreach (var action in new[] { ControllerAction.Guide, ControllerAction.View, ControllerAction.Back, ControllerAction.Accept, ControllerAction.NavigateDown })
                Check(f.Route(f.Input(2, action)) == ControllerRouteDecision.Discarded);
            Check(f.Route(f.Input(1, ControllerAction.Guide)) == ControllerRouteDecision.Accepted);
        });
        Test("opening scope drops the remainder of an already generated batch", () => {
            var f = new Fixture(); var open = f.Input(1, ControllerAction.Guide); var select = f.Input(1); var other = f.Input(2);
            f.Router.BeginBatch(new[] { open, select, other });
            Check(f.Router.Route(open) == ControllerRouteDecision.Accepted);
            f.Router.Push(ControllerScopeKind.Guide, Device(1));
            Check(f.Router.Route(select) == ControllerRouteDecision.Discarded);
            Check(f.Router.Route(other) == ControllerRouteDecision.Discarded);
            Check(f.Route(f.Input(1)) == ControllerRouteDecision.Accepted);
        });
        Test("nested scopes inherit owner and retain independent focus tokens", () => {
            var f = new Fixture();
            var parent = f.Router.Push(ControllerScopeKind.Friends, Device(1), "tile");
            var chat = f.Router.Push(ControllerScopeKind.Chat, focusReturnToken: "friend-card");
            var guide = f.Router.Push(ControllerScopeKind.Guide, focusReturnToken: "chat-input");
            Check(guide.Owner == Device(1) && chat.Owner == Device(1));
            Check((string?)f.Router.Remove(guide.Id).Single().FocusReturnToken == "chat-input" && f.Router.Current == chat);
            Check((string?)f.Router.Remove(chat.Id).Single().FocusReturnToken == "friend-card" && f.Router.Current == parent);
        });
        Test("a different controller cannot acquire a nested interaction", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Friends, Device(1));
            bool rejected = false;
            try { f.Router.Push(ControllerScopeKind.Guide, Device(2)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && f.Router.Scopes.Count == 1);
        });
        Test("parent close unwinds children and invalidates focus revision", () => {
            var f = new Fixture(); var root = f.Router.Push(ControllerScopeKind.Friends, Device(1), "page");
            f.Router.Push(ControllerScopeKind.Chat, focusReturnToken: "card");
            long revision = f.Router.Revision;
            var removed = f.Router.Remove(root.Id);
            Check(removed.Count == 2 && removed[0].Kind == ControllerScopeKind.Chat && removed[1] == root);
            Check(f.Router.Current == null && f.Router.Revision > revision);
        });
        Test("async scope completion preserves newer blocking surface", () => {
            var f = new Fixture(); var handoff = f.Router.Push(ControllerScopeKind.KeyboardHandoff, Device(1));
            var auth = f.Router.Push(ControllerScopeKind.Authentication);
            f.Router.Remove(handoff.Id, descendants: false);
            Check(f.Router.Current == auth && f.Router.Scopes.Count == 1);
        });
        Test("owner disconnect pauses until an explicit fresh claim", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Friends, Device(1));
            f.Router.UpdateDevices(new[] { Device(2) });
            Check(f.Router.NeedsRecovery);
            Check(f.Route(f.Input(2, ControllerAction.Guide)) == ControllerRouteDecision.Discarded);
            Check(f.Route(f.Input(2, repeat: true)) == ControllerRouteDecision.Discarded);
            Check(f.Route(f.Input(2)) == ControllerRouteDecision.Claimed && f.Router.Current?.Owner == Device(2));
            Check(f.Route(f.Input(2)) == ControllerRouteDecision.Accepted);
        });
        Test("reconnect generation cannot silently regain an old lease", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Guide, Device(1));
            f.Router.UpdateDevices(new[] { Device(1, 2), Device(2) });
            Check(f.Router.NeedsRecovery && f.Router.Current?.Owner?.ConnectionGeneration == 1);
            Check(f.Route(f.Input(1, ControllerAction.Guide, 2)) == ControllerRouteDecision.Discarded);
            Check(f.Route(f.Input(1, gen: 2)) == ControllerRouteDecision.Claimed);
            Check(f.Router.Current?.Owner?.ConnectionGeneration == 2);
        });
        Test("takeover updates the suspended parent scopes as well", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Friends, Device(1));
            f.Router.Push(ControllerScopeKind.Chat); var guide = f.Router.Push(ControllerScopeKind.Guide);
            f.Router.UpdateDevices(new[] { Device(2) });
            Check(f.Route(f.Input(2)) == ControllerRouteDecision.Claimed);
            Check(f.Router.Scopes.All(s => s.Owner == Device(2)));
            f.Router.Remove(guide.Id);
            Check(f.Router.Current?.Kind == ControllerScopeKind.Chat && f.Router.CurrentOwnerAvailable);
        });
        Test("non-owner disconnect does not pause or reset the owner", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Guide, Device(1)); long revision = f.Router.Revision;
            f.Router.UpdateDevices(new[] { Device(1) });
            Check(!f.Router.NeedsRecovery && f.Router.Revision == revision);
        });
        Test("authentication and keyboard handoff block even global shortcuts", () => {
            foreach (var kind in new[] { ControllerScopeKind.Authentication, ControllerScopeKind.KeyboardHandoff }) {
                var f = new Fixture(); var scope = f.Router.Push(kind, Device(1));
                Check(f.Route(f.Input(1, ControllerAction.Guide)) == ControllerRouteDecision.Discarded);
                Check(f.Route(f.Input(2)) == ControllerRouteDecision.Discarded);
                f.Router.Remove(scope.Id); // Mouse/physical-keyboard/programmatic dismissal needs no controller.
                Check(f.Router.Current == null);
            }
        });
        Test("mouse-opened scope consumes the first controller Accept as a claim", () => {
            var f = new Fixture(); f.Router.Push(ControllerScopeKind.Guide);
            Check(f.Router.NeedsRecovery);
            Check(f.Route(f.Input(2)) == ControllerRouteDecision.Claimed);
            Check(f.Router.Current?.Owner == Device(2));
        });
        Test("discarded non-owner, stale session, and duplicate input are never replayed", () => {
            var f = new Fixture(); var scope = f.Router.Push(ControllerScopeKind.Guide, Device(1));
            var denied = f.Input(2);
            Check(f.Route(denied) == ControllerRouteDecision.Discarded);
            f.Router.Remove(scope.Id);
            Check(f.Route(denied) == ControllerRouteDecision.Discarded);
            var stale = f.Input(1) with { CollectorSessionId = Guid.NewGuid() };
            Check(f.Route(stale) == ControllerRouteDecision.Discarded);
        });
        Test("navigation invalidates queued focus even without an open scope", () => {
            var f = new Fixture(); long revision = f.Router.Revision; f.Router.Clear();
            Check(f.Router.Revision != revision && f.Suppressions == 1);
        });
        Test("integration: held non-owner input cannot leak when a modal closes", () => {
            var f = new Integration(); f.Pump(Program.Sample(1), Program.Sample(2));
            var scope = f.Router.Push(ControllerScopeKind.Guide, Device(1));
            f.Pump(Program.Sample(1, 2), Program.Sample(2, 2)); // Arm after transition.
            Check(f.Pump(Program.Sample(1, 3), Program.Sample(2, 3, buttons: 4)).Single().Decision == ControllerRouteDecision.Discarded);
            f.Router.Remove(scope.Id);
            Check(f.Pump(Program.Sample(1, 4), Program.Sample(2, 4, buttons: 4)).Count == 0);
            f.Pump(Program.Sample(1, 5), Program.Sample(2, 5));
            Check(f.Pump(Program.Sample(1, 6), Program.Sample(2, 6, buttons: 4)).Single().Decision == ControllerRouteDecision.Accepted);
        });
        Test("integration: held-input reconnect gate and explicit reclaim for both families", () => {
            foreach (byte id in new byte[] { 1, 2 }) {
                var f = new Integration(); f.Pump(Program.Sample(id));
                f.Router.Push(ControllerScopeKind.Guide, Device(id));
                f.Pump(Program.Sample(id, 2));
                f.Pump(Program.Sample(id, 3, connected: false));
                Check(f.Pump(Program.Sample(id, 4, buttons: 4, generation: 2)).Count == 0);
                Check(f.Pump(Program.Sample(id, 5, buttons: 4, generation: 2)).Count == 0);
                f.Pump(Program.Sample(id, 6, generation: 2));
                Check(f.Pump(Program.Sample(id, 7, buttons: 4, generation: 2)).Single().Decision == ControllerRouteDecision.Claimed);
                Check(f.Pump(Program.Sample(id, 8, buttons: 4, generation: 2)).Count == 0);
                f.Pump(Program.Sample(id, 9, generation: 2));
                Check(f.Pump(Program.Sample(id, 10, buttons: 4, generation: 2)).Single().Decision == ControllerRouteDecision.Accepted);
            }
        });
        Test("integration: disconnect transition discards a precomputed takeover action", () => {
            var f = new Integration(); f.Pump(Program.Sample(1), Program.Sample(2));
            f.Router.Push(ControllerScopeKind.Guide, Device(1));
            f.Pump(Program.Sample(1, 2), Program.Sample(2, 2));
            var result = f.Pump(Program.Sample(1, 3, connected: false), Program.Sample(2, 3, buttons: 4));
            Check(result.Single().Decision == ControllerRouteDecision.Discarded && f.Router.NeedsRecovery);
        });
        Test("fresh background Guide survives scope suppression without weakening reconnect gates", () => {
            var f = new Integration(); f.Pump(Program.Sample(1));
            f.Service.SuppressForOwnershipTransition();
            Check(f.Pump(Program.Sample(1, 2, available: false, guide: 1)).Single().Input.Action == ControllerAction.Guide);
            Check(f.Pump(Program.Sample(1, 3, buttons: 4)).Count == 0);
            f.Pump(Program.Sample(1, 4, connected: false));
            f.Pump(Program.Sample(1, 5, generation: 2, available: false));
            f.Service.SuppressForOwnershipTransition();
            Check(f.Pump(Program.Sample(1, 6, generation: 2, available: false, guide: 1)).Count == 0);
        });
        return _passed;
    }
}
