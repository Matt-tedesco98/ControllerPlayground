using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using SteamKit2;

namespace ControllerPlayground.Services {
    public sealed class SteamSessionService : ISteamSessionService {

        private readonly SteamClient _steamClient;
        private readonly CallbackManager _callbackManager;

        private CancellationTokenSource _callbackCancellation;

        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public SteamSessionService() {
            _steamClient = new SteamClient();
            _callbackManager = new CallbackManager(_steamClient);

            _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);

            _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        }

        public void Connect() {
            if (IsConnected)
                return;

            _callbackCancellation = new CancellationTokenSource();

            _ = Task.Run(() =>
                RunCallBacks(_callbackCancellation.Token));

            _steamClient.Connect();
        }

        public void Disconnect() {
            _callbackCancellation?.Cancel();
            _steamClient.Disconnect();

            IsConnected = false;
            IsAuthenticated = false;
        }

        private void RunCallBacks(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }

        private void OnConnected(SteamClient.ConnectedCallback callback) {
            IsConnected = true;
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback) {
            IsConnected = false;
            IsAuthenticated = false;
        }
    }
}
