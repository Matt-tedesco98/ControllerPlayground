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
        public MainWindow() {
            InitializeComponent();

            _keyboardPipeServer.OpenRequested += KeyboardPipeServer_OpenRequested;

            Keyboard.TextCommitted += Keyboard_TextCommitted;

            Keyboard.Cancelled += Keyboard_Cancelled;

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
            Keyboard.FocusFirstKey();

            root.Loaded -= Root_Loaded;
        }

        private void ControllerTimer_Tick(object? sender, object e) {
            ControllerAction action = _controllerService.PollAction();

            if (action == ControllerAction.none)
                return;

            System.Diagnostics.Debug.WriteLine($"Controller action: {action}");

            Keyboard.HandleControllerAction(action);
        }

        private Task KeyboardPipeServer_OpenRequested(string initialText) {
            TaskCompletionSource<bool> completion = new();

            DispatcherQueue.TryEnqueue(() => {
                Keyboard.SetText(initialText);
                
                if (AppWindow.Presenter is OverlappedPresenter presenter) {
                    presenter.Restore();
                }

                Activate();

                Keyboard.FocusFirstKey();

                completion.SetResult(true);
            });

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
    }
}
