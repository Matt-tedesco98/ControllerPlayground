using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using Windows.System;


namespace ControllerPlayground.Services {
    internal sealed class KeyboardHostClient {
        private const string PipeName = "ControllerPlayground.KeyboardHost";

        public async Task<string>? RequestTextAsync(string initialText, Action<nint>? keyboardReady = null, CancellationToken cancellationToken = default) {
            try {
                 NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                try {
                    await pipe.ConnectAsync(250, cancellationToken);
                } catch (TimeoutException) {
                    pipe.Dispose();

                    bool launched = await Launcher.LaunchUriAsync(new Uri("controllerplayground-keyboard:"));

                    if (!launched) 
                        return null;

                    await Task.Delay(500, cancellationToken);

                    pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(3000, cancellationToken);

                }

                await using (pipe) {

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
                        keyboardReady?.Invoke(new nint(windowHandleValue));
                    }

                    string? commitResponse = await reader.ReadLineAsync(cancellationToken);

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
