using ControllerPlayground.Input;

internal static class NavigationRegressionTests {
    private static void Check(bool condition, string message = "navigation regression") {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class Source : IControllerInputSource {
        public ControllerInputFrame Frame = new(Array.Empty<ControllerDeviceSnapshotV2>(), Array.Empty<ControllerCaptureEventV2>());
        public ControllerInputFrame ReadFrame() => Frame;
    }
    private sealed class Rig {
        public long Now;
        public ulong Time;
        public readonly Source Source = new();
        public readonly ControllerService Service;
        public Rig() => Service = new(Source, () => Now);
        public IReadOnlyList<ControllerActionEvent> Poll(uint buttons = 0, float x = 0, float y = 0, uint pressed = 0) {
            Source.Frame = new(new[] { Program.Sample(1, ++Time, buttons: buttons, x: x, y: y, pressed: pressed) }, Array.Empty<ControllerCaptureEventV2>());
            return Service.PollActions();
        }
    }
    public static int Run() {
        int count = 0;
        void Test(string name, Action body) { body(); ++count; Console.WriteLine($"PASS navigation: {name}"); }
        Test("mapped Xbox A/Space and B/Escape cannot bypass the attributed router", () => {
            Check(ShellKeyPolicy.SuppressUnattributedControllerKey(32, 195));
            Check(ShellKeyPolicy.SuppressUnattributedControllerKey(27, 196));
            Check(ShellKeyPolicy.SuppressUnattributedControllerKey(32, 142));
            Check(ShellKeyPolicy.SuppressUnattributedControllerKey(27, 143));
        });
        Test("all Navigation aliases and Gamepad directions are blocked, even when remapped", () => {
            foreach (uint original in Enumerable.Range(136, 8).Concat(Enumerable.Range(195, 24)).Select(v => (uint)v)) {
                Check(ShellKeyPolicy.SuppressUnattributedControllerKey(39, original), $"unfiltered original={original}");
                Check(ShellKeyPolicy.SuppressUnattributedControllerKey(original, 0), $"unfiltered mapped={original}");
            }
        });
        Test("physical Space/Escape/Enter/arrows/Tab/F1/F10 remain available", () => {
            foreach (uint key in new uint[] { 32, 27, 13, 37, 38, 39, 40, 9, 112, 121 })
                Check(!ShellKeyPolicy.SuppressUnattributedControllerKey(key, key));
        });
        Test("raw Xbox B is only Back through press, hold, release and short tap", () => {
            var r = new Rig(); r.Poll();
            Check(r.Poll(buttons: 8).Single().Action == ControllerAction.Back);
            r.Now = 1000; Check(r.Poll(buttons: 8).Count == 0);
            Check(r.Poll().Count == 0);
            Check(r.Poll(pressed: 8).Single().Action == ControllerAction.Back);
            Check(r.Poll().Count == 0);
            Check(r.Poll(buttons: 4).Single().Action == ControllerAction.Accept);
            r.Poll();
            Check(r.Poll(buttons: 8).Single().Action == ControllerAction.Back);
        });
        Test("each short D-pad direction yields exactly one non-repeat action", () => {
            var r = new Rig();
            foreach (uint button in new uint[] { 0x40, 0x80, 0x100, 0x200 }) {
                r.Poll();
                var input = r.Poll(button).Single(); Check(!input.IsRepeat);
                r.Now += 16; Check(r.Poll(button).Count == 0);
                Check(r.Poll().Count == 0);
                r.Now += 1000; Check(r.Poll().Count == 0);
            }
        });
        Test("held D-pad follows 400/120ms without catch-up bursts and stops on release", () => {
            var r = new Rig(); Check(!r.Poll(0x200).Single().IsRepeat);
            foreach (long time in new long[] { 16, 100, 399 }) { r.Now = time; Check(r.Poll(0x200).Count == 0); }
            foreach (long time in new long[] { 400, 520, 640, 5000 }) {
                r.Now = time; Check(r.Poll(0x200).Single().IsRepeat);
                Check(r.Poll(0x200).Count == 0);
            }
            Check(r.Poll().Count == 0); r.Now = 9000; Check(r.Poll().Count == 0);
        });
        Test("stick threshold jitter does not skip and neutral cancels repeat", () => {
            var r = new Rig(); Check(r.Poll(x: .64f).Count == 0);
            Check(!r.Poll(x: .70f, y: .68f).Single().IsRepeat);
            foreach (var xy in new[] { (.64f, .67f), (.68f, .72f), (.4f, .3f) }) {
                r.Now += 16; Check(r.Poll(x: xy.Item1, y: xy.Item2).Count == 0);
            }
            Check(r.Poll(x: .29f, y: .29f).Count == 0);
            r.Now = 1000; Check(r.Poll().Count == 0);
            Check(r.Poll(x: .8f).Single().Action == ControllerAction.NavigateRight);
        });
        Test("ownership still rejects the non-owner Back/Accept/Guide stream", () => {
            var r = new Rig();
            r.Source.Frame = new(new[] { Program.Sample(1), Program.Sample(2) }, Array.Empty<ControllerCaptureEventV2>());
            r.Service.PollActions();
            var router = new ControllerInputRouter(r.Service.CollectorSessionId, r.Service.SuppressForOwnershipTransition);
            router.UpdateDevices(r.Service.Devices);
            var owner = r.Service.Devices.Single(d => d.Id == Program.Id(1));
            router.Push(ControllerScopeKind.Guide, owner);
            r.Service.PollActions(); // neutral boundary
            r.Source.Frame = new(new[] { Program.Sample(1, 2), Program.Sample(2, 2, buttons: 12, guide: 1) }, Array.Empty<ControllerCaptureEventV2>());
            var actions = r.Service.PollActions(); router.BeginBatch(actions); router.UpdateDevices(r.Service.Devices);
            Check(actions.Count == 3 && actions.All(a => router.Route(a) == ControllerRouteDecision.Discarded));
            Check(router.Current?.Owner == owner);
        });
        return count;
    }
}
