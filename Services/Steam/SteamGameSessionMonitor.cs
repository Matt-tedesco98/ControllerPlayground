using ControllerPlayground.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ControllerPlayground.Services.Steam {
    public sealed class SteamGameSessionMonitor {
        private readonly ISteamLocalService _steamLocalService;

        private CancellationTokenSource? _monitorCancellation;

        public event Action<uint>? GameStarted;
        public event Action<uint>? GameExited;
        private uint? _monitoringAppId;

        public SteamGameSessionMonitor(
            ISteamLocalService steamLocalService) {
            _steamLocalService =
                steamLocalService;
        }

        public void StartMonitoring(uint appId) {
            if (_monitoringAppId == appId && _monitorCancellation != null && !_monitorCancellation.IsCancellationRequested) {
                Debug.WriteLine(
                    $"Already monitoring Steam AppId {appId}.");
                return;
            }
            _monitorCancellation?.Cancel();
            _monitorCancellation?.Dispose();

            CancellationTokenSource cancellation = new();

            _monitorCancellation = cancellation;
            _monitoringAppId = appId;

            _ = MonitorGameAsync(
                appId,
                cancellation);
        }

        private async Task MonitorGameAsync(
            uint appId,
            CancellationTokenSource cancellation) {
            CancellationToken cancellationToken = cancellation.Token;
            try {
                Debug.WriteLine(
                    $"Waiting for Steam AppId {appId} to start...");

                bool started = false;

                // Give Steam/game launch up to 15 minutes.
                for (int i = 0; i < 900; i++) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await _steamLocalService
                        .IsGameRunningAsync(
                            appId,
                            cancellationToken)) {
                        started = true;
                        break;
                    }

                    await Task.Delay(
                        1000,
                        cancellationToken);
                }

                if (!started) {
                    Debug.WriteLine(
                        $"Steam AppId {appId} did not start within 15 minutes.");

                    return;
                }

                Debug.WriteLine(
                    $"Steam AppId {appId} started.");

                GameStarted?.Invoke(appId);

                while (!cancellationToken.IsCancellationRequested) {
                    await Task.Delay(
                        1000,
                        cancellationToken);

                    bool isRunning =
                        await _steamLocalService
                            .IsGameRunningAsync(
                                appId,
                                cancellationToken);

                    if (!isRunning) {
                        break;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                Debug.WriteLine(
                    $"Steam AppId {appId} exited.");

                GameExited?.Invoke(appId);
            } catch (OperationCanceledException) {
                Debug.WriteLine(
                    $"Steam game monitor cancelled: {appId}");
            } catch (Exception ex) {
                Debug.WriteLine(
                    $"Steam game monitor failed for {appId}: {ex}");
            } finally { 
                if (ReferenceEquals(_monitorCancellation, cancellation)) {
                    _monitorCancellation.Dispose();
                    _monitorCancellation = null;
                    _monitoringAppId = null;
                }
            }
        }
    }
}