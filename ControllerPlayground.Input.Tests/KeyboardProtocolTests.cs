using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.IO.Pipes;
using ControllerPlayground.Input;
using ControllerPlayground.Input.Keyboard;

internal static class KeyboardProtocolTests {
    private static void Check(bool condition, string reason = "keyboard protocol assertion failed") {
        if (!condition) throw new InvalidOperationException(reason);
    }
    private static async Task Throws<T>(Func<Task> body) where T : Exception {
        try { await body(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
    private static void Throws<T>(Action body) where T : Exception {
        try { body(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
    private sealed class Clock : TimeProvider {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    private sealed class Rig {
        public readonly KeyboardConnectionIdentity Connection = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public readonly Guid SessionId = Guid.NewGuid();
        public readonly KeyboardDeviceRef ShellOwner = new(Guid.NewGuid(), Program.Id(1).ToString(), Program.Id(1).ToString(), 7, ControllerFamily.Xbox);
        // Deliberately different application-local ID AND generation.
        public readonly KeyboardDeviceRef CaptureOwner = new(Guid.NewGuid(), Program.Id(3).ToString(), Program.Id(3).ToString(), 2, ControllerFamily.Xbox);
        public KeyboardDeviceRef Other => CaptureOwner with { DeviceId = Program.Id(2).ToString(), Family = ControllerFamily.PlayStation };
        public readonly Clock Time = new();
        public readonly KeyboardSession Session;
        public readonly List<KeyboardEnvelope> History = new();
        public KeyboardOwnerBinding Binding;
        public ulong ShellSequence, HostSequence, CaptureSequence;
        public Rig(KeyboardPeer peer = KeyboardPeer.Host) {
            Session = new(Connection, SessionId, peer, Time);
            Binding = new(Guid.NewGuid(), ShellOwner, CaptureOwner);
        }
        public KeyboardEnvelope Envelope(KeyboardMessage message, ulong? epoch = null, KeyboardPeer? sender = null) {
            var peer = sender ?? (message is KeyboardOpen or KeyboardSetOwner or KeyboardExecute or KeyboardSuspend or KeyboardClose ? KeyboardPeer.Shell : KeyboardPeer.Host);
            return new(new(2, Connection.ConnectionId, Connection.ShellInstanceId, Connection.HostInstanceId, SessionId,
                peer, epoch ?? Math.Max(1, Session.OwnershipEpoch), peer == KeyboardPeer.Shell ? ++ShellSequence : ++HostSequence), message);
        }
        public KeyboardSessionDecision Send(KeyboardMessage message, ulong? epoch = null) {
            var envelope = Envelope(message, epoch); History.Add(envelope); return Session.Apply(envelope);
        }
        public void Open() {
            Check(Send(new KeyboardOpen("initial\ttext\nΩ😀", ShellOwner)).Disposition == KeyboardDisposition.Accepted);
            Check(Send(new KeyboardReady(1234, 10, CaptureOwner.CollectorId, ImmutableArray.Create(CaptureOwner, Other))).Disposition == KeyboardDisposition.Accepted);
        }
        public void Activate() {
            Open(); Assign(Binding); Arm();
        }
        public void Assign(KeyboardOwnerBinding binding) {
            Binding = binding;
            Check(Send(new KeyboardSetOwner(binding), Session.OwnershipEpoch + 1).Disposition == KeyboardDisposition.Accepted);
        }
        public void Arm() => Check(Send(new KeyboardOwnerReady(Binding.BindingId, true, true, CaptureSequence)).Disposition == KeyboardDisposition.Accepted);
        public KeyboardCapturedAction Input(ControllerAction action = ControllerAction.Accept, KeyboardDeviceRef? device = null, bool repeat = false) =>
            new(device ?? CaptureOwner, ++CaptureSequence, action, repeat, 123456, 200);
        public KeyboardExecutionTicket Issue(KeyboardCapturedAction input, ulong command = 1) {
            Check(Send(new KeyboardCaptureBatch(CaptureOwner.CollectorId, true, ImmutableArray.Create(input))).Proposals.Length == 1);
            return Send(new KeyboardExecute(command, input)).Execution ?? throw new InvalidOperationException("Missing execution ticket");
        }
    }
    private sealed class FragmentedStream(byte[] bytes, int chunk = 1) : MemoryStream(bytes) {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }
    private sealed class YieldingStream : MemoryStream {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            while (!buffer.IsEmpty) {
                int size = Math.Min(7, buffer.Length);
                await base.WriteAsync(buffer[..size], cancellationToken); buffer = buffer[size..]; await Task.Yield();
            }
        }
    }
    private sealed class PrefixThenCancel : Stream {
        private bool _prefix;
        public readonly TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
            if (!_prefix) { _prefix = true; new byte[] { 10, 0, 0, 0 }.CopyTo(buffer); return 4; }
            Waiting.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
            Waiting.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private static byte[] Frame(string json) {
        byte[] body = Encoding.UTF8.GetBytes(json), frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length); body.CopyTo(frame, 4); return frame;
    }
    private static string Json(KeyboardEnvelope envelope) => Encoding.UTF8.GetString(KeyboardFramedStream.Encode(envelope).AsSpan(4));

    public static async Task<int> Run() {
        int count = 0;
        async Task Test(string name, Func<Task> body) { await body(); ++count; Console.WriteLine($"PASS keyboard: {name}"); }
        async Task Sync(string name, Action body) => await Test(name, () => { body(); return Task.CompletedTask; });
        await Test("all v2 session records survive fragmented framing with opaque IDs and exact Unicode text", async () => {
            var r = new Rig(); var input = r.Input();
            KeyboardMessage[] messages = [new KeyboardOpen("\0\t\r\nΩ😀\\\"", r.ShellOwner),
                new KeyboardReady(123, 4, r.CaptureOwner.CollectorId, ImmutableArray.Create(r.CaptureOwner)),
                new KeyboardSetOwner(r.Binding), new KeyboardOwnerReady(r.Binding.BindingId, true, true, 0),
                new KeyboardDevicesChanged(r.CaptureOwner.CollectorId, 1, ImmutableArray.Create(r.CaptureOwner)),
                new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(input)),
                new KeyboardExecute(1, input), new KeyboardExecuted(1), new KeyboardSuspend(KeyboardSuspendReason.FocusLost),
                new KeyboardDraftSnapshot(1, "draft\tΩ😀"), new KeyboardCommit("commit"), new KeyboardCancel(),
                new KeyboardClose(KeyboardCloseReason.Finished), new KeyboardClosed(), new KeyboardPing(1), new KeyboardPong(1),
                new KeyboardError(KeyboardErrorCode.InvalidState)];
            foreach (var message in messages) {
                var envelope = r.Envelope(message);
                byte[] encoded = KeyboardFramedStream.Encode(envelope);
                await using var wire = new KeyboardFramedStream(new FragmentedStream(encoded));
                var decoded = await wire.ReadAsync();
                Check(decoded?.Header == envelope.Header && encoded.SequenceEqual(KeyboardFramedStream.Encode(decoded!)));
                if (decoded!.Message is KeyboardOpen open) Check(open.InitialText == "\0\t\r\nΩ😀\\\"");
                if (decoded.Message is KeyboardSetOwner owner) Check(owner.Binding.CaptureDevice.DeviceId == Program.Id(3).ToString());
            }
        });
        await Sync("version/capability negotiation is correlated to both process lifetimes", () => {
            var r = new Rig();
            var h = r.Envelope(new KeyboardPing(1)).Header with { SessionId = Guid.Empty, OwnershipEpoch = 0, Sender = KeyboardPeer.Shell, HostInstanceId = Guid.Empty };
            var hello = new KeyboardEnvelope(h, new KeyboardHello(2, 3, KeyboardProtocol.RequiredCapabilities));
            var ack = new KeyboardEnvelope(h with { Sender = KeyboardPeer.Host, HostInstanceId = r.Connection.HostInstanceId },
                new KeyboardHelloAck(2, KeyboardProtocol.RequiredCapabilities));
            Check(KeyboardConnectionIdentity.Negotiate(hello, ack) == r.Connection);
            Throws<KeyboardProtocolException>(() => KeyboardConnectionIdentity.Negotiate(hello,
                ack with { Header = ack.Header with { ConnectionId = Guid.NewGuid() } }));
            Throws<KeyboardProtocolException>(() => KeyboardFramedStream.Encode(ack with { Message = new KeyboardHelloAck(3, KeyboardProtocol.RequiredCapabilities) }));
            Throws<KeyboardProtocolException>(() => KeyboardFramedStream.Encode(hello with { Message = new KeyboardHello(3, 3, KeyboardProtocol.RequiredCapabilities) }));
            Throws<KeyboardProtocolException>(() => KeyboardFramedStream.Encode(ack with { Message = new KeyboardHelloAck(2, KeyboardCapabilities.None) }));
        });
        await Test("coalesced frames and concurrent fragmented writes remain separate messages", async () => {
            var r = new Rig(); using var buffer = new YieldingStream();
            await using var wire = new KeyboardFramedStream(buffer, leaveOpen: true);
            var frames = Enumerable.Range(1, 12).Select(i => r.Envelope(new KeyboardPing((ulong)i))).ToArray();
            await Task.WhenAll(frames.Select(e => wire.WriteAsync(e).AsTask())); buffer.Position = 0;
            var nonces = new HashSet<ulong>();
            for (int i = 0; i < frames.Length; ++i) nonces.Add(((KeyboardPing)(await wire.ReadAsync())!.Message).Nonce);
            Check(nonces.Count == 12); Check(await wire.ReadAsync() == null && !wire.IsUsable);
        });
        await Test("zero, huge and truncated lengths fail before payload allocation or reuse", async () => {
            foreach (uint length in new uint[] { 0, KeyboardProtocol.MaxFrameBytes + 1, uint.MaxValue }) {
                byte[] prefix = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(prefix, length);
                using var buffer = new MemoryStream(prefix); await using var wire = new KeyboardFramedStream(buffer, true);
                await Throws<KeyboardProtocolException>(async () => await wire.ReadAsync());
                Check(buffer.Position == 4 && !wire.IsUsable);
            }
            var valid = KeyboardFramedStream.Encode(new Rig().Envelope(new KeyboardPing(1)));
            foreach (var bytes in new[] { valid[..1], valid[..3], valid[..^1] }) {
                await using var wire = new KeyboardFramedStream(new FragmentedStream(bytes));
                await Throws<KeyboardProtocolException>(async () => await wire.ReadAsync()); Check(!wire.IsUsable);
                await Throws<KeyboardProtocolException>(async () => await wire.ReadAsync());
            }
        });
        await Test("strict JSON rejects unknown type/version/fields, duplicates, missing fields and invalid UTF8", async () => {
            string valid = Json(new Rig().Envelope(new KeyboardCommit("PRIVATE_TEXT")));
            foreach (var bytes in new[] { Frame(valid.Replace("\"version\":2", "\"version\":99")),
                Frame(valid.Replace("\"type\":\"commit\"", "\"type\":\"future\"")),
                Frame(valid.Replace("\"version\":2", "\"version\":2,\"version\":2")),
                Frame(valid.Replace("\"version\":2,", "")), Frame(valid.Replace("\"text\":", "\"unexpected\":1,\"text\":")),
                new byte[] { 1, 0, 0, 0, 255 }, Frame("null") }) {
                await using var wire = new KeyboardFramedStream(new MemoryStream(bytes));
                try { await wire.ReadAsync(); throw new InvalidOperationException("Expected bad payload"); }
                catch (KeyboardProtocolException error) { Check(!error.Message.Contains("PRIVATE_TEXT") && !wire.IsUsable); }
            }
        });
        await Test("managed action/family enum ordinals and invalid Unicode escapes cannot enter the wire", async () => {
            var r = new Rig(); string json = Json(r.Envelope(new KeyboardExecute(1, r.Input(ControllerAction.Back))));
            Check(json.Contains("\"action\":\"Back\"") && json.Contains("\"family\":\"Xbox\""));
            foreach (string bad in new[] { json.Replace("\"action\":\"Back\"", "\"action\":6"),
                json.Replace("\"action\":\"Back\"", "\"action\":\"6\""), json.Replace("\"family\":\"Xbox\"", "\"family\":1"),
                Json(r.Envelope(new KeyboardCommit("marker"))).Replace("marker", "\\uD800") }) {
                await using var wire = new KeyboardFramedStream(new MemoryStream(Frame(bad)));
                await Throws<KeyboardProtocolException>(async () => await wire.ReadAsync());
            }
        });
        await Sync("schema bounds text, inventory, action batches, identities and enum values", () => {
            var r = new Rig(); var packet = r.Envelope(new KeyboardOpen(new string('a', KeyboardProtocol.MaxTextCodeUnits), r.ShellOwner));
            Check(KeyboardFramedStream.Encode(packet).Length <= KeyboardProtocol.MaxFrameBytes + 4);
            foreach (var message in new KeyboardMessage[] { new KeyboardOpen(new string('a', KeyboardProtocol.MaxTextCodeUnits + 1), null),
                new KeyboardOpen("\ud800", null), new KeyboardSetOwner(r.Binding with { CaptureDevice = r.CaptureOwner with { DeviceId = "045E" } }),
                new KeyboardReady(1, 1, r.CaptureOwner.CollectorId, ImmutableArray.Create(r.CaptureOwner, r.CaptureOwner)),
                new KeyboardReady(1, 1, r.CaptureOwner.CollectorId, ImmutableArray<KeyboardDeviceRef>.Empty.AddRange(Enumerable.Repeat(r.CaptureOwner, 33))),
                new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.CreateRange(Enumerable.Range(0,65).Select(_ => r.Input()))),
                new KeyboardExecute(1, r.Input() with { Action = (ControllerAction)999 }), new KeyboardExecuted(0) })
                Throws<KeyboardProtocolException>(() => KeyboardFramedStream.Encode(r.Envelope(message)));
            Throws<KeyboardProtocolException>(() => KeyboardFramedStream.Encode(packet with { Header = packet.Header with { Sender = KeyboardPeer.Host } }));
        });
        await Test("pre-cancel leaves framing reusable; partial read/write cancellation poisons it", async () => {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await using (var usable = new KeyboardFramedStream(new MemoryStream())) {
                await Throws<OperationCanceledException>(async () => await usable.ReadAsync(cancelled.Token)); Check(usable.IsUsable);
            }
            foreach (bool read in new[] { true, false }) {
                using var stream = new PrefixThenCancel(); await using var wire = new KeyboardFramedStream(stream);
                using var cancel = new CancellationTokenSource();
                var task = read ? wire.ReadAsync(cancel.Token).AsTask() : AsRead(wire.WriteAsync(new Rig().Envelope(new KeyboardPing(1)), cancel.Token));
                await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
                await Throws<OperationCanceledException>(async () => await task); Check(!wire.IsUsable);
            }
        });
        await Sync("open/ready/assignment/neutral produce an active lease without equating IDs or generations", () => {
            var r = new Rig(); Check(r.Session.Phase == KeyboardSessionPhase.Opening); r.Open();
            Check(r.Session.Phase == KeyboardSessionPhase.AwaitingNeutral);
            r.Assign(r.Binding);
            Check(r.Send(new KeyboardOwnerReady(r.Binding.BindingId, false, true, 0)).Disposition == KeyboardDisposition.Discarded);
            Check(r.Send(new KeyboardOwnerReady(r.Binding.BindingId, true, false, 0)).Disposition == KeyboardDisposition.Discarded);
            r.Arm(); Check(r.Session.Phase == KeyboardSessionPhase.Active && r.Session.Owner == r.Binding);
            Check(r.Session.Owner!.ShellDevice.Generation != r.Session.Owner.CaptureDevice.Generation);
        });
        await Sync("non-owner input and Guide/View are dropped rather than retained for later commands", () => {
            var r = new Rig(); r.Activate();
            var other = r.Input(device:r.Other); var guide = r.Input(ControllerAction.Guide); var view = r.Input(ControllerAction.View); var good = r.Input();
            var decision = r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(other, guide, view, good)));
            Check(decision.Proposals.SequenceEqual(new[] { good }) && r.Session.PendingWork == 1);
            Check(r.Send(new KeyboardExecute(1, other)).Disposition == KeyboardDisposition.Suspended);
            Check(r.Session.PendingWork == 0);
        });
        await Sync("commands require the exact captured action and execute/acknowledge at most once", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            Check(r.Session.TryBeginExecution(ticket)); Check(!r.Session.TryBeginExecution(ticket));
            Check(r.Send(new KeyboardExecuted(1)).Disposition == KeyboardDisposition.Accepted);
            Check(r.Send(new KeyboardExecuted(1)).Disposition == KeyboardDisposition.Discarded);
            Check(!r.Session.TryBeginExecution(ticket));
            Check(r.Send(new KeyboardExecute(1, ticket.SourceAction)).Disposition == KeyboardDisposition.Discarded);
        });
        await Sync("an invented Accept cannot replace a captured Back", () => {
            var r = new Rig(); r.Activate(); var back = r.Input(ControllerAction.Back);
            r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(back)));
            Check(r.Send(new KeyboardExecute(1, back with { Action = ControllerAction.Accept })).Disposition == KeyboardDisposition.Suspended);
            Check(r.Session.PendingWork == 0);
        });
        await Sync("source actions and delayed execution callbacks cannot reorder text mutations", () => {
            var r = new Rig(); r.Activate(); var first = r.Issue(r.Input(ControllerAction.RightTrigger), 1);
            var second = r.Issue(r.Input(ControllerAction.Accept), 2);
            Check(!r.Session.TryBeginExecution(second) && !r.Session.TryBeginExecution(first));
            Check(r.Session.Phase == KeyboardSessionPhase.Suspended);
            r.Assign(r.Binding with { BindingId = Guid.NewGuid() }); r.Arm();
            Check(r.Session.TryBeginExecution(r.Issue(r.Input(), 3)));
            var order = new Rig(); order.Activate(); var a = order.Input(ControllerAction.X); var b = order.Input(ControllerAction.Y);
            order.Send(new KeyboardCaptureBatch(order.CaptureOwner.CollectorId, true, ImmutableArray.Create(a, b)));
            Check(order.Send(new KeyboardExecute(1, b)).Disposition == KeyboardDisposition.Suspended);
        });
        await Sync("delayed UI callbacks are revoked by owner change and need another neutral acknowledgement", () => {
            var r = new Rig(); r.Activate(); var stale = r.Issue(r.Input());
            var binding = new KeyboardOwnerBinding(Guid.NewGuid(), r.ShellOwner with { DeviceId = r.Other.DeviceId }, r.Other);
            r.Assign(binding); Check(!r.Session.TryBeginExecution(stale));
            Check(r.Session.Phase == KeyboardSessionPhase.AwaitingNeutral);
            r.Arm(); Check(r.Session.Phase == KeyboardSessionPhase.Active && r.Session.Owner == binding);
            var input = r.Input(device:r.Other); var ticket = r.Issue(input, 2); Check(r.Session.TryBeginExecution(ticket));
        });
        await Sync("disconnect/reconnect generations cannot regain an old owner or revive a ticket", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            Check(r.Send(new KeyboardDevicesChanged(r.CaptureOwner.CollectorId, 1, ImmutableArray.Create(r.Other))).Disposition == KeyboardDisposition.Suspended);
            Check(!r.Session.TryBeginExecution(ticket));
            var next = r.CaptureOwner with { Generation = 3 };
            r.Send(new KeyboardDevicesChanged(r.CaptureOwner.CollectorId, 2, ImmutableArray.Create(next, r.Other)));
            Check(r.Send(new KeyboardOwnerReady(r.Binding.BindingId, true, true, r.CaptureSequence)).Disposition == KeyboardDisposition.Discarded);
            r.Assign(new(Guid.NewGuid(), r.ShellOwner with { Generation = 8 }, next)); r.Arm();
            Check(r.Session.Phase == KeyboardSessionPhase.Active && !r.Session.TryBeginExecution(ticket));
        });
        await Sync("same-ID generation and collector mismatch cannot authorize input", () => {
            var r = new Rig(); r.Activate(); var input = r.Input(device:r.CaptureOwner with { Generation = 99 });
            Check(r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(input))).Proposals.IsEmpty);
            Check(r.Send(new KeyboardCaptureBatch(Guid.NewGuid(), true, ImmutableArray<KeyboardCapturedAction>.Empty)).Disposition == KeyboardDisposition.Suspended);
        });
        await Sync("old lifetime and duplicate messages cannot poison sequence state", () => {
            var r = new Rig(); r.Activate(); var valid = r.Envelope(new KeyboardPing(1));
            foreach (var h in new[] { valid.Header with { SessionId = Guid.NewGuid() }, valid.Header with { HostInstanceId = Guid.NewGuid() },
                valid.Header with { ShellInstanceId = Guid.NewGuid() }, valid.Header with { ConnectionId = Guid.NewGuid() } })
                Check(r.Session.Apply(valid with { Header = h }).Disposition == KeyboardDisposition.Discarded);
            Check(r.Session.Apply(valid).Disposition == KeyboardDisposition.Accepted);
            Check(r.Session.Apply(valid).Disposition == KeyboardDisposition.Discarded && r.Session.Phase == KeyboardSessionPhase.Active);
        });
        await Sync("sequence gaps suspend; a fresh owner epoch can recover without replay", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            var gap = r.Envelope(new KeyboardPing(1)); r.HostSequence += 1;
            Check(r.Session.Apply(gap with { Header = gap.Header with { Sequence = r.HostSequence } }).Disposition == KeyboardDisposition.Suspended);
            Check(!r.Session.TryBeginExecution(ticket));
            r.Assign(r.Binding with { BindingId = Guid.NewGuid() }); r.Arm();
            Check(r.Session.Phase == KeyboardSessionPhase.Active && r.Session.PendingWork == 0);
        });
        await Sync("stale epoch messages are consumed without rearming or forcing a later sequence gap", () => {
            var r = new Rig(); r.Activate(); ulong old = r.Session.OwnershipEpoch;
            r.Send(new KeyboardSuspend(KeyboardSuspendReason.FocusLost), old + 1);
            Check(r.Send(new KeyboardCommit("STALE"), old).Disposition == KeyboardDisposition.Discarded);
            r.Assign(r.Binding with { BindingId = Guid.NewGuid() }); r.Arm();
            Check(r.Session.Result == null && r.Session.Phase == KeyboardSessionPhase.Active);
            Check(r.Send(new KeyboardOwnerReady(r.Binding.BindingId, true, true, 0), r.Session.OwnershipEpoch + 1).Disposition == KeyboardDisposition.Suspended);
        });
        await Sync("empty background capture suspends immediately and cannot be undone by a late owner-ready", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            Check(r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, false, ImmutableArray<KeyboardCapturedAction>.Empty)).Disposition == KeyboardDisposition.Suspended);
            Check(!r.Session.TryBeginExecution(ticket));
            Check(r.Send(new KeyboardOwnerReady(r.Binding.BindingId, true, true, r.CaptureSequence)).Disposition == KeyboardDisposition.Discarded);
        });
        await Sync("bounded pending work suspends instead of retaining an input backlog", () => {
            var r = new Rig(); r.Activate();
            var full = Enumerable.Range(0, KeyboardProtocol.MaxPendingCommands).Select(_ => r.Input()).ToImmutableArray();
            Check(r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, full)).Proposals.Length == 64);
            Check(r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(r.Input()))).Disposition == KeyboardDisposition.Suspended);
            Check(r.Session.PendingWork == 0);
        });
        await Sync("proposal and execution deadlines reject UI-stall input using local monotonic time", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input()); r.Time.Milliseconds = 251;
            Check(!r.Session.TryBeginExecution(ticket) && r.Session.Phase == KeyboardSessionPhase.Suspended);
            var other = new Rig(); other.Activate(); var action = other.Input();
            other.Send(new KeyboardCaptureBatch(other.CaptureOwner.CollectorId, true, ImmutableArray.Create(action)));
            other.Time.Milliseconds = 251;
            Check(other.Send(new KeyboardExecute(1, action)).Disposition == KeyboardDisposition.Suspended);
        });
        await Sync("commit seals exactly one result and revokes all queued commands", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            Check(r.Send(new KeyboardCommit("Ω😀\t\r\n")).Disposition == KeyboardDisposition.Accepted);
            Check(!r.Session.TryBeginExecution(ticket));
            Check(r.Send(new KeyboardCancel()).Disposition == KeyboardDisposition.Discarded);
            Check(r.Send(new KeyboardCommit("late")).Disposition == KeyboardDisposition.Discarded);
            Check(r.Session.Result == new KeyboardSessionResult(true, "Ω😀\t\r\n"));
            r.Send(new KeyboardClose(KeyboardCloseReason.Finished), r.Session.OwnershipEpoch + 1);
            Check(r.Send(new KeyboardClosed()).Disposition == KeyboardDisposition.Accepted);
            Check(r.Session.Phase == KeyboardSessionPhase.Closed);
        });
        await Sync("cancel, draft revisions and transport loss never implicitly commit partial text", () => {
            var r = new Rig(); r.Open();
            Check(r.Send(new KeyboardDraftSnapshot(2, "draft")).Disposition == KeyboardDisposition.Accepted);
            Check(r.Send(new KeyboardDraftSnapshot(1, "stale")).Disposition == KeyboardDisposition.Discarded);
            Check(r.Session.LatestDraft == "draft");
            r.Send(new KeyboardCancel()); Check(r.Session.Result == new KeyboardSessionResult(false, null));
            var other = new Rig(); other.Activate(); var ticket = other.Issue(other.Input());
            other.Send(new KeyboardDraftSnapshot(1, "recovery only")); other.Session.NotifyTransportLost();
            Check(other.Session.Phase == KeyboardSessionPhase.Faulted && !other.Session.TryBeginExecution(ticket));
            Check(other.Session.Result == new KeyboardSessionResult(false, null) && other.Session.LatestDraft == "recovery only");
        });
        await Sync("close during startup and duplicate close cannot resurrect the session", () => {
            var r = new Rig(); r.Send(new KeyboardOpen("original", null));
            r.Send(new KeyboardClose(KeyboardCloseReason.Cancelled), 2);
            Check(r.Send(new KeyboardClose(KeyboardCloseReason.Cancelled), 3).Disposition == KeyboardDisposition.Discarded);
            Check(r.Session.OwnershipEpoch == 2);
            r.Send(new KeyboardClosed());
            Check(r.Session.Phase == KeyboardSessionPhase.Closed && r.Session.Result == new KeyboardSessionResult(false, null));
        });
        await Sync("invalid messages revoke the session; cross-model tickets cannot execute", () => {
            var r = new Rig(); r.Activate(); var ticket = r.Issue(r.Input());
            var invalid = r.Envelope(new KeyboardPing(1));
            Check(r.Session.Apply(invalid with { Header = invalid.Header with { Version = 999 } }).Disposition == KeyboardDisposition.ProtocolFault);
            Check(!r.Session.TryBeginExecution(ticket) && r.Session.Phase == KeyboardSessionPhase.Faulted);
            var other = new Rig(); other.Activate(); other.Issue(other.Input()); Check(!other.Session.TryBeginExecution(ticket));
            var shell = new Rig(KeyboardPeer.Shell); shell.Activate(); var issued = shell.Issue(shell.Input());
            Check(!shell.Session.TryBeginExecution(issued));
            var live = new Rig(); live.Activate(); var oldTicket = live.Issue(live.Input());
            var replacement = new KeyboardSession(live.Connection, live.SessionId, KeyboardPeer.Host, live.Time);
            KeyboardExecutionTicket? replacementTicket = null;
            foreach (var message in live.History) replacementTicket = replacement.Apply(message).Execution ?? replacementTicket;
            Check(!replacement.TryBeginExecution(oldTicket) && replacement.TryBeginExecution(replacementTicket!));
        });
        await Sync("capture watermarks, stale inventories and command ID gaps do not replay input", () => {
            var r = new Rig(); r.Activate(); var input = r.Input();
            r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(input)));
            Check(r.Send(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(input))).Proposals.IsEmpty);
            Check(r.Session.PendingWork == 1);
            Check(r.Send(new KeyboardDevicesChanged(r.CaptureOwner.CollectorId, 1, ImmutableArray.Create(r.CaptureOwner, r.Other))).Disposition == KeyboardDisposition.Accepted);
            Check(r.Send(new KeyboardDevicesChanged(r.CaptureOwner.CollectorId, 1, ImmutableArray<KeyboardDeviceRef>.Empty)).Disposition == KeyboardDisposition.Discarded);
            Check(r.Send(new KeyboardExecute(2, input)).Disposition == KeyboardDisposition.Suspended && r.Session.PendingWork == 0);
        });
        await Test("two independent replicas complete a duplex named-pipe session", async () => {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string name = "ControllerPlayground.ProtocolTests." + Guid.NewGuid().ToString("N");
            await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await using var shellWire = new KeyboardFramedStream(server, true);
            await using var hostWire = new KeyboardFramedStream(client, true);
            var r = new Rig(); var shell = new KeyboardSession(r.Connection, r.SessionId, KeyboardPeer.Shell, r.Time);
            async Task<KeyboardSessionDecision> Transfer(KeyboardMessage message, ulong? epoch = null) {
                var envelope = r.Envelope(message, epoch);
                bool fromShell = envelope.Header.Sender == KeyboardPeer.Shell;
                var outgoing = fromShell ? shellWire : hostWire; var incoming = fromShell ? hostWire : shellWire;
                var first = (fromShell ? shell : r.Session).Apply(envelope);
                var receive = incoming.ReadAsync(timeout.Token).AsTask();
                await outgoing.WriteAsync(envelope, timeout.Token);
                var second = (fromShell ? r.Session : shell).Apply((await receive)!);
                Check(first.Disposition == KeyboardDisposition.Accepted && second.Disposition == first.Disposition);
                return fromShell ? second : first;
            }
            await Transfer(new KeyboardOpen("original Ω😀", r.ShellOwner));
            await Transfer(new KeyboardReady(100, 1, r.CaptureOwner.CollectorId, ImmutableArray.Create(r.CaptureOwner, r.Other)));
            await Transfer(new KeyboardSetOwner(r.Binding), 2);
            await Transfer(new KeyboardOwnerReady(r.Binding.BindingId, true, true, 0));
            var input = r.Input(ControllerAction.X); var nonowner = r.Input(device:r.Other);
            var observed = await Transfer(new KeyboardCaptureBatch(r.CaptureOwner.CollectorId, true, ImmutableArray.Create(input, nonowner)));
            Check(observed.Proposals.SequenceEqual(new[] { input }));
            var issued = await Transfer(new KeyboardExecute(1, input));
            Check(r.Session.TryBeginExecution(issued.Execution!) && !r.Session.TryBeginExecution(issued.Execution!));
            await Transfer(new KeyboardExecuted(1));
            await Transfer(new KeyboardCommit("exact final text\tΩ😀"));
            await Transfer(new KeyboardClose(KeyboardCloseReason.Finished), 3); await Transfer(new KeyboardClosed());
            Check(shell.Phase == KeyboardSessionPhase.Closed && r.Session.Phase == shell.Phase && shell.Result == r.Session.Result);
        });
        return count;
    }
    private static async Task<KeyboardEnvelope?> AsRead(ValueTask write) { await write; return null; }
}
