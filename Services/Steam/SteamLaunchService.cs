using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.System;

namespace ControllerPlayground.Services.Steam {
    public sealed class SteamLaunchService {
        public event Action<uint>? GameLaunchRequested;
        public GameSessionService? SessionService { get; set; }
        public async Task<bool> LaunchGameAsync(uint appId) {
            if (SessionService != null && !SessionService.BeginLaunch(appId)) {
                Debug.WriteLine($"Steam launch blocked: {SessionService.State} | AppId: {SessionService.CurrentAppId}");
                return false;
            }
            Uri launchUri = new($"steam://run/{appId}");
            bool launched = await Launcher.LaunchUriAsync(launchUri);
            Debug.WriteLine(
                launched
                    ? $"Steam launch requested: {appId}"
                    : $"Steam launch failed: {appId}");
            if (!launched) {
                SessionService?.EndSession(appId);
                return false;
            }

            GameLaunchRequested?.Invoke(appId);

            return true;
        }
    }
}
