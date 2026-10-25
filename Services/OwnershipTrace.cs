using System;
using System.Diagnostics;
using System.IO;

namespace ControllerPlayground.Services;

// Debug metadata only: no chat text or credentials. Logging failures must not
// prevent the controller recovery path from working.
internal sealed class OwnershipTrace : IDisposable {
    internal static Action<string>? TemporaryInputSink;
    internal static string? ActionContext;
    internal static void Command(string command) => TemporaryInputSink?.Invoke($"UI {ActionContext ?? "source=pointer-or-keyboard-or-async"} {command}");
    private StreamWriter? _writer;
    public OwnershipTrace() {
#if DEBUG
        try {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerPlayground", "Logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"ownership-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
            _writer = new StreamWriter(path) { AutoFlush = true };
            Debug.WriteLine($"Ownership log: {path}");
        } catch (Exception error) { Debug.WriteLine($"Ownership logging unavailable: {error.Message}"); }
#endif
    }
    public void Write(string message) {
        Debug.WriteLine(message);
        try { _writer?.WriteLine($"{DateTime.UtcNow:O} tickMs={Environment.TickCount64} {message}"); }
        catch (Exception error) { Debug.WriteLine($"Ownership logging failed: {error.Message}"); _writer = null; }
    }
    public void Dispose() {
        try { _writer?.Dispose(); } catch (Exception error) { Debug.WriteLine(error.Message); }
        _writer = null;
    }
}
