using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Services.Steam {
    public enum GameSessionState {
        Idle,
        Launching,
        Running,
    }
    public sealed class GameSessionService {
        public GameSessionState State { get; private set; } = GameSessionState.Idle;
        public uint? CurrentAppId { get; private set; }
        public event Action<GameSessionState, uint?>? StateChanged;
        public bool BeginLaunch(uint appId) {
            if (State != GameSessionState.Idle)
                return false;

            CurrentAppId = appId;

            State = GameSessionState.Launching;

            StateChanged?.Invoke(State, CurrentAppId);
            return true;
        }
        public void MarkRunning(uint appId) {
            if (CurrentAppId != appId)
                return;
            State = GameSessionState.Running;
            StateChanged?.Invoke(State, CurrentAppId);
        }
        public void EndSession(uint appId) {
            if (CurrentAppId != appId)
                return;

            CurrentAppId = null;
            State = GameSessionState.Idle;

            StateChanged?.Invoke(State, null);
        }
    }
}
