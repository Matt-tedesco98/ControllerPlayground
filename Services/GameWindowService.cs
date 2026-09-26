using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ControllerPlayground.Services {
    public sealed class GameWindowService {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);


        public bool TryFocusGameWindow(string gameInstallPath) {
            if (string.IsNullOrWhiteSpace(gameInstallPath))
                return false;

            string normalizedGamePath = Path.GetFullPath(gameInstallPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            IntPtr gameWindow = IntPtr.Zero;

            EnumWindows((hWnd, _) => {
                if (!IsWindowVisible(hWnd))
                    return true; // Continue enumerating
                
                GetWindowThreadProcessId(hWnd, out uint processId);

                if(processId == 0)
                    return true; // Continue enumerating

                try {
                    using Process process = Process.GetProcessById((int)processId);
                    string? executablePath = process.MainModule?.FileName;

                    if (string.IsNullOrWhiteSpace(executablePath))
                        return true; // Continue enumerating

                    if(executablePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase)) {
                        gameWindow = hWnd;
                        return false; // Stop enumerating
                    }
                } catch {

                }
                return true; // Continue enumerating
            }, IntPtr.Zero);

            if (gameWindow == IntPtr.Zero) {
                Debug.WriteLine($"No game window found under: {normalizedGamePath}");
                return false;

            }

            bool focused = SetForegroundWindow(gameWindow);

            Debug.WriteLine(focused ? $"Focused game window for: {gameWindow}" : $"Failed to focus game window for: {gameWindow}");
            return focused;
        }
    }
}
