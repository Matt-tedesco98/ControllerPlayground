using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ControllerPlayground.Input.Keyboard;
using Windows.ApplicationModel;

namespace ControllerPlayground.KeyboardHost.Services;

internal sealed record KeyboardUiRequest(Guid SessionId, string Text, bool Version2, CancellationToken Cancellation);

internal sealed partial class KeyboardPipeServer {
    private sealed class Lease(Guid id, bool version2, CancellationToken token) : IDisposable {
        public Guid Id { get; } = id;
        public bool Version2 { get; } = version2;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public TaskCompletionSource<string?> Commit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public KeyboardLifecycleTransport? Transport;
        public void Dispose() { Cancel(); Cancellation.Dispose(); }
        public void Cancel() { try { Cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    }
    private readonly Guid _instance = Guid.NewGuid();
    private Lease? _lease;
    public event Func<Guid, Task>? CloseRequested;
    public Action<string>? Trace { get; set; }
    public Guid? ActiveSessionId => Volatile.Read(ref _lease)?.Id;
    public bool ActiveVersion2 => Volatile.Read(ref _lease)?.Version2 == true;
    public bool ActiveMediated => Volatile.Read(ref _lease)?.Transport?.Mediated == true;
    public KeyboardControllerBridge? ControllerBridge { get; set; }
    public bool PublishCapture(KeyboardCaptureSnapshot snapshot) => Volatile.Read(ref _lease)?.Transport?.PublishCapture(snapshot) == true;
    public bool IsCurrent(Guid id) => Volatile.Read(ref _lease)?.Id == id;
    private KeyboardPeerPolicy ShellPolicy => new("1f976d86-31bc-42a4-a14e-40fb2eb4fc98_" + Package.Current.Id.PublisherId, "ControllerPlayground.exe");
    private Lease Acquire(Guid id, bool v2, CancellationToken token) {
        var lease = new Lease(id, v2, token);
        if (Interlocked.CompareExchange(ref _lease, lease, null) != null) { lease.Dispose(); throw new IOException("Keyboard UI is busy."); }
        return lease;
    }
    private async Task Release(Lease lease) {
        lease.Cancel();
        try { if (CloseRequested is { } close) await close(lease.Id).WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { Interlocked.CompareExchange(ref _lease, null, lease); lease.Dispose(); }
    }
    public Task RunAsync() => Task.WhenAll(RunLegacyAsync(), RunVersion2());
    private async Task RunVersion2() {
        while (!_cancellation.IsCancellationRequested) {
            Lease? lease = null;
            try {
                await using var pipe = new NamedPipeServerStream(KeyboardPipeNames.Version2, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                using var peer = KeyboardPipePeer.Verify(pipe, ShellPolicy);
                Trace?.Invoke($"V2 client-verified pid={peer.ProcessId}");
                await using var connection = new KeyboardDuplexConnection(pipe);
                KeyboardLifecycleTransport? transport = null;
                transport = KeyboardLifecycleTransport.Host(connection, _instance, async (id, request, token) => {
                    peer.EnsureAlive(); lease = Acquire(id, true, token); lease.Transport = transport;
                    var handler = OpenRequested ?? throw new IOException("Keyboard UI unavailable.");
                    return await handler(new(id, request.InitialText, true, lease.Cancellation.Token));
                }, async (id, token) => {
                    if (lease?.Id == id) {
                        lease.Cancel();
                        if (CloseRequested is { } close) await close(id).WaitAsync(token);
                    }
                }, trace: Trace, controllers: ControllerBridge);
                await transport.RunAsync(_cancellation.Token);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or
                System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException) {
                Trace?.Invoke("V2 server-end=" + error.GetType().Name);
                if (!_cancellation.IsCancellationRequested) await Task.Delay(100);
            } finally {
                if (lease != null) { try { await Release(lease); } catch (Exception error) { Trace?.Invoke("V2 release=" + error.GetType().Name); } }
            }
        }
    }
    public void Dispose() { _cancellation.Cancel(); Volatile.Read(ref _lease)?.Cancel(); }
}
