using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControllerPlayground.Input.Keyboard;

// OS-observed identity, never a PID or executable path supplied by a message.
[SupportedOSPlatform("windows")]
public sealed class KeyboardPipePeer : IDisposable {
    private readonly Process _process;
    private readonly string _path;
    private readonly long _started;
    public uint ProcessId { get; }
    private KeyboardPipePeer(uint pid, KeyboardPeerPolicy policy) {
        ProcessId = pid;
        _process = Process.GetProcessById(checked((int)pid));
        _path = Path.GetFullPath(_process.MainModule?.FileName ?? throw new IOException("Peer executable unavailable."));
        _started = _process.StartTime.ToUniversalTime().Ticks;
        if (_process.SessionId != Process.GetCurrentProcess().SessionId || !policy.Accepts(_process, _path)) {
            _process.Dispose(); throw new UnauthorizedAccessException("Unexpected keyboard pipe peer.");
        }
    }
    public static KeyboardPipePeer Verify(NamedPipeClientStream pipe, KeyboardPeerPolicy policy) {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) || pid == 0) throw new IOException("Cannot identify pipe server.");
        return new(pid, policy);
    }
    public static KeyboardPipePeer Verify(NamedPipeServerStream pipe, KeyboardPeerPolicy policy) {
        var computer = new StringBuilder(256);
        bool named = GetNamedPipeClientComputerName(pipe.SafePipeHandle, computer, 256);
        int nameError = Marshal.GetLastWin32Error();
        // Windows returns ERROR_PIPE_LOCAL (229) instead of a computer name for
        // genuine local clients. A returned name is a network transport, even
        // if that name happens to match this machine (SMB loopback).
        if (named || nameError != 229 ||
            !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid == 0)
            throw new UnauthorizedAccessException("Non-local/unidentified pipe client.");
        return new(pid, policy);
    }
    public void EnsureAlive() {
        if (_process.HasExited || _process.StartTime.ToUniversalTime().Ticks != _started ||
            !string.Equals(Path.GetFullPath(_process.MainModule?.FileName ?? ""), _path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Keyboard peer lifetime ended.");
    }
    public bool ValidateWindow(KeyboardReady ready) {
        return ValidateWindow(ready.WindowHandle, ready.ProcessId);
    }
    public bool ValidateWindow(long handle, uint processId) {
        EnsureAlive();
        if (processId != ProcessId || handle <= 0) return false;
        nint hwnd = checked((nint)handle);
        return IsWindow(hwnd) && IsWindowVisible(hwnd) && GetAncestor(hwnd, 2) == hwnd &&
            GetWindowThreadProcessId(hwnd, out uint owner) != 0 && owner == ProcessId;
    }
    public bool TryActivate(KeyboardReady ready) {
        if (!ValidateWindow(ready)) return false;
        nint window = checked((nint)ready.WindowHandle);
        ShowWindow(window, 9);
        return SetForegroundWindow(window);
    }
    public Task WaitForExitAsync(CancellationToken token) => _process.WaitForExitAsync(token);
    public void Dispose() => _process.Dispose();
    internal static string PackageFamily(Process process) {
        uint size = 0;
        if (GetPackageFamilyName(process.SafeHandle, ref size, null) != 122) return "";
        var text = new StringBuilder((int)size);
        return GetPackageFamilyName(process.SafeHandle, ref size, text) == 0 ? text.ToString() : "";
    }
    internal static string PackageDirectory(Process process) {
        uint size = 0;
        if (GetPackageFullName(process.SafeHandle, ref size, null) != 122) return "";
        var fullName = new StringBuilder((int)size);
        if (GetPackageFullName(process.SafeHandle, ref size, fullName) != 0) return "";
        size = 0; if (GetPackagePathByFullName(fullName.ToString(), ref size, null) != 122) return "";
        var path = new StringBuilder((int)size);
        return GetPackagePathByFullName(fullName.ToString(), ref size, path) == 0 ? path.ToString() : "";
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetNamedPipeClientComputerName(SafePipeHandle pipe, StringBuilder name, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint size, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(SafeProcessHandle process, ref uint size, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint size, StringBuilder? path);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

[SupportedOSPlatform("windows")]
public sealed record KeyboardPeerPolicy(string? PackageFamily, string Executable) {
    public bool Accepts(Process process, string path) {
        if (PackageFamily == null) return string.Equals(Path.GetFullPath(Executable), path, StringComparison.OrdinalIgnoreCase);
        string directory = KeyboardPipePeer.PackageDirectory(process);
        return directory.Length > 0 && KeyboardPipePeer.PackageFamily(process) == PackageFamily &&
            string.Equals(Path.GetFullPath(Path.Combine(directory, Executable)), path, StringComparison.OrdinalIgnoreCase);
    }
    public bool HasRunningPeer() {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable))) {
            using (process) {
                try { if (process.SessionId == Process.GetCurrentProcess().SessionId && Accepts(process, Path.GetFullPath(process.MainModule!.FileName))) return true; }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
            }
        }
        return false;
    }
}
