using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using ControllerPlayground.Input.Keyboard;


namespace ControllerPlayground.KeyboardHost.Services {
    internal sealed partial class KeyboardPipeServer : IDisposable {
        private const string PipeName = "ControllerPlayground.KeyboardHost";
        private readonly CancellationTokenSource _cancellation = new();
        private TaskCompletionSource<string?>? _pendingCommit;
        public event Func<KeyboardUiRequest, Task<KeyboardReady>>? OpenRequested;
        public nint WindowHandle { get; set; }

        private async Task RunLegacyAsync() {
            while (!_cancellation.IsCancellationRequested) {
                Lease? lease = null;
                try {
                    await using NamedPipeServerStream pipe = new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await pipe.WaitForConnectionAsync(_cancellation.Token);
                    using var peer = KeyboardPipePeer.Verify(pipe, ShellPolicy);

                    using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);

                    using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) {
                        AutoFlush = true
                    };

                    string? request = await reader.ReadLineAsync(_cancellation.Token);

                    if (request == null || !request.StartsWith("OPEN ", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    string initialText = Decode(request[5..]);

                    lease = Acquire(Guid.NewGuid(), false, _cancellation.Token);
                    _pendingCommit = lease.Commit;
                    var disconnected = reader.ReadLineAsync(lease.Cancellation.Token).AsTask();

                    var openHandler = OpenRequested ?? throw new IOException("Keyboard UI unavailable.");

                    var opening = openHandler(new(lease.Id, initialText, false, lease.Cancellation.Token));
                    if (await Task.WhenAny(opening, disconnected) == disconnected) continue;
                    await opening.WaitAsync(TimeSpan.FromSeconds(5), lease.Cancellation.Token);

                    await writer.WriteLineAsync($"READY {WindowHandle.ToInt64()}");

                    if (await Task.WhenAny(_pendingCommit.Task, disconnected) == disconnected) continue;
                    string? commitedText = await _pendingCommit.Task.WaitAsync(lease.Cancellation.Token);

                    if (commitedText == null) {
                        await writer.WriteLineAsync("CANCEL");
                    } else {
                    await writer.WriteLineAsync($"COMMIT {Encode(commitedText)}");
                    }

                    _pendingCommit = null;

                } catch (OperationCanceledException)
                    when (_cancellation.IsCancellationRequested) { 
                } catch (IOException) {
                    _pendingCommit = null;
                    if (!_cancellation.IsCancellationRequested) await Task.Delay(100);
                } catch (Exception error) when (error is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException) {
                    Trace?.Invoke("Legacy server-end=" + error.GetType().Name);
                } finally {
                    if (lease != null) {
                        if (ReferenceEquals(_pendingCommit, lease.Commit)) _pendingCommit = null;
                        try { await Release(lease); } catch (Exception error) { Trace?.Invoke("Legacy release=" + error.GetType().Name); }
                    }
                }
            } 
        }

        public void Commit(string text) {
            var lease = Volatile.Read(ref _lease);
            if (lease?.Version2 == true) lease.Transport?.Commit(lease.Id, text);
            else _pendingCommit?.TrySetResult(text ?? string.Empty);
        }

        public void Cancel() {
            var lease = Volatile.Read(ref _lease);
            if (lease?.Version2 == true) lease.Transport?.Cancel(lease.Id);
            else _pendingCommit?.TrySetResult(null);
        }

        public static string Encode(string text) {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        public static string Decode(string encoded) {
            try {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            } catch (FormatException) {
                return string.Empty;
            }
        }
    }
}
