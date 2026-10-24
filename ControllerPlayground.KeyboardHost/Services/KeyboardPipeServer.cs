using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using System.Diagnostics.CodeAnalysis;


namespace ControllerPlayground.KeyboardHost.Services {
    internal sealed class KeyboardPipeServer {
        private const string PipeName = "ControllerPlayground.KeyboardHost";
        private readonly CancellationTokenSource _cancellation = new();
        private TaskCompletionSource<string?>? _pendingCommit;
        public event Func<string, Task>? OpenRequested;
        public nint WindowHandle { get; set; }

        public async Task RunAsync() {
            while (!_cancellation.IsCancellationRequested) {
                try {
                    await using NamedPipeServerStream pipe = new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_cancellation.Token);

                    using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);

                    using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) {
                        AutoFlush = true
                    };

                    string? request = await reader.ReadLineAsync(_cancellation.Token);

                    if (request == null || !request.StartsWith("OPEN ", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    string initialText = Decode(request[5..]);

                    _pendingCommit = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

                    Func<string, Task>? openHandler = OpenRequested;

                    if (openHandler != null) {
                        await openHandler(initialText);
                    }   

                    await writer.WriteLineAsync($"READY {WindowHandle.ToInt64()}");

                    string? commitedText = await _pendingCommit.Task.WaitAsync(_cancellation.Token);

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
                }
            } 
        }

        public void Commit(string text) {
            _pendingCommit?.TrySetResult(text ?? string.Empty);
        }

        public void Cancel() {
            _pendingCommit?.TrySetResult(null);
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
