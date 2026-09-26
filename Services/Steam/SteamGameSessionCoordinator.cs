using ControllerPlayground.Services;
using SteamKit2.Internal;
using System;
using System.Diagnostics;

namespace ControllerPlayground.Services.Steam {
    public sealed class SteamGameSessionCoordinator {
        private readonly GameSessionService _sessionService;
        private readonly SteamGameSessionMonitor _sessionMonitor;

        public event Action<uint>? GameStarted;
        public event Action<uint>? GameExited;

        public SteamGameSessionCoordinator(SteamLaunchService launchService, GameSessionService sessionService, ISteamLocalService steamLocalService) {
            _sessionService = sessionService;

            launchService.SessionService = sessionService;

            _sessionMonitor = new SteamGameSessionMonitor(steamLocalService);

            launchService.GameLaunchRequested += SteamLaunchService_GameLaunchRequested;

            _sessionMonitor.GameStarted += SteamGameSessionMonitor_GameStarted;

            _sessionMonitor.GameExited += SteamGameSessionMonitor_GameExited;
        }
        private void SteamLaunchService_GameLaunchRequested(uint appId) {
            Debug.WriteLine($"ControllerPlayground observed Steam game launch: {appId}");
            _sessionMonitor.StartMonitoring(appId);
        }
        private void SteamGameSessionMonitor_GameStarted(uint appId) {
            _sessionService.MarkRunning(appId);
            Debug.WriteLine($"Steam Game started: {appId}");
            GameStarted?.Invoke(appId);
        }
        private void SteamGameSessionMonitor_GameExited(uint appId) {
            _sessionService.EndSession(appId);
            Debug.WriteLine($"Steam Game exited: {appId}");
            GameExited?.Invoke(appId);
        }
    }
}
