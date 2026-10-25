using ControllerPlayground.Input;
using System.Runtime.InteropServices;

internal static class Program {
    private static int _passed;
    internal static ControllerDeviceId Id(byte value) {
        byte[] bytes = new byte[32]; bytes[31] = value;
        return ControllerDeviceId.FromBytes(bytes);
    }
    internal static ControllerDeviceSnapshotV2 Sample(byte id, ulong time = 1, uint buttons = 0,
        ulong generation = 1, bool connected = true, bool available = true,
        uint pressed = 0, uint guide = 0, float x = 0, float y = 0, float trigger = 0) => new() {
        StructSize = 144, DeviceId = Id(id), DeviceRootId = Id(id), ConnectionGeneration = generation,
        Timestamp = time, VendorId = id == 2 ? (ushort)0x054C : (ushort)0x045E, ProductId = 10,
        Connected = connected ? 1u : 0u, ReadingAvailable = available ? 1u : 0u,
        State = new() { Buttons = buttons, LeftThumbstickX = x, LeftThumbstickY = y, RightTrigger = trigger },
        PressedButtons = pressed, GuidePressed = guide
    };
    private const uint A = (uint)ControllerButtons.Accept, B = (uint)ControllerButtons.Back;
    private const uint Right = (uint)ControllerButtons.DpadRight, Left = (uint)ControllerButtons.DpadLeft;

