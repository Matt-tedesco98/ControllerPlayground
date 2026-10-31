using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ControllerPlayground.Services {
    public sealed class PowerService {

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SetSuspendState(
            [MarshalAs(UnmanagedType.I1)] bool hibernate,
            [MarshalAs(UnmanagedType.I1)] bool force,
            [MarshalAs(UnmanagedType.I1)] bool wakeupEventsDisabled);

        public void Sleep() {
            if (!SetSuspendState(hibernate: false, force: false, wakeupEventsDisabled: false)) {
                throw new InvalidOperationException($"Sleep failed (Win32 error {Marshal.GetLastWin32Error()})");
            }
        }

        public void Restart() => RunShutdown("/r", "/t", "0");

        public void Shutdown() => RunShutdown("/s", "/t", "0");

        private static void RunShutdown(params string[] arguments) {
            var startInfo = new ProcessStartInfo("shutdown.exe") {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments) {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
        }
    }
}
