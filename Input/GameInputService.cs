using System;
using System.Runtime.InteropServices;

namespace ControllerPlayground.Input {
    internal class GameInputService {
        [DllImport("GameInput.dll", ExactSpelling = true)]
        private static extern int GameInputCreate(
            out IGameInput gameInput
        );

        private IGameInput? _gameInput;

        public bool IsInitialized { get; private set; }

        public GameInputService() {
            int result = GameInputCreate(out IGameInput gameInput);

            if (result >= 0) {
                _gameInput = gameInput;
                IsInitialized = true;
            } else {
                _gameInput = null;
                IsInitialized = false;
            }
        }

        public bool TryGetGamepadState(out GameInputGamepadState state) {
            state = default;

            if (_gameInput == null)
                return false;

            int result = _gameInput.GetCurrentReading(
                GameInputKind.Gamepad,
                IntPtr.Zero,
                out IGameInputReading reading
            );

            if (result < 0 || reading == null)
                return false;

            try {
                return reading.GetGamepadState(out state);
            } finally {
                if (Marshal.IsComObject(reading)) {
                    Marshal.ReleaseComObject(reading);
                }
            }
        }
    }
}