using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using SteamKit2;
using SteamKit2.Authentication;
using System.Diagnostics;

namespace ControllerPlayground.Services {
    public sealed class SteamSessionService : ISteamSessionService {

        private readonly SteamClient _steamClient;
        private readonly CallbackManager _callbackManager;
        private readonly SteamUser _steamUser;

        private CancellationTokenSource _callbackCancellation;

        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public event Action? Connected;
        public event Action<string>? QrChallengeChanged;

        public SteamSessionService() {
            _steamClient = new SteamClient();

            _steamUser = _steamClient.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamUser handler was not found");

            _callbackManager = new CallbackManager(_steamClient);

            _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);

            _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
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

            Debug.WriteLine($"SteamKit connected");

            Connected?.Invoke();
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback) {
            IsConnected = false;
            IsAuthenticated = false;

            Debug.WriteLine("SteamKit disconnected");
        }

        public async Task BeginQrAuthenticationAsync(CancellationToken cancellationToken = default) {
            if (!IsConnected) { 
                throw new InvalidOperationException("Steam must be connected before starting authentication");
            }

            QrAuthSession session = await _steamClient.Authentication.BeginAuthSessionViaQRAsync(
                new AuthSessionDetails {
                    DeviceFriendlyName = "Controller Playground",
                    IsPersistentSession = true,
                });

            QrChallengeChanged?.Invoke(session.ChallengeURL);

            session.ChallengeURLChanged = () => {
                QrChallengeChanged?.Invoke(session.ChallengeURL);
            };

            AuthPollResult result = await session.PollingWaitForResultAsync(cancellationToken);

            Debug.WriteLine($"Steam QR approved for: {result.AccountName}");

            _steamUser.LogOn(
                new SteamUser.LogOnDetails {
                    Username = result.AccountName,
                    AccessToken = result.AccessToken,
                    ShouldRememberPassword = true,
                });

        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
            if (callback.Result == EResult.OK) {
                IsAuthenticated = true;
                Debug.WriteLine($"SteamKit logged on as {_steamClient.SteamID}");
            } else {
                IsAuthenticated = false;
                Debug.WriteLine($"SteamKit logon failed: {callback.Result}");
            }
        }
    }
}
