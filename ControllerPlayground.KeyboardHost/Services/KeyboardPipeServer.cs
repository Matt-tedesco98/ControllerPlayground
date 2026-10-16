using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;
using System.Threading;


namespace ControllerPlayground.KeyboardHost.Services {
    internal sealed class KeyboardPipeServer {
        private const string PipeName = "ControllerPlayground.KeyboardHost";
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<string> _pendingCommit;
        public event Action<string>? OpenRequested;

        public async Task RunAsync() {

        }
    }
}