    private static void Check(bool condition, string message = "assertion failed") {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Test(string name, Action body) {
        body(); ++_passed; Console.WriteLine($"PASS {name}");
    }

    private sealed class FakeSource : IControllerInputSource {
        public ControllerInputFrame Next = new(Array.Empty<ControllerDeviceSnapshotV2>(), Array.Empty<ControllerCaptureEventV2>());
        public ControllerInputFrame ReadFrame() => Next;
    }
    private sealed class Rig {
        public long Now;
        public readonly FakeSource Source = new();
        public readonly ControllerService Service;
        public Rig() { Service = new(Source, () => Now); }
        public IReadOnlyList<ControllerActionEvent> Poll(params ControllerDeviceSnapshotV2[] samples) {
            Source.Next = new(samples, Array.Empty<ControllerCaptureEventV2>());
            return Service.PollActions();
        }
    }

    private static void ManagedTests() {
        Test("native ABI sizes/offsets and opaque full ID", () => {
            Check(Marshal.SizeOf<ControllerDeviceId>() == 32);
            Check(Marshal.SizeOf<ControllerDeviceSnapshotV2>() == 144);
            Check(Marshal.SizeOf<ControllerCaptureEventV2>() == 160);
            Check(Marshal.SizeOf<ControllerCaptureDiagnosticsV2>() == 48);
            Check(Marshal.OffsetOf<ControllerDeviceSnapshotV2>(nameof(ControllerDeviceSnapshotV2.State)).ToInt32() == 96);
            byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            var id = ControllerDeviceId.FromBytes(bytes);
            Check(id.ToByteArray().SequenceEqual(bytes) && id.ToString() == Convert.ToHexString(bytes));
            Check(new Dictionary<ControllerDeviceId, int> { [Id(1)] = 1, [Id(2)] = 2 }.Count == 2,
                "IDs differing only in byte 32 must remain distinct");
        });
        Test("interleaved Accept edges and per-device family", () => {
            var r = new Rig();
            var first = r.Poll(Sample(1, buttons: A), Sample(2));
            Check(first.Count == 1 && first[0].Device.Id == Id(1) && first[0].Device.Family == ControllerFamily.Xbox);
            var second = r.Poll(Sample(1, 2, A), Sample(2, 2, A));
            Check(second.Count == 1 && second[0].Device.Id == Id(2) && second[0].Device.Family == ControllerFamily.PlayStation);
            Check(r.Poll(Sample(1, 3), Sample(2, 3, A)).Count == 0);
            Check(r.Poll(Sample(1, 4, A), Sample(2, 4)).Single().Device.Id == Id(1));
        });
        Test("simultaneous presses retained with unique sequence/session", () => {
            var r = new Rig();
            var actions = r.Poll(Sample(1, buttons: A | B), Sample(2, buttons: A | B));
            Check(actions.Count == 4);
            Check(actions.Select(a => a.Sequence).SequenceEqual(new ulong[] { 1, 2, 3, 4 }));
            Check(actions.All(a => a.CollectorSessionId == r.Service.CollectorSessionId && a.NativeTimestampMicroseconds == 1));
            Check(actions.Count(a => a.Action == ControllerAction.Accept) == 2);
            Check(r.Poll(Sample(1, 2, A | B), Sample(2, 2, A | B)).Count == 0);
        });
        Test("independent held navigation repeat clocks", () => {
            var r = new Rig();
            Check(!r.Poll(Sample(1, buttons: Right), Sample(2)).Single().IsRepeat);
            r.Now = 100;
            Check(r.Poll(Sample(1, buttons: Right), Sample(2, buttons: Left)).Single().Device.Id == Id(2));
            r.Now = 399; Check(r.Poll(Sample(1, buttons: Right), Sample(2, buttons: Left)).Count == 0);
            r.Now = 400; var a = r.Poll(Sample(1, buttons: Right), Sample(2, buttons: Left)).Single();
            Check(a.Device.Id == Id(1) && a.IsRepeat && a.ObservedAtMilliseconds == 400);
            r.Now = 500; Check(r.Poll(Sample(1, buttons: Right), Sample(2, buttons: Left)).Single().Device.Id == Id(2));
            r.Now = 520; Check(r.Poll(Sample(1, buttons: Right), Sample(2, buttons: Left)).Single().Device.Id == Id(1));
        });
        Test("independent stick hysteresis and opposite-direction switch", () => {
            var r = new Rig();
            var initial = r.Poll(Sample(1, x: .70f, y: .68f), Sample(2, y: .8f));
            Check(initial[0].Action == ControllerAction.NavigateRight && initial[1].Action == ControllerAction.NavigateUp);
            r.Now = 400;
            Check(r.Poll(Sample(1, 2, x: .68f, y: .72f), Sample(2, 2)).Single().Action == ControllerAction.NavigateRight);
            r.Now = 401;
            var changed = r.Poll(Sample(1, 3, x: -.8f), Sample(2, 3, x: .8f));
            Check(changed.Count == 2 && changed[0].Action == ControllerAction.NavigateLeft && !changed[0].IsRepeat);
        });
        Test("independent right-trigger edge and threshold", () => {
            var r = new Rig();
            Check(r.Poll(Sample(1, trigger: .6f), Sample(2)).Single().Action == ControllerAction.RightTrigger);
            Check(r.Poll(Sample(1, 2, trigger: .8f), Sample(2, 2, trigger: .6f)).Single().Device.Id == Id(2));
            Check(r.Poll(Sample(1, 3, trigger: .59f), Sample(2, 3, trigger: .6f)).Count == 0);
            Check(r.Poll(Sample(1, 4, trigger: .6f), Sample(2, 4, trigger: .6f)).Single().Device.Id == Id(1));
        });
        Test("disconnect clears only its device and reconnect requires neutral", () => {
            var r = new Rig(); var changes = new List<ControllerConnectionEvent>();
            r.Service.DeviceConnectionChanged += changes.Add;
            r.Poll(Sample(1, buttons: A), Sample(2));
            Check(r.Poll(Sample(1, 2, connected: false), Sample(2, 2, A)).Single().Device.Id == Id(2));
            Check(r.Service.IsConnected && r.Service.Devices.Count == 1);
            Check(r.Poll(Sample(1, 3, A, generation: 2), Sample(2, 3, A)).Count == 0);
            Check(r.Poll(Sample(1, 4, generation: 2, x: .5f), Sample(2, 4)).Count == 0);
            Check(r.Poll(Sample(1, 5, generation: 2), Sample(2, 5)).Count == 0);
            var action = r.Poll(Sample(1, 6, A, generation: 2), Sample(2, 6)).Single();
            Check(action.Device.Id == Id(1) && action.Device.ConnectionGeneration == 2);
            Check(changes.Count(c => c.Device.Id == Id(1) && !c.Connected) == 1);
        });
        Test("reconnect cannot rearm from unavailable or transient tap sample", () => {
            var r = new Rig(); r.Poll(Sample(1));
            r.Poll(Sample(1, 2, connected: false));
            Check(r.Poll(Sample(1, 3, generation: 2, available: false)).Count == 0);
            Check(r.Poll(Sample(1, 4, generation: 2, pressed: A)).Count == 0);
            Check(r.Poll(Sample(1, 5, A, generation: 2)).Count == 0);
            Check(r.Poll(Sample(1, 6, generation: 2)).Count == 0);
            Check(r.Poll(Sample(1, 7, A, generation: 2)).Count == 1);
        });
        Test("retired generation and timestamp never replay input", () => {
            var r = new Rig(); r.Poll(Sample(1));
            r.Poll(Sample(1, 10, generation: 2));
            Check(r.Poll(Sample(1, 11, A, generation: 2)).Count == 1);
            Check(r.Poll(Sample(1, 99, B, generation: 1)).Count == 0);
            Check(r.Poll(Sample(1, 9, B, generation: 2)).Count == 0);
            Check(r.Service.Devices.Single().ConnectionGeneration == 2);
        });
        Test("disconnect arriving after snapshot cannot resurrect same generation", () => {
            var r = new Rig(); r.Poll(Sample(1));
            var disconnected = Sample(1, 2, connected: false);
            r.Source.Next = new(new[] { Sample(1, 1, A) }, new[] {
                new ControllerCaptureEventV2 { Kind = ControllerCaptureEventKindV2.Disconnected, Sequence = 5, Device = disconnected }
            });
            Check(r.Service.PollActions().Count == 0 && !r.Service.IsConnected);
            Check(r.Poll(Sample(1, 3, A)).Count == 0);
        });
        Test("rapid disconnect/reconnect between polls resets state", () => {
            var r = new Rig(); r.Poll(Sample(1, buttons: A));
            r.Source.Next = new(new[] { Sample(1, 4, A, generation: 2) }, new[] {
                new ControllerCaptureEventV2 { Kind = ControllerCaptureEventKindV2.Disconnected, Sequence = 2, Device = Sample(1, 2, connected: false) },
                new ControllerCaptureEventV2 { Kind = ControllerCaptureEventKindV2.Connected, Sequence = 3, Device = Sample(1, 3, generation: 2) }
            });
            Check(r.Service.PollActions().Count == 0);
            r.Poll(Sample(1, 5, generation: 2));
            Check(r.Poll(Sample(1, 6, A, generation: 2)).Single().Action == ControllerAction.Accept);
        });
        Test("unavailable reading is not disconnect and cannot repeat stale input", () => {
            var r = new Rig(); r.Poll(Sample(1, buttons: Right)); r.Now = 1000;
            Check(r.Poll(Sample(1, 2, available: false)).Count == 0 && r.Service.IsConnected);
            var resumed = r.Poll(Sample(1, 3, Right)).Single();
            Check(!resumed.IsRepeat);
        });
        Test("background Guide remains attributed without ordinary reading", () => {
            var r = new Rig();
            var a = r.Poll(Sample(1, available: false, guide: 1), Sample(2, available: false)).Single();
            Check(a.Action == ControllerAction.Guide && a.Device.Id == Id(1));
            var b = r.Poll(Sample(1, available: false), Sample(2, available: false, guide: 1)).Single();
            Check(b.Device.Id == Id(2) && b.Device.Family == ControllerFamily.PlayStation);
        });
        Test("brief native tap flags and repeated timestamp suppression", () => {
            var r = new Rig();
            Check(r.Poll(Sample(1, pressed: A)).Single().Action == ControllerAction.Accept);
            Check(r.Poll(Sample(1, pressed: A)).Count == 0);
            Check(r.Poll(Sample(1, 2, pressed: A)).Single().Action == ControllerAction.Accept);
        });
        Test("history recovery retains observable fresh press", () => {
            var r = new Rig(); r.Poll(Sample(1));
            var recovered = Sample(1, 2, A, pressed: A); recovered.HistoryDiscontinuity = 1;
            Check(r.Poll(recovered).Single().Action == ControllerAction.Accept);
        });
        Test("short D-pad tap and per-device short stick-flick projection", () => {
            var r = new Rig();
            Check(r.Poll(Sample(1, pressed: Right), Sample(2)).Single().Action == ControllerAction.NavigateRight);
            r.Now = 1000;
            Check(r.Poll(Sample(1, pressed: Right), Sample(2)).Count == 0, "an old transient D-pad bit cannot repeat");
            r.Poll(Sample(1, 2), Sample(2, 2));
            r.Source.Next = new(new[] { Sample(1, 3), Sample(2, 3) }, Array.Empty<ControllerCaptureEventV2>(),
                NavigationProjections: new Dictionary<ControllerDeviceId, ControllerState> {
                    [Id(1)] = new() { LeftThumbstickX = -.8f }, [Id(2)] = new() { LeftThumbstickY = .9f }
                });
            var flicks = r.Service.PollActions();
            Check(flicks.Count == 2 && flicks[0].Device.Id == Id(1) && flicks[0].Action == ControllerAction.NavigateLeft);
            Check(flicks[1].Device.Id == Id(2) && flicks[1].Action == ControllerAction.NavigateUp);
            r.Now = 2000;
            Check(r.Service.PollActions().Count == 0, "replaying the same projected sample cannot repeat a flick");
            Check(r.Poll(Sample(1, 4), Sample(2, 4)).Count == 0, "a released flick cannot become a held repeat");
        });
        Test("identical-model controllers do not share input state", () => {
            var r = new Rig(); var a = Sample(1, buttons: A); var b = Sample(2, buttons: A);
            b.VendorId = a.VendorId; b.ProductId = a.ProductId;
            var actions = r.Poll(a, b);
            Check(actions.Count == 2 && actions.All(e => e.Device.Family == ControllerFamily.Xbox));
            Check(actions[0].Device.Id != actions[1].Device.Id && r.Service.Devices.Count == 2);
        });
        Test("resync and native queue loss require a release boundary", () => {
            var r = new Rig(); r.Poll(Sample(1, buttons: A));
            r.Service.ResyncInputState();
            Check(r.Poll(Sample(1, 2, A)).Count == 0);
            r.Poll(Sample(1, 3));
            Check(r.Poll(Sample(1, 4, A)).Count == 1);
            r.Source.Next = new(new[] { Sample(1, 5, B) }, Array.Empty<ControllerCaptureEventV2>(), true);
            Check(r.Service.PollActions().Count == 0);
            Check(r.Poll(Sample(1, 6, B)).Count == 0);
            r.Poll(Sample(1, 7));
            Check(r.Poll(Sample(1, 8, B)).Single().Action == ControllerAction.Back);
        });
        Test("legacy adapter pins endpoint, publishes other actions, preserves Guide priority", () => {
            var r = new Rig(); var published = new List<ControllerActionEvent>();
            r.Service.ActionReceived += published.Add;
            r.Source.Next = new(new[] { Sample(2, buttons: A), Sample(1) }, Array.Empty<ControllerCaptureEventV2>());
            Check(r.Service.PollAction() == ControllerAction.none && published.Single().Device.Id == Id(2));
            Check(r.Service.Family == ControllerFamily.Xbox);
            r.Source.Next = new(new[] { Sample(1, 2, A), Sample(2, 2, guide: 1) }, Array.Empty<ControllerCaptureEventV2>());
            Check(r.Service.PollAction() == ControllerAction.Guide);
            r.Source.Next = new(new[] { Sample(1, 3, connected: false), Sample(2, 3, A) }, Array.Empty<ControllerCaptureEventV2>());
            Check(r.Service.PollAction() == ControllerAction.Accept && r.Service.Family == ControllerFamily.PlayStation);
        });
        Test("existing one-shot mappings, navigation priority, and reentrancy", () => {
            var r = new Rig();
            uint all = A | B | (uint)(ControllerButtons.X | ControllerButtons.Y | ControllerButtons.Menu |
                ControllerButtons.View | ControllerButtons.LeftShoulder | ControllerButtons.RightShoulder);
            var actions = r.Poll(Sample(1, buttons: all | Right, trigger: .7f, guide: 1));
            Check(actions.Select(a => a.Action).SequenceEqual(new[] {
                ControllerAction.Guide, ControllerAction.Accept, ControllerAction.Back, ControllerAction.X,
                ControllerAction.Y, ControllerAction.Menu, ControllerAction.View, ControllerAction.PreviousTab,
                ControllerAction.NextTab, ControllerAction.RightTrigger
            }));
            bool rejected = false;
            r.Service.ActionReceived += _ => {
                try { r.Service.PollActions(); } catch (InvalidOperationException) { rejected = true; }
            };
            r.Poll(Sample(1, 2, Right));
            Check(rejected);
        });
        Test("unattributed legacy Guide fallback never fabricates an action identity", () => {
            var r = new Rig(); var published = new List<ControllerActionEvent>();
            r.Service.ActionReceived += published.Add;
            r.Source.Next = new(new[] { Sample(1) }, Array.Empty<ControllerCaptureEventV2>(), LegacyGuideRequested: true);
            Check(r.Service.PollAction() == ControllerAction.Guide && published.Count == 0);
            r.Source.Next = new(new[] { Sample(1, 2) }, Array.Empty<ControllerCaptureEventV2>());
            Check(r.Service.PollAction() == ControllerAction.none, "legacy Guide cannot be replayed next poll");
        });
        int ownership = OwnershipTests.Run();
        int navigation = NavigationRegressionTests.Run();
        int phase0 = KeyboardPhase0Tests.Run();
        int keyboard = KeyboardProtocolTests.Run().GetAwaiter().GetResult();
        Console.WriteLine($"PASS: {_passed} managed input groups + {ownership} ownership groups + {navigation} navigation regression groups + {phase0} Phase 0 probe groups + {keyboard} keyboard protocol/session groups; no hardware required.");
    }

    private static void NativeSmoke() {
        Check(ControllerInputNative.ControllerInput_Initialize(), "native initialization");
        try {
            var source = new NativeControllerInputSource();
            var service = new ControllerService(source);
            for (int i = 0; i < 3; ++i) {
                service.PollActions();
                Check(source.Diagnostics.GameInputApiVersion == 3);
                Console.WriteLine($"native poll {i + 1}: devices={service.Devices.Count}, drops={source.Diagnostics.DroppedEvents}");
                foreach (var device in service.Devices)
                {
                    Console.WriteLine($"id={device.Id} root={device.RootId} generation={device.ConnectionGeneration} family={device.Family}");
                    var id = device.Id;
                    int projectionResult = ControllerInputNative.ControllerInput_CopyNavigationProjectionV2(in id, device.ConnectionGeneration, out _);
                    Check(projectionResult == 0 || projectionResult == unchecked((int)0x838A0003), "attributed navigation projection query");
                    Check(ControllerInputNative.ControllerInput_CopyNavigationProjectionV2(in id, device.ConnectionGeneration + 1, out _) == unchecked((int)0x838A0001),
                        "projection cannot cross connection generation");
                }
                Thread.Sleep(50);
            }
            Console.WriteLine("PASS managed/native ABI v2 polling and lifecycle interop");
        } finally { ControllerInputNative.ControllerInput_Shutdown(); }
    }

    private static int Main(string[] args) {
        try {
            if (args.Contains("--native-smoke")) NativeSmoke();
            else if (args.Contains("--keyboard-protocol-tests")) Console.WriteLine($"PASS: {KeyboardProtocolTests.Run().GetAwaiter().GetResult()} keyboard protocol/session groups.");
            else if (args.Contains("--ownership-monitor")) ControllerMonitor.Run(ownership: true,
                seconds: args.Length > 1 && int.TryParse(args[1], out int duration) ? duration : 120);
            else if (args.Contains("--monitor")) ControllerMonitor.Run();
            else ManagedTests();
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
