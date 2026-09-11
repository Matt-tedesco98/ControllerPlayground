using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.System;

namespace ControllerPlayground.Services.Steam {
    public sealed class SteamLaunchService {
        public async Task<bool> LaunchGameAsync(uint appId) {
            Uri launchUri = new($"steam://run/{appId}");
            bool launched = await Launcher.LaunchUriAsync(launchUri);
            Debug.WriteLine(
                launched
                    ? $"Steam launch requested: {appId}"
                    : $"Steam launch failed: {appId}");

            return launched;
        }
    }
}
