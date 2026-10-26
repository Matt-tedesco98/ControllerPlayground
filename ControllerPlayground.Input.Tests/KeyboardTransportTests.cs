using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using ControllerPlayground.Input.Keyboard;

internal static class KeyboardTransportTests {
    private static void Check(bool value, string reason = "transport assertion failed") { if (!value) throw new InvalidOperationException(reason); }
    private static KeyboardReady Ready() => new(1, (uint)Environment.ProcessId, Guid.NewGuid(), ImmutableArray<KeyboardDeviceRef>.Empty);
    private static readonly KeyboardTransportTimeouts Fast = new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    private static string Name() => "ControllerPlayground.TransportTests." + Guid.NewGuid().ToString("N");
    [SupportedOSPlatform("windows")]
    private sealed class Pair : IAsyncDisposable {
        private readonly NamedPipeServerStream _server;
        private readonly NamedPipeClientStream _client;
        private readonly KeyboardDuplexConnection _hostWire, _shellWire;
        private readonly CancellationTokenSource _hostStop = new();
        public readonly CancellationTokenSource Request = new();
        public readonly TaskCompletionSource<Guid> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReadyHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly KeyboardLifecycleTransport Host;
        public readonly Task<KeyboardLifecycleResult> HostTask, ClientTask;
        public readonly List<string> Trace = new();
        private Pair(NamedPipeServerStream server, NamedPipeClientStream client, bool delay, bool validWindow,
            bool validContext, KeyboardCapabilities required) {
            _server = server; _client = client; _hostWire = new(server); _shellWire = new(client);
            void Log(string line) { lock (Trace) Trace.Add(line); }
            Host = KeyboardLifecycleTransport.Host(_hostWire, Guid.NewGuid(), async (id, open, token) => {
                Opened.TrySetResult(id);
                if (delay) await Task.Delay(Timeout.Infinite, token);
                return Ready();
            }, (id, token) => { Closed.TrySetResult(); return Task.CompletedTask; }, Fast, Log);
            var shell = KeyboardLifecycleTransport.Client(_shellWire, Guid.NewGuid(), new("initial Ω\t😀", null),
                _ => validWindow, (_, token) => { ReadyHandled.TrySetResult(); return Task.FromResult(validContext); }, required, Fast, Log);
            HostTask = Host.RunAsync(_hostStop.Token); ClientTask = shell.RunAsync(Request.Token);
        }
        public static async Task<Pair> Create(bool delay = false, bool validWindow = true, bool validContext = true,
            KeyboardCapabilities required = KeyboardProtocol.LifecycleCapabilities, string? name = null) {
            name ??= Name();
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            var policy = new KeyboardPeerPolicy(null, Environment.ProcessPath!);
            using var shellPeer = KeyboardPipePeer.Verify(client, policy);
            using var hostPeer = KeyboardPipePeer.Verify(server, policy);
            Check(shellPeer.ProcessId == Environment.ProcessId && hostPeer.ProcessId == Environment.ProcessId);
            return new(server, client, delay, validWindow, validContext, required);
        }
        public async ValueTask DisposeAsync() {
            Request.Cancel(); _hostStop.Cancel();
            await _hostWire.DisposeAsync(); await _shellWire.DisposeAsync();
            await Task.WhenAll(HostTask, ClientTask).WaitAsync(TimeSpan.FromSeconds(5));
            _server.Dispose(); _client.Dispose(); Request.Dispose(); _hostStop.Dispose();
        }
    }
    public static async Task<int> Run() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows peer security tests require Windows.");
        int count = 0;
        async Task Test(string name, Func<Task> body) { await body(); ++count; Console.WriteLine($"PASS transport: {name}"); }
        await Test("concurrent bidirectional writers preserve complete bounded frames without loss", async () => {
            string name = Name();
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await using var host = new KeyboardDuplexConnection(server);
            await using var shell = new KeyboardDuplexConnection(client);
            async Task Receive(KeyboardDuplexConnection connection) {
                var seen = new HashSet<Guid>();
                for (int i = 0; i < 48; i++) seen.Add((await connection.Incoming.ReadAsync(timeout.Token)).Header.ConnectionId);
                Check(seen.Count == 48, "A concurrent frame was duplicated or lost.");
            }
            var receiveHost = Receive(host); var receiveShell = Receive(shell);
            await Task.WhenAll(Enumerable.Range(0, 48).SelectMany(_ => new[] {
                host.SendAsync(Hello(), timeout.Token), shell.SendAsync(Hello(), timeout.Token)
            }));
            await Task.WhenAll(receiveHost, receiveShell);
        });
        await Test("failed startup times out and releases the helper UI without a terminal result", async () => {
            await using var pair = await Pair.Create(delay:true);
            await pair.Opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(5))).Status == KeyboardLifecycleStatus.Failed);
            await pair.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!pair.ReadyHandled.Task.IsCompleted);
        });
        await Test("bidirectional heartbeats, Ready, exact commit and Close/Closed complete one session", async () => {
            await using var pair = await Pair.Create();
            Guid id = await pair.Opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await pair.ReadyHandled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(100);
            pair.Host.Commit(id, "exact Ω😀\t\r\n");
            var result = await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4));
            Check(result.Status == KeyboardLifecycleStatus.Committed && result.Text == "exact Ω😀\t\r\n");
            Check((await pair.HostTask).Status == KeyboardLifecycleStatus.Committed);
            Check(pair.Trace.Any(t => t.Contains("KeyboardPong")) && pair.Trace.Any(t => t.Contains("KeyboardClosed")));
        });
        await Test("user cancellation after Ready closes and releases the helper", async () => {
            await using var pair = await Pair.Create(); await pair.ReadyHandled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            pair.Request.Cancel(); Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Cancelled);
            await pair.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        });
        await Test("cancellation during delayed UI startup revokes queued work and still closes", async () => {
            await using var pair = await Pair.Create(delay:true); await pair.Opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            pair.Request.Cancel();
            Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Cancelled);
            await pair.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3)); Check(!pair.ReadyHandled.Task.IsCompleted);
        });
        await Test("wrong HWND or invalidated chat context cannot request activation or commit text", async () => {
            await using (var pair = await Pair.Create(validWindow:false)) {
                Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Failed);
                Check(!pair.ReadyHandled.Task.IsCompleted);
            }
            await using (var pair = await Pair.Create(validContext:false)) {
                Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Failed);
                await pair.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
        });
        await Test("v2 ownership requirements cannot negotiate a silent lifecycle-only downgrade", async () => {
            await using var pair = await Pair.Create(required:KeyboardProtocol.RequiredCapabilities);
            Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Failed);
            Check(!pair.Opened.Task.IsCompleted);
        });
        await Test("duplicate and stale UI terminal callbacks produce only one result", async () => {
            await using var pair = await Pair.Create(); var id = await pair.Opened.Task;
            await pair.ReadyHandled.Task;
            pair.Host.Commit(Guid.NewGuid(), "wrong session"); pair.Host.Commit(id, "first"); pair.Host.Commit(id, "duplicate"); pair.Host.Cancel(id);
            var result = await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4));
            Check(result.Status == KeyboardLifecycleStatus.Committed && result.Text == "first");
            Check(pair.Trace.Count(t => t.Contains("send=KeyboardCommit")) == 1);
        });
        await Test("fresh connections on the same endpoint do not inherit cancelled session state", async () => {
            string name = Name(); Guid first;
            await using (var pair = await Pair.Create(name:name)) { first = await pair.Opened.Task; pair.Request.Cancel(); await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4)); }
            await using (var pair = await Pair.Create(name:name)) {
                Guid second = await pair.Opened.Task; Check(first != second); await pair.ReadyHandled.Task;
                pair.Host.Commit(first, "old result"); pair.Host.Cancel(second);
                Check((await pair.ClientTask.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Cancelled);
            }
        });
        await Test("peer process validation rejects another executable and forged window metadata", async () => {
            string name = Name(); using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            bool denied = false;
            try { using var wrong = KeyboardPipePeer.Verify(client, new(null, Path.Combine(AppContext.BaseDirectory, "not-the-helper.exe"))); }
            catch (UnauthorizedAccessException) { denied = true; }
            Check(denied);
            using var peer = KeyboardPipePeer.Verify(client, new(null, Environment.ProcessPath!));
            Check(!peer.ValidateWindow(Ready() with { ProcessId = uint.MaxValue })); Check(!peer.ValidateWindow(Ready()));
        });
        await Test("malformed input after Open closes the orphaned UI lease", async () => {
            string name = Name(); using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await using var hostWire = new KeyboardDuplexConnection(server); await using var attack = new KeyboardFramedStream(client, true);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = KeyboardLifecycleTransport.Host(hostWire, Guid.NewGuid(), (_,_,_) => Task.FromResult(Ready()), (_,_) => {closed.TrySetResult();return Task.CompletedTask;}, Fast);
            var running = host.RunAsync(timeout.Token);
            var hello = Hello(); await attack.WriteAsync(hello, timeout.Token); var ack = await attack.ReadAsync(timeout.Token);
            var identity = KeyboardConnectionIdentity.Negotiate(hello, ack!); Guid session = Guid.NewGuid();
            await attack.WriteAsync(new(new(2, identity.ConnectionId, identity.ShellInstanceId, identity.HostInstanceId, session, KeyboardPeer.Shell, 1, 1),new KeyboardOpen("draft",null)), timeout.Token);
            KeyboardEnvelope? incoming;
            do { incoming = await attack.ReadAsync(timeout.Token); } while (incoming?.Message is not KeyboardReady);
            await client.WriteAsync(new byte[] {0,0,0,0}, timeout.Token);
            Check((await running.WaitAsync(TimeSpan.FromSeconds(4))).Status == KeyboardLifecycleStatus.Failed);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        });
        await Test("duplicated terminal wire responses cannot overwrite the first committed result", async () => {
            string name = Name(); using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await using var shellWire = new KeyboardDuplexConnection(client); await using var raw = new KeyboardFramedStream(server, true);
            var shell = KeyboardLifecycleTransport.Client(shellWire, Guid.NewGuid(), new("draft",null), _=>true, (_,_)=>Task.FromResult(true), timeouts:Fast);
            var running = shell.RunAsync(timeout.Token);
            var hello = (await raw.ReadAsync(timeout.Token))!; Guid hostId = Guid.NewGuid();
            await raw.WriteAsync(new(hello.Header with {Sender=KeyboardPeer.Host,HostInstanceId=hostId},new KeyboardHelloAck(2,KeyboardProtocol.LifecycleCapabilities)),timeout.Token);
            var open = (await raw.ReadAsync(timeout.Token))!;
            var header = open.Header with {Sender=KeyboardPeer.Host,Sequence=1};
            await raw.WriteAsync(new(header,Ready()),timeout.Token);
            await raw.WriteAsync(new(header with {Sequence=2},new KeyboardCommit("first")),timeout.Token);
            await raw.WriteAsync(new(header with {Sequence=3},new KeyboardCommit("duplicate")),timeout.Token);
            KeyboardEnvelope? close;
            do {close=await raw.ReadAsync(timeout.Token);} while(close?.Message is not KeyboardClose);
            await raw.WriteAsync(new(header with {Sequence=4,OwnershipEpoch=close.Header.OwnershipEpoch},new KeyboardClosed()),timeout.Token);
            var result=await running.WaitAsync(TimeSpan.FromSeconds(4)); Check(result.Status==KeyboardLifecycleStatus.Committed&&result.Text=="first");
        });
        await Test("helper process termination yields failure, not a fabricated terminal result", async () => {
            string name = Name(); string executable = Path.Combine(AppContext.BaseDirectory, "ControllerPlayground.Input.Tests.exe");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--keyboard-transport-child"); start.ArgumentList.Add(name);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start test helper.");
            try {
                using var pipe = new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await pipe.ConnectAsync(5000,timeout.Token);
                using var peer = KeyboardPipePeer.Verify(pipe,new(null,executable)); Check(peer.ProcessId==child.Id);
                await using var wire=new KeyboardDuplexConnection(pipe); var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var session=KeyboardLifecycleTransport.Client(wire,Guid.NewGuid(),new("draft",null),_=>true,(_,_)=>{ready.TrySetResult();return Task.FromResult(true);},timeouts:Fast);
                var running=session.RunAsync(timeout.Token); await ready.Task.WaitAsync(TimeSpan.FromSeconds(4)); child.Kill(); await child.WaitForExitAsync(timeout.Token);
                Check((await running.WaitAsync(TimeSpan.FromSeconds(4))).Status==KeyboardLifecycleStatus.Failed);
            } finally {if(!child.HasExited)child.Kill();}
        });
        return count;
    }
    private static KeyboardEnvelope Hello() => new(new(2,Guid.NewGuid(),Guid.NewGuid(),Guid.Empty,Guid.Empty,KeyboardPeer.Shell,0,1),new KeyboardHello(2,2,KeyboardProtocol.LifecycleCapabilities));
    public static async Task RunChild(string name) {
        if(!OperatingSystem.IsWindows()||!name.StartsWith("ControllerPlayground.TransportTests.",StringComparison.Ordinal))throw new ArgumentException("Invalid child probe.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(timeout.Token);
        await using var wire=new KeyboardDuplexConnection(pipe);
        var host=KeyboardLifecycleTransport.Host(wire,Guid.NewGuid(),(_,_,_)=>Task.FromResult(Ready()),(_,_)=>Task.CompletedTask,Fast);
        await host.RunAsync(timeout.Token);
    }
}
