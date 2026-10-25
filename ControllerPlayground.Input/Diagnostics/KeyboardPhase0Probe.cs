using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ControllerPlayground.Input.Diagnostics;

// PHASE 0 ONLY. Observes the existing collector; never polls native input, changes
// focus policy, edits text, or executes a keyboard command. Private disposable pipe.
public sealed class KeyboardPhase0Probe : IDisposable {
    public sealed record Activation(Guid Run, string Directory);
    public sealed record Capture(string Collector, ulong Sequence, string Id, string Root,
        ulong Generation, string Family, string Action, bool Repeat, ulong NativeTime, bool Foreground);
    public sealed record Packet(string Kind, long Sequence, long StartedTicks, Capture? Input = null,
        string[]? DeviceIds = null, string? OwnerId = null, bool Approved = false);
    private readonly string _role, _pipe;
    private readonly ControllerService _service;
    private readonly Func<Action, bool> _dispatch;
    private readonly Func<ControllerDevice?> _owner;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Packet> _outbound = Channel.CreateBounded<Packet>(64);
    private readonly StreamWriter _log;
    private readonly object _logLock = new();
    private readonly Action<string>? _previousSink;
    private readonly Action<string> _sink;
    private long _sequence, _lastPing, _lastInventory, _mockExecutions;
    private bool? _foreground;
    private bool _disposed;
    private readonly Task _worker;
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? name);
    private static string PackageIdentity() {
        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != 122) return "unpackaged";
        var name = new StringBuilder((int)length);
        return GetCurrentPackageFamilyName(ref length, name) == 0 ? name.ToString() : "unknown";
    }
    private bool Foreground { get { GetWindowThreadProcessId(GetForegroundWindow(), out uint pid); return pid == Environment.ProcessId; } }

    public static KeyboardPhase0Probe? TryStart(string role, ControllerService service,
        Func<Action, bool> dispatch, Func<ControllerDevice?>? owner = null) {
        try {
            string? runText = Environment.GetEnvironmentVariable("CONTROLLERPLAYGROUND_PHASE0");
            string? folder = Environment.GetEnvironmentVariable("CONTROLLERPLAYGROUND_PHASE0_DIR");
            string marker = Path.Combine(AppContext.BaseDirectory, "phase0-probe.log");
            if (runText == null && File.Exists(marker)) {
                var activation = JsonSerializer.Deserialize<Activation>(File.ReadAllText(marker));
                runText = activation?.Run.ToString(); folder = activation?.Directory;
            }
            if (!Guid.TryParse(runText, out var run)) return null;
            return new(role, run, folder, service, dispatch, owner ?? (() => null));
        }
        catch (Exception error) { Debug.WriteLine($"Phase 0 probe unavailable: {error}"); return null; }
    }
    private KeyboardPhase0Probe(string role, Guid run, string? folder, ControllerService service,
        Func<Action, bool> dispatch, Func<ControllerDevice?> owner) {
        _role = role; _pipe = $"ControllerPlayground.Phase0.{run:N}";
        _service = service; _dispatch = dispatch; _owner = owner;
        folder ??= Path.GetTempPath();
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"phase0-{run:N}-{role}-{Environment.ProcessId}.log");
        _log = new(path, false, new UTF8Encoding(false));
        Log("START", new { run, role, pid = Environment.ProcessId, binary = Environment.ProcessPath,
            collector = service.CollectorSessionId, package = PackageIdentity(), frequency = Stopwatch.Frequency,
            policy = "UNCHANGED ExclusiveForegroundInput + EnableBackgroundGuideButton",
            execution = "diagnostic counter only; working keyboard unchanged" });
        _previousSink = service.DiagnosticSink;
        _sink = line => { _previousSink?.Invoke(line); Log("RAW", line); };
        service.DiagnosticSink = _sink;
        service.ActionReceived += Observe;
        service.DeviceConnectionChanged += Connection;
        _worker = Task.Run(role == "shell" ? ServerAsync : ClientAsync);
    }
    private void Log(string kind, object? data) {
        lock (_logLock) {
            if (_disposed) return;
            try { _log.WriteLine(JsonSerializer.Serialize(new { utc = DateTime.UtcNow, ticks = Stopwatch.GetTimestamp(), role = _role, kind, data })); }
            catch (IOException) { /* Diagnostic IO cannot break the keyboard. */ }
        }
    }
    private void Connection(ControllerConnectionEvent e) => Log("CONNECTION", new {
        id = e.Device.Id.ToString(), root = e.Device.RootId.ToString(), e.Device.ConnectionGeneration,
        e.Device.VendorId, e.Device.ProductId, e.Device.Family, e.Connected, e.NativeSequence, e.NativeTimestampMicroseconds });
    private void Observe(ControllerActionEvent e) {
        long started = Stopwatch.GetTimestamp();
        var capture = new Capture(e.CollectorSessionId.ToString(), e.Sequence, e.Device.Id.ToString(), e.Device.RootId.ToString(),
            e.Device.ConnectionGeneration, e.Device.Family.ToString(), e.Action.ToString(), e.IsRepeat, e.NativeTimestampMicroseconds, Foreground);
        Log("CAPTURE", capture);
        if (_role == "helper" && capture.Foreground)
            Send(new("action", Interlocked.Increment(ref _sequence), started, capture));
    }
    private void Send(Packet packet) {
        if (!_outbound.Writer.TryWrite(packet)) Log("PROBE_QUEUE_DROP", packet.Sequence);
    }
    // Called after the host's existing poll, on its UI thread. No extra capture.
    public void AfterPoll() {
        bool foreground = Foreground;
        if (_foreground != foreground) { _foreground = foreground; Log("FOCUS", new { foreground }); }
        long now = Environment.TickCount64;
        if (now - _lastInventory >= 1000) {
            _lastInventory = now;
            Log("INVENTORY", _service.Devices.Select(d => new { id = d.Id.ToString(), root = d.RootId.ToString(),
                d.ConnectionGeneration, d.VendorId, d.ProductId, family = d.Family.ToString() }).ToArray());
            lock (_logLock) { if (!_disposed) _log.Flush(); }
        }
        if (_role == "helper" && now - _lastPing >= 100) {
            _lastPing = now;
            Send(new("ping", Interlocked.Increment(ref _sequence), Stopwatch.GetTimestamp()));
        }
    }
    private async Task<T> OnUi<T>(Func<T> callback) {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatch(() => {
            try { if (_stop.IsCancellationRequested) completion.TrySetCanceled(); else completion.TrySetResult(callback()); }
            catch (Exception error) { completion.TrySetException(error); }
        })) throw new IOException("UI dispatcher stopped.");
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), _stop.Token);
    }
    public static bool IdentityOnlyApproval(Packet packet, ControllerDevice? owner, IEnumerable<ControllerDevice> devices) =>
        packet.Kind == "action" && packet.Input is { Foreground: true } input && owner != null &&
        input.Id == owner.Id.ToString() && devices.Any(d => d.Id == owner.Id && d.ConnectionGeneration == owner.ConnectionGeneration);
    private async Task ServerAsync() {
        while (!_stop.IsCancellationRequested) {
            try {
                await using var pipe = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                Log("PIPE_CONNECTED", null);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                long previous = 0;
                while (await reader.ReadLineAsync(_stop.Token) is { } line) {
                    if (line.Length > 16384) throw new IOException("Probe frame too large.");
                    var request = JsonSerializer.Deserialize<Packet>(line) ?? throw new IOException("Empty probe packet.");
                    if (request.Sequence <= previous) throw new IOException("Duplicate/reordered probe packet.");
                    previous = request.Sequence;
                    var response = await OnUi(() => {
                        var owner = _owner();
                        bool approved = IdentityOnlyApproval(request, owner, _service.Devices);
                        if (request.Input != null) Log("AUTHORIZATION", new { request.Input, approved,
                            owner = owner?.Id.ToString(), note = "identity equality experiment only; no production binding" });
                        return request with { DeviceIds = _service.Devices.Select(d => d.Id.ToString()).ToArray(),
                            OwnerId = owner?.Id.ToString(), Approved = approved };
                    });
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), _stop.Token);
                }
            } catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or JsonException) {
                Log("PIPE_END", error.GetType().Name);
            }
        }
    }
    private async Task ClientAsync() {
        while (!_stop.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1000, _stop.Token);
                Log("PIPE_CONNECTED", null);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                await foreach (var request in _outbound.Reader.ReadAllAsync(_stop.Token)) {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), _stop.Token);
                    string? line = await reader.ReadLineAsync(_stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3), _stop.Token);
                    var response = line == null ? null : JsonSerializer.Deserialize<Packet>(line);
                    if (response?.Sequence != request.Sequence) throw new IOException("Unmatched probe response.");
                    await OnUi(() => {
                        // Intentionally not Keyboard.HandleControllerAction: never types twice.
                        bool executed = response.Approved && Foreground;
                        if (executed) ++_mockExecutions;
                        double elapsedMs = Stopwatch.GetElapsedTime(request.StartedTicks).TotalMilliseconds;
                        Log("ROUNDTRIP", new { request.Kind, request.Sequence, elapsedMs, response.Approved,
                            mockExecuted = executed, executions = _mockExecutions, response.OwnerId,
                            shellIds = response.DeviceIds, helperIds = _service.Devices.Select(d => d.Id.ToString()).ToArray(),
                            input = request.Input });
                        return true;
                    });
                }
            } catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or JsonException) {
                Log("PIPE_END", error.GetType().Name);
                while (_outbound.Reader.TryRead(out _)) { } // Never replay stale probe work after reconnect.
                try { await Task.Delay(250, _stop.Token); } catch (OperationCanceledException) { }
            }
        }
    }
    public void Dispose() {
        _service.ActionReceived -= Observe;
        _service.DeviceConnectionChanged -= Connection;
        if (_service.DiagnosticSink == _sink) _service.DiagnosticSink = _previousSink;
        _stop.Cancel(); _outbound.Writer.TryComplete();
        Log("STOP", new { mockExecutions = _mockExecutions });
        lock (_logLock) { _disposed = true; _log.Dispose(); }
        // The worker observes cancellation; do not synchronously block a UI dispatcher.
        _ = _worker.ContinueWith(t => Debug.WriteLine(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }
}
