using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace ControllerPlayground.Services {
    public sealed class AudioService {
        public string GetDefaultOutputInfo() {
            using var enumerator = new MMDeviceEnumerator();

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            int volume = (int)System.Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);

            bool muted = device.AudioEndpointVolume.Mute;

            return $"{device.FriendlyName} - Volume: {volume}%" + (muted ? "(Muted)" : "");
        }

        public void AdjustVolume(float amount) {
            using var enumerator = new MMDeviceEnumerator();

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            float currentVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;

            float newVolume = Math.Clamp(currentVolume + amount, 0.0f, 1.0f);

            device.AudioEndpointVolume.MasterVolumeLevelScalar = newVolume;
        }

        public void ToggleMute() {
            using var enumerator = new MMDeviceEnumerator();

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            bool isMuted = device.AudioEndpointVolume.Mute;
            
            device.AudioEndpointVolume.Mute = !isMuted;
        }
    }
}
