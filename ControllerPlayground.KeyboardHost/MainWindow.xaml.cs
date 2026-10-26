using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System.Runtime.InteropServices;
using ControllerPlayground.Input;
using ControllerPlayground.KeyboardHost.Services;
using System.Threading.Tasks;
using Windows.UI;
using ControllerPlayground.Input.Keyboard;
using System.Threading;
using System.Collections.Immutable;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.KeyboardHost {
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        private readonly ControllerService _controllerService = new();
        private readonly DispatcherTimer _controllerTimer = new();
        private readonly KeyboardPipeServer _keyboardPipeServer = new();
        private readonly ControllerPlayground.Input.Diagnostics.KeyboardPhase0Probe? _phase0;
        private bool _isMoveMode;
        private readonly bool _deferInitialActivation;
        private Guid? _focusedV2Session;
        private readonly TaskCompletionSource _rootReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MainWindow(bool deferInitialActivation = false) {
            _deferInitialActivation = deferInitialActivation;
            InitializeComponent();
            _phase0 = ControllerPlayground.Input.Diagnostics.KeyboardPhase0Probe.TryStart("helper", _controllerService,
                action => DispatcherQueue.TryEnqueue(() => action()));
            Closed += (_, _) => _phase0?.Dispose();

            _controllerService.ControllerFamilyChanged += family => {
                if (!_keyboardPipeServer.ActiveVersion2) Keyboard.SetControllerFamily(family);
            };

            _keyboardPipeServer.ControllerBridge = new() {
                HostOwnerChanged = (binding, current, token) => OnKeyboardUi(() => {
                    if (!current()) throw new OperationCanceledException();
                    _controllerService.ResyncInputState();
                    Keyboard.SetControllerFamily(binding.ShellDevice.Family);
                    return true;
                }, token),
                HostExecute = (input, authorize, token) => OnKeyboardUi(() => {
                    // Refresh live connection/focus before a queued UI command. This
                    // uses the same capture method as the timer and retains its batch.
                    CaptureMediatedInput();
                    return _keyboardPipeServer.ActiveMediated && IsKeyboardForeground() &&
                        _controllerService.Devices.Any(d => KeyboardControllerBinding.Describe(_controllerService.CollectorSessionId, d) == input.Device) &&
                        authorize(() => HandleKeyboardControllerAction(input.Action));
                }, token)
            };

            _keyboardPipeServer.OpenRequested += KeyboardPipeServer_OpenRequested;
            _keyboardPipeServer.CloseRequested += KeyboardPipeServer_CloseRequested;
            _keyboardPipeServer.Trace = message => Debug.WriteLine(message);
            Closed += (_, _) => { _controllerTimer.Stop(); _keyboardPipeServer.Dispose(); };
            Activated += (_, args) => {
                if (args.WindowActivationState != WindowActivationState.Deactivated && _keyboardPipeServer.ActiveVersion2 &&
                    _keyboardPipeServer.ActiveSessionId is { } id && _focusedV2Session != id) {
                    _focusedV2Session = id;
                    Keyboard.FocusFirstKey(() => _keyboardPipeServer.IsCurrent(id));
                }
            };

            Keyboard.TextCommitted += Keyboard_TextCommitted;

            Keyboard.Cancelled += Keyboard_Cancelled;

            Keyboard.MoveRequested += Keyboard_MoveRequested;

            _ = _keyboardPipeServer.RunAsync();

            nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

            _keyboardPipeServer.WindowHandle = windowHandle;

            ControllerInputNative.ControllerInput_SetWindowHandle(windowHandle);

            bool controllerInitialized = ControllerInputNative.ControllerInput_Initialize();

            if (controllerInitialized) {
                _controllerTimer.Interval = TimeSpan.FromMilliseconds(16);
                _controllerTimer.Tick += ControllerTimer_Tick;
                _controllerTimer.Start();
            }

            if (Content is FrameworkElement root) {
                root.Loaded += Root_Loaded;
                root.PreviewKeyDown += (_, args) => {
                    if (_keyboardPipeServer.ActiveVersion2 && ShellKeyPolicy.SuppressUnattributedControllerKey((uint)args.Key, (uint)args.OriginalKey)) args.Handled = true;
                };
                root.PreviewKeyUp += (_, args) => {
                    if (_keyboardPipeServer.ActiveVersion2 && ShellKeyPolicy.SuppressUnattributedControllerKey((uint)args.Key, (uint)args.OriginalKey)) args.Handled = true;
                };
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;

                presenter.SetBorderAndTitleBar(false, false);
            }
        }
        private void DragArea_PointerPressed(object sender, PointerRoutedEventArgs e) {
            nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

            ReleaseCapture();

            SendMessage(windowHandle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, 0);
        }

        private void Root_Loaded(object sender, RoutedEventArgs e) {
            if (Content is not FrameworkElement root ||
                root.XamlRoot == null) {
                return;
            }

            double scale =
                root.XamlRoot.RasterizationScale;

            int width =
                (int)(1200 * scale);

            int height =
                (int)(540 * scale);

            AppWindow.Resize(
                new SizeInt32(
                    width,
                    height));
            _rootReady.TrySetResult();
            if (!_deferInitialActivation && !_keyboardPipeServer.ActiveVersion2) Keyboard.FocusFirstKey();

            root.Loaded -= Root_Loaded;
        }

        private void ControllerTimer_Tick(object? sender, object e) {
            if (_keyboardPipeServer.ActiveMediated) {
                CaptureMediatedInput();
                _phase0?.AfterPoll();
                return; // Never also execute the scalar path for a mediated session.
            }
            ControllerAction action =
                _controllerService.PollAction();
            _phase0?.AfterPoll();

            if (action == ControllerAction.none)
                return;

            HandleKeyboardControllerAction(action);
        }
        private ulong _mediatedCaptureSequence;
        private void CaptureMediatedInput() {
            if (!_keyboardPipeServer.ActiveMediated || _keyboardPipeServer.ActiveSessionId is not { } session) return;
            var actions = _controllerService.PollActions();
            var devices = _controllerService.Devices.Select(d => KeyboardControllerBinding.Describe(_controllerService.CollectorSessionId, d)).ToImmutableArray();
            var awaiting = _controllerService.AwaitingNeutralDevices.Select(d => KeyboardControllerBinding.Describe(_controllerService.CollectorSessionId, d)).ToImmutableHashSet();
            var snapshot = new KeyboardCaptureSnapshot(session, _controllerService.CollectorSessionId, devices,
                devices.Where(d => !awaiting.Contains(d)).ToImmutableHashSet(), IsKeyboardForeground(),
                KeyboardControllerBinding.OrderBatch(actions, ref _mediatedCaptureSequence));
            if (_keyboardPipeServer.PublishCapture(snapshot)) _controllerService.ResyncInputState();
        }
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        private bool IsKeyboardForeground() => GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);
        private Task<T> OnKeyboardUi<T>(Func<T> action, CancellationToken token) {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() => {
                try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new IOException("Keyboard dispatcher closed."));
            return completion.Task.WaitAsync(token);
        }
        private void HandleKeyboardControllerAction(ControllerAction action) {

            if (_isMoveMode) {
                PointInt32 position =
                    AppWindow.Position;

                const int moveStep = 32;

                switch (action) {
                    case ControllerAction.NavigateUp:
                        AppWindow.Move(
                            new PointInt32(
                                position.X,
                                position.Y - moveStep));
                        return;

                    case ControllerAction.NavigateDown:
                        AppWindow.Move(
                            new PointInt32(
                                position.X,
                                position.Y + moveStep));
                        return;

                    case ControllerAction.NavigateLeft:
                        AppWindow.Move(
                            new PointInt32(
                                position.X - moveStep,
                                position.Y));
                        return;

                    case ControllerAction.NavigateRight:
                        AppWindow.Move(
                            new PointInt32(
                                position.X + moveStep,
                                position.Y));
                        return;

                    case ControllerAction.Accept:
                    case ControllerAction.Back:
                        _isMoveMode = false;

                        Keyboard.SetMoveModeActive(false);
                        return;
                }
            }

            Keyboard.HandleControllerAction(action);
        }

        private Task<KeyboardReady> KeyboardPipeServer_OpenRequested(KeyboardUiRequest request) {
            var completion = new TaskCompletionSource<KeyboardReady>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(async () => {
                try {
                    request.Cancellation.ThrowIfCancellationRequested();
                    if (!_keyboardPipeServer.IsCurrent(request.SessionId)) throw new OperationCanceledException();
#if DEBUG
                    if (request.Version2 && System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "phase2-delay-open.log")))
                        await Task.Delay(500, request.Cancellation);
#endif
                    Keyboard.SetText(request.Text);
                    if (request.Version2) _controllerService.ResyncInputState();
                    else Keyboard.SetControllerFamily(_controllerService.Family);
                    if (request.Version2) AppWindow.Show(false);
                    else {
                        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
                        Activate(); Keyboard.FocusFirstKey(() => _keyboardPipeServer.IsCurrent(request.SessionId));
                    }
                    await _rootReady.Task.WaitAsync(request.Cancellation);
                    request.Cancellation.ThrowIfCancellationRequested();
                    if (!_keyboardPipeServer.IsCurrent(request.SessionId)) throw new OperationCanceledException();
                    var devices = _controllerService.Devices.Select(d => new KeyboardDeviceRef(_controllerService.CollectorSessionId,
                        d.Id.ToString(), d.RootId.ToString(), d.ConnectionGeneration, d.Family)).ToImmutableArray();
                    completion.TrySetResult(new(WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64(),
                        (uint)Environment.ProcessId, _controllerService.CollectorSessionId, devices));
                } catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new IOException("Keyboard dispatcher closed."));
            return completion.Task.WaitAsync(request.Cancellation);
        }
        private Task KeyboardPipeServer_CloseRequested(Guid session) {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() => {
                if (_keyboardPipeServer.IsCurrent(session)) {
                    if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
                }
                completion.TrySetResult();
            })) completion.TrySetResult();
            return completion.Task;
        }

        private void Keyboard_TextCommitted(string text) {
            if (AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.Minimize();
            }

            _keyboardPipeServer.Commit(text);
        }

        public void Keyboard_Cancelled() {
            if (AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.Minimize();
            }
            _keyboardPipeServer.Cancel();
        }

        private void Keyboard_MoveRequested() {
            _isMoveMode = true;

            Keyboard.SetMoveModeActive(true);
        }
    }
}
