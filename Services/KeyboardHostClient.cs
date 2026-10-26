using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using Windows.System;
using ControllerPlayground.Input;
using ControllerPlayground.Input.Keyboard;
using Windows.ApplicationModel;


namespace ControllerPlayground.Services {
    internal sealed class KeyboardHostClient {
        private const string PipeName = "ControllerPlayground.KeyboardHost";
        private static readonly Guid ShellInstance = Guid.NewGuid();

        // Lifecycle migration only. Required controller ownership must never use
        // this compatibility executor, nor silently downgrade to the legacy pipe.
        public async Task<KeyboardLifecycleResult> RequestV2TextAsync(string initialText, KeyboardDeviceRef? owner,
            Func<Func<bool>, CancellationToken, Task<bool>> activateValidatedWindow,
            bool requireControllerOwnership = false, Action<string>? trace = null, CancellationToken cancellationToken = default,
            Action<uint>? peerVerified = null) {
            if (requireControllerOwnership) return new(KeyboardLifecycleStatus.Failed, Failure: "ControllerOwnershipNotAvailableUntilPhase3");
            NamedPipeClientStream? pipe = null;
            try {
                var policy = new KeyboardPeerPolicy("2d72be25-6768-4b4e-96a4-f3705bd815ff_" + Package.Current.Id.PublisherId,
                    "ControllerPlayground.KeyboardHost.exe");
                pipe = new(".", KeyboardPipeNames.Version2, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.ConnectAsync(250, cancellationToken); }
                catch (TimeoutException) {
                    pipe.Dispose(); pipe = null;
                    if (policy.HasRunningPeer()) {
                        // A verified process can be starting or finishing shutdown after
                        // its last pipe ended. Give its endpoint one bounded retry before
                        // declaring busy; never launch a competing instance or downgrade.
                        pipe = new(".", KeyboardPipeNames.Version2, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        try { await pipe.ConnectAsync(2000, cancellationToken); }
                        catch (TimeoutException) {
                            pipe.Dispose(); pipe = null;
                            if (policy.HasRunningPeer()) {
                                trace?.Invoke("V2 client-failure=V2UnavailableOrBusy (verified helper running, v2 endpoint unavailable; check helper deployment)");
                                return new(KeyboardLifecycleStatus.Failed, Failure: "V2UnavailableOrBusy");
                            }
                        }
                    }
                    if (pipe == null) {
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    startup.CancelAfter(TimeSpan.FromSeconds(7));
                    bool launched = await Launcher.LaunchUriAsync(new Uri("controllerplayground-keyboard:?transport=v2")).AsTask(startup.Token);
                    if (!launched) return new(KeyboardLifecycleStatus.Failed, Failure: "LaunchRejected");
                    pipe = new(".", KeyboardPipeNames.Version2, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.ConnectAsync(5000, startup.Token);
                    }
                }
                using var peer = KeyboardPipePeer.Verify(pipe, policy);
                peerVerified?.Invoke(peer.ProcessId);
                trace?.Invoke($"V2 server-verified pid={peer.ProcessId}");
                await using var connection = new KeyboardDuplexConnection(pipe);
                pipe = null; // Connection owns and closes the stream.
                var session = KeyboardLifecycleTransport.Client(connection, ShellInstance, new(initialText, owner),
                    ready => peer.ValidateWindow(ready), async (ready, token) => {
                        trace?.Invoke($"V2 window-validated pid={ready.ProcessId} hwnd={ready.WindowHandle}");
                        return await activateValidatedWindow(() => peer.TryActivate(ready), token);
                    }, trace: trace);
                return (await session.RunAsync(cancellationToken)) with { HelperProcessId = peer.ProcessId };
            } catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) {
                trace?.Invoke("V2 client-failure=" + error.GetType().Name);
                return cancellationToken.IsCancellationRequested ? new(KeyboardLifecycleStatus.Cancelled) : new(KeyboardLifecycleStatus.Failed, Failure: error.GetType().Name);
            } finally { pipe?.Dispose(); }
        }

        public async Task<string?> RequestTextAsync(string initialText, Action<nint>? keyboardReady = null, CancellationToken cancellationToken = default) {
            try {
                 NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                try {
                    await pipe.ConnectAsync(250, cancellationToken);
                } catch (TimeoutException) {
                    pipe.Dispose();

                    bool launched = await Launcher.LaunchUriAsync(new Uri("controllerplayground-keyboard:"));

                    if (!launched) 
                        return null;

                    await Task.Delay(500, cancellationToken);

                    pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await pipe.ConnectAsync(3000, cancellationToken);

                }

                await using (pipe) {
                    using var peer = KeyboardPipePeer.Verify(pipe, new("2d72be25-6768-4b4e-96a4-f3705bd815ff_" + Package.Current.Id.PublisherId,
                        "ControllerPlayground.KeyboardHost.exe"));

                    using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);

                    using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) {
                        AutoFlush = true
                    };

                    string encodedText = Encode(initialText ?? string.Empty);

                    await writer.WriteLineAsync($"OPEN {encodedText}");

                    string? readyResponse = await reader.ReadLineAsync(cancellationToken);

                    if (readyResponse == null || !readyResponse.StartsWith("READY ", StringComparison.Ordinal)) {
                        return null;
                    }

                    if (long.TryParse(readyResponse[6..], out long windowHandleValue)) {
                        if (!peer.ValidateWindow(windowHandleValue, peer.ProcessId)) return null;
                        keyboardReady?.Invoke(new nint(windowHandleValue));
                    }

                    string? commitResponse = await reader.ReadLineAsync(cancellationToken);

                    if (commitResponse == null) {
                        return null;
                    }

                    if (commitResponse == null || !commitResponse.StartsWith("COMMIT ", StringComparison.Ordinal)) {
                        return null;
                    }

                    return Decode(commitResponse[7..]);
                }

            } catch (TimeoutException) {
                return null;
            } 
            catch (IOException) {
                return null;
            } 
            catch (OperationCanceledException) {
                return null;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) {
                return null;
            }
        }

        private static string Encode(string text) {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private static string Decode(string encoded) {
            try {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            } catch (FormatException) {
                return string.Empty;
            }
        }
    }
            
}
