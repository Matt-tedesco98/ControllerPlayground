using System.Runtime.InteropServices;
using ControllerPlayground.Input;

// Optional hardware tool; never used by the shell or by automated tests.
internal static class ControllerMonitor {
    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);
    private static bool _closing;
    private static Func<bool>? _escape;
    private static readonly WindowProc Procedure = HandleMessage;
    private static nint HandleMessage(nint window, uint message, nuint wParam, nint lParam) {
        if (message == 0x0010) { _closing = true; return 0; } // WM_CLOSE: defer destruction until shutdown.
        if (message == 0x0100 && wParam == 27 && _escape?.Invoke() == true) return 0;
        return DefWindowProcW(window, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass {
        public uint Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Menu;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extraStyle, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(nint window, string title);

    public static void Run(bool ownership = false, int seconds = 120) {
        if (seconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        _closing = false;
        using StreamWriter? log = ownership ? new(Path.Combine(Environment.CurrentDirectory,
            $"controller-ownership-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log")) { AutoFlush = true } : null;
        void Write(string value) { Console.WriteLine(value); log?.WriteLine(value); }
        var cls = new WindowClass { Name = "ControllerPlaygroundManagedInputDiagnostic", Instance = GetModuleHandleW(null), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure) };
        if (RegisterClassW(ref cls) == 0) throw new InvalidOperationException("Cannot register diagnostic window.");
        nint window = CreateWindowExW(0, cls.Name, "Managed input: focus here; use both controllers; close to stop", 0x10CF0000, 100, 100, 900, 180, 0, 0, cls.Instance, 0);
        if (window == 0) throw new InvalidOperationException("Cannot create diagnostic window.");
        try {
            ControllerInputNative.ControllerInput_SetWindowHandle(window);
            if (!ControllerInputNative.ControllerInput_Initialize()) throw new InvalidOperationException("Native initialization failed.");
            SetForegroundWindow(window);
            var source = new NativeControllerInputSource();
            var service = new ControllerService(source);
            service.DeviceConnectionChanged += e => Write($"DEVICE id={e.Device.Id} gen={e.Device.ConnectionGeneration} connected={e.Connected} family={e.Device.Family}");
            if (!ownership) service.ActionReceived += e => Write($"ACTION seq={e.Sequence} id={e.Device.Id} gen={e.Device.ConnectionGeneration} family={e.Device.Family} action={e.Action} repeat={e.IsRepeat} nativeTime={e.NativeTimestampMicroseconds} observedMs={e.ObservedAtMilliseconds}");
            var owned = ownership ? new OwnershipDiagnosticSession(service, Write, title => SetWindowTextW(window, title)) : null;
            _escape = owned == null ? null : owned.CloseTop;
            long end = Environment.TickCount64 + seconds * 1000L;
            Write($"session={service.CollectorSessionId}; mode={(ownership ? "router ownership diagnostic (not WinUI focus)" : "capture only")}; close window to stop.");
            while (!_closing && Environment.TickCount64 < end) {
                while (PeekMessageW(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); }
                if (owned != null) owned.Pump(); else service.PollActions();
                Thread.Sleep(16);
            }
            Write($"Finished. devices={service.Devices.Count} drops={source.Diagnostics.DroppedEvents} historyRecoveries={source.Diagnostics.HistoryResets}");
        } finally {
            _escape = null;
            ControllerInputNative.ControllerInput_Shutdown();
            DestroyWindow(window);
            GC.KeepAlive(Procedure);
        }
    }
}
