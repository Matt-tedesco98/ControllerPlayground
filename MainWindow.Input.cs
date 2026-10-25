using ControllerPlayground.Input;
using ControllerPlayground.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;

namespace ControllerPlayground;

public sealed partial class MainWindow {
    private readonly ControllerInputRouter _inputRouter;
    private ControllerDevice? _dispatchController;
    private bool _updatingScopes;
    private bool _shellForeground = true;
    private bool _keyboardRequestPending;
    private bool _controllerInputFaulted;
    private readonly Services.OwnershipTrace _ownershipTrace = new();
    private readonly Dictionary<(ControllerDeviceId Id, ulong Generation), bool> _neutralTrace = new();
    private sealed record FocusReturn(AppScreen Screen, WeakReference<Control>? Control);

    private ControllerInputRouter InitializeInputOwnership() {
        bool detailedTrace = Environment.GetEnvironmentVariable("CONTROLLERPLAYGROUND_INPUT_TRACE") == "1";
        if (detailedTrace) {
            _controllerService.DiagnosticSink = _ownershipTrace.Write;
            Services.OwnershipTrace.TemporaryInputSink = _ownershipTrace.Write;
            _ownershipTrace.Write($"TRACE_VERSION xbox-ui-investigation-2 binary={typeof(MainWindow).Assembly.Location}");
        }
        var router = new ControllerInputRouter(_controllerService.CollectorSessionId, _controllerService.SuppressForOwnershipTransition);
        router.Changed += () => {
            _ownershipTrace.Write($"SCOPE revision={router.Revision} kind={router.Current?.Kind} owner={router.Current?.Owner?.Id} gen={router.Current?.Owner?.ConnectionGeneration} recovery={router.NeedsRecovery}");
            RefreshOwnershipPresentation(); QueueActiveScopeFocus();
        };
        _controllerService.DeviceConnectionChanged += e => _ownershipTrace.Write($"DEVICE id={e.Device.Id} gen={e.Device.ConnectionGeneration} connected={e.Connected} family={e.Device.Family}");
        Closed += (_, _) => { _controllerTimer.Stop(); Services.OwnershipTrace.TemporaryInputSink = null; _ownershipTrace.Dispose(); };
        _homeView.ContextMenuStateChanged += open => ModalStateChanged(ControllerScopeKind.HomeContextMenu, open);
        _libraryView.ContextMenuStateChanged += open => ModalStateChanged(ControllerScopeKind.LibraryContextMenu, open);
        FriendsOverlay.ModalStateChanged += ModalStateChanged;
        _GamePageView.ModalStateChanged += ModalStateChanged;
        _homeView.CanRestoreFocus = () => _shellForeground && _currentScreen == AppScreen.Home && CanFocusScope(ControllerScopeKind.HomeContextMenu);
        _libraryView.CanRestoreFocus = () => _shellForeground && _currentScreen == AppScreen.Library && CanFocusScope(ControllerScopeKind.LibraryContextMenu);
        _GamePageView.CanRestoreFocus = () => _shellForeground && _currentScreen == AppScreen.GamePage &&
            (_inputRouter.Current == null || _inputRouter.Current.Kind is ControllerScopeKind.Support or ControllerScopeKind.KnownIssues);
        FriendsOverlay.CanRestoreFocus = () => _shellForeground && _inputRouter.Current?.Kind is ControllerScopeKind.Friends or ControllerScopeKind.Chat;
        PlayArea.GettingFocus += PlayArea_GettingFocus;
        PlayArea.PreviewKeyDown += PlayArea_PreviewKeyDown;
        PlayArea.PreviewKeyUp += (_, args) => FilterControllerKey(args, "up");
        PlayArea.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) => TraceBubblingKey(args, "down")), true);
        PlayArea.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) => TraceBubblingKey(args, "up")), true);
        PlayArea.GotFocus += (_, args) => Services.OwnershipTrace.Command($"FOCUS target={DescribeFocus(args.OriginalSource)}");
        return router;
    }

    private bool CanFocusScope(ControllerScopeKind kind) => _inputRouter.Current == null || _inputRouter.Current.Kind == kind;
    private FocusReturn CaptureFocus() => new(_currentScreen,
        FocusManager.GetFocusedElement(PlayArea.XamlRoot) is Control control ? new(control) : null);

    private ControllerInputScope OpenShellScope(ControllerScopeKind kind) =>
        _inputRouter.Find(kind) ?? _inputRouter.Push(kind, _dispatchController, CaptureFocus());

    private void ModalStateChanged(ControllerScopeKind kind, bool open) {
        if (_updatingScopes) return;
        if (open) OpenShellScope(kind);
        else CloseShellScope(kind);
    }

    private void CloseShellScope(ControllerScopeKind kind, bool restore = true, bool descendants = true) {
        var scope = _inputRouter.Find(kind);
        if (scope == null) return;
        _updatingScopes = true;
        try {
            var removed = _inputRouter.Remove(scope.Id, descendants);
            foreach (var item in removed) HideScopeSurface(item.Kind);
        } finally { _updatingScopes = false; }
        RefreshOwnershipPresentation();
        if (restore) QueueActiveScopeFocus(scope.FocusReturnToken as FocusReturn);
    }

    private void HideScopeSurface(ControllerScopeKind kind) {
        switch (kind) {
            case ControllerScopeKind.Guide:
                _isGuideOpen = false; OverlayLayer.Visibility = Visibility.Collapsed; break;
            case ControllerScopeKind.Friends:
                _isFriendsOpen = false; FriendsOverlayLayer.Visibility = Visibility.Collapsed; break;
            case ControllerScopeKind.Chat:
                // Parent Friends hides preserve the current chat/draft. Explicit
                // Chat dismissal calls CloseChatPane before removing its scope.
                break;
            case ControllerScopeKind.HomeContextMenu: _homeView.CloseGameContextMenu(); break;
            case ControllerScopeKind.LibraryContextMenu: _libraryView.CloseContextMenu(); break;
            case ControllerScopeKind.Support: _GamePageView.CloseNativeSupportPage(); break;
            case ControllerScopeKind.KnownIssues: _GamePageView.CloseKnownIssuesPage(); break;
            case ControllerScopeKind.Authentication: SteamQrOverlayLayer.Visibility = Visibility.Collapsed; break;
        }
    }

    private void ResetShellScopesForNavigation() {
        _updatingScopes = true;
        try {
            foreach (var scope in _inputRouter.Clear()) HideScopeSurface(scope.Kind);
        } finally { _updatingScopes = false; }
        RefreshOwnershipPresentation();
    }

    private void ProcessControllerBatch() {
        long startingRevision = _inputRouter.Revision;
        bool startedAtRoot = _inputRouter.Current == null;
        IReadOnlyList<ControllerActionEvent> actions;
        try { actions = _controllerService.PollActions(); }
        catch (Exception error) {
            _ownershipTrace.Write($"INPUT_FAULT {error.GetType().Name}: {error.Message}");
            _controllerInputFaulted = true;
            _controllerTimer.Stop();
            _inputRouter.UpdateDevices(Array.Empty<ControllerDevice>());
            RefreshOwnershipPresentation();
            return;
        }
        var awaiting = _controllerService.AwaitingNeutralDevices.Select(d => (d.Id, d.ConnectionGeneration)).ToHashSet();
        foreach (var device in _controllerService.Devices) {
            var key = (device.Id, device.ConnectionGeneration);
            bool waiting = awaiting.Contains(key);
            if (!_neutralTrace.TryGetValue(key, out bool previous) || previous != waiting) {
                _ownershipTrace.Write($"NEUTRAL id={device.Id} gen={device.ConnectionGeneration} waiting={waiting}");
                _neutralTrace[key] = waiting;
            }
        }
        _inputRouter.BeginBatch(actions);
        _inputRouter.UpdateDevices(_controllerService.Devices);
        foreach (var input in actions) {
            // Keep ordinary input out of the background shell, without changing
            // GameInput focus policies or suppressing input to other processes.
            if (!_shellForeground && input.Action != ControllerAction.Guide) continue;
            var decision = _inputRouter.Route(input); // BEFORE Guide/View handling.
            _ownershipTrace.Write($"ACTION seq={input.Sequence} id={input.Device.Id} gen={input.Device.ConnectionGeneration} nativeTime={input.NativeTimestampMicroseconds} observedMs={input.ObservedAtMilliseconds} action={input.Action} repeat={input.IsRepeat} result={decision} scope={_inputRouter.Current?.Kind} focus={CurrentFocus()}");
            if (decision != ControllerRouteDecision.Accepted) continue;
            _dispatchController = input.Device;
            Services.OwnershipTrace.ActionContext = $"seq={input.Sequence} id={input.Device.Id} gen={input.Device.ConnectionGeneration} action={input.Action}";
            try {
                Services.OwnershipTrace.Command($"DISPATCH screen={_currentScreen} scope={_inputRouter.Current?.Kind} before={CurrentFocus()}");
                ControllerService_ControllerFamilyChanged((_inputRouter.Current?.Owner ?? input.Device).Family);
                DispatchShellAction(input.Action);
                Services.OwnershipTrace.Command($"DISPATCH_DONE screen={_currentScreen} scope={_inputRouter.Current?.Kind} after={CurrentFocus()}");
            } finally { _dispatchController = null; Services.OwnershipTrace.ActionContext = null; }
        }
        // Preserve legacy fallback only at the root: there is no reliable owner
        // to authorize a shortcut inside an existing interaction.
        if (_controllerService.UnattributedGuideRequested && startedAtRoot && _inputRouter.Current == null &&
            _inputRouter.Revision == startingRevision && !actions.Any(a => a.Action == ControllerAction.Guide)) ToggleGuideScope();
    }

    private void DispatchShellAction(ControllerAction action) {
        if (action == ControllerAction.Accept && ReferenceEquals(FocusManager.GetFocusedElement(PlayArea.XamlRoot), CloseOwnershipButton)) {
            DismissTopShellScope(); return;
        }
        if (action == ControllerAction.Guide) { ToggleGuideScope(); return; }
        if (_inputRouter.Current?.Kind == ControllerScopeKind.Guide) {
            if (action == ControllerAction.Back) CloseShellScope(ControllerScopeKind.Guide);
            else GuideMenu.HandleControllerAction(action);
            return;
        }
        if (action == ControllerAction.View) { ToggleFriendsScope(); return; }
        if (_isFriendsOpen && _inputRouter.Current?.Kind is ControllerScopeKind.Friends or ControllerScopeKind.Chat) {
            if (action == ControllerAction.Back) {
                if (FriendsOverlay.IsChatOpen) FriendsOverlay.CloseChatPane();
                else CloseFriendsScope();
            } else FriendsOverlay.HandleControllerAction(action);
            return;
        }
        if (action == ControllerAction.Back) {
            if (_inputRouter.Current != null) { DismissTopShellScope(); return; }
            GoBack(); return;
        }
        object? focused = FocusManager.GetFocusedElement(PlayArea.XamlRoot);
        if (action == ControllerAction.Accept && ReferenceEquals(focused, ShellGuideButton)) { ToggleGuideScope(); return; }
        if (action == ControllerAction.Accept && ReferenceEquals(focused, ShellFriendsButton)) { ToggleFriendsScope(); return; }
        if (focused == null || ReferenceEquals(focused, PlayArea)) { QueueActiveScopeFocus(); return; }
        switch (_currentScreen) {
            case AppScreen.Home: _homeView.HandleControllerAction(action); break;
            case AppScreen.Library: _libraryView.HandleControllerAction(action); break;
            case AppScreen.GamePage: _GamePageView.HandleControllerAction(action); break;
            case AppScreen.Settings: _settingsView.HandleControllerAction(action); break;
        }
    }

    private void ToggleGuideScope() {
        Services.OwnershipTrace.Command($"COMMAND ToggleGuide open={_isGuideOpen}");
        if (_inputRouter.Current?.BlocksControllerInput == true) return;
        if (_isGuideOpen) { CloseShellScope(ControllerScopeKind.Guide); return; }
        OpenShellScope(ControllerScopeKind.Guide);
        _isGuideOpen = true;
        OverlayLayer.Visibility = Visibility.Visible;
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        Activate();
        _gameWindowService.TryFocusWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        RefreshOwnershipPresentation(); QueueActiveScopeFocus();
    }

    private void ToggleFriendsScope() {
        if (_inputRouter.Current?.BlocksControllerInput == true) return;
        if (_isFriendsOpen) { CloseFriendsScope(); return; }
        OpenShellScope(ControllerScopeKind.Friends);
        _isFriendsOpen = true;
        FriendsOverlayLayer.Visibility = Visibility.Visible;
        if (FriendsOverlay.IsChatOpen) OpenShellScope(ControllerScopeKind.Chat);
        RefreshOwnershipPresentation(); QueueActiveScopeFocus();
    }
    private void CloseFriendsScope() => CloseShellScope(ControllerScopeKind.Friends);

    private void DismissTopShellScope() {
        Services.OwnershipTrace.Command($"COMMAND DismissScope kind={_inputRouter.Current?.Kind}");
        var scope = _inputRouter.Current;
        if (scope == null || scope.Kind == ControllerScopeKind.KeyboardHandoff) return;
        if (scope.Kind == ControllerScopeKind.Chat) FriendsOverlay.CloseChatPane();
        else CloseShellScope(scope.Kind);
    }

    private UIElement? ActiveFocusRoot() => _inputRouter.Current?.Kind switch {
        ControllerScopeKind.Guide => GuideMenu,
        ControllerScopeKind.Friends or ControllerScopeKind.Chat or ControllerScopeKind.KeyboardHandoff => FriendsOverlay,
        ControllerScopeKind.HomeContextMenu => _homeView.ContextFocusRoot,
        ControllerScopeKind.LibraryContextMenu => _libraryView.ContextFocusRoot,
        ControllerScopeKind.Support => _GamePageView.SupportFocusRoot,
        ControllerScopeKind.KnownIssues => _GamePageView.KnownIssuesFocusRoot,
        ControllerScopeKind.Authentication => SteamQrOverlayLayer,
        _ => null
    };
    private static bool IsWithin(DependencyObject? element, DependencyObject root) {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current == root) return true;
        return false;
    }
    private void PlayArea_GettingFocus(UIElement sender, GettingFocusEventArgs args) {
        Services.OwnershipTrace.Command($"FOCUS_REQUEST old={DescribeFocus(args.OldFocusedElement)} new={DescribeFocus(args.NewFocusedElement)} scope={_inputRouter.Current?.Kind}");
        var root = ActiveFocusRoot();
        if (root == null || args.NewFocusedElement == null) return;
        if (IsWithin(args.NewFocusedElement, root) || IsWithin(args.NewFocusedElement, OwnershipPanel)) return;
        args.TryCancel();
        QueueActiveScopeFocus();
    }
    private void PlayArea_PreviewKeyDown(object sender, KeyRoutedEventArgs args) {
        // WinUI keys without a device identity must not bypass the GameInput
        // router. Physical keys (Escape, Tab, Enter, arrows) remain available.
        if (FilterControllerKey(args, "down")) return;
        if (args.Key == VirtualKey.Escape && _inputRouter.Current != null) {
            DismissTopShellScope(); args.Handled = true;
        } else if (args.Key == VirtualKey.F1) {
            ToggleGuideScope(); args.Handled = true;
        } else if (args.Key == VirtualKey.F10 && _inputRouter.Current == null) {
            DispatchShellAction(ControllerAction.Menu); args.Handled = true;
        }
    }
    private static string DescribeFocus(object? element) => element is FrameworkElement view ? $"{view.GetType().Name}#{view.Name}" : element?.GetType().Name ?? "none";
    private string CurrentFocus() => DescribeFocus(FocusManager.GetFocusedElement(PlayArea.XamlRoot));
    private bool FilterControllerKey(KeyRoutedEventArgs args, string phase) {
        bool suppress = ShellKeyPolicy.SuppressUnattributedControllerKey((uint)args.Key, (uint)args.OriginalKey);
        Services.OwnershipTrace.Command($"XAML_KEY phase={phase} key={args.Key} original={args.OriginalKey} xamlDevice={args.DeviceId} repeat={args.KeyStatus.RepeatCount} suppressed={suppress} focus={CurrentFocus()}");
        if (suppress) args.Handled = true;
        return suppress;
    }
    private void TraceBubblingKey(KeyRoutedEventArgs args, string phase) =>
        Services.OwnershipTrace.Command($"XAML_BUBBLE phase={phase} key={args.Key} original={args.OriginalKey} handled={args.Handled} source={DescribeFocus(args.OriginalSource)} focus={CurrentFocus()}");
    private void ShellGuideButton_Click(object sender, RoutedEventArgs args) => ToggleGuideScope();
    private void ShellFriendsButton_Click(object sender, RoutedEventArgs args) => ToggleFriendsScope();
    private void CloseOwnershipButton_Click(object sender, RoutedEventArgs args) => DismissTopShellScope();

    private void RefreshOwnershipPresentation() {
        var scope = _inputRouter.Current;
        bool external = scope?.Kind is ControllerScopeKind.Guide or ControllerScopeKind.Friends or ControllerScopeKind.Chat or ControllerScopeKind.Authentication or ControllerScopeKind.KeyboardHandoff;
        ScreenHost.IsEnabled = !external;
        GuideMenu.IsEnabled = scope?.Kind == ControllerScopeKind.Guide;
        FriendsOverlay.IsEnabled = scope?.Kind is ControllerScopeKind.Friends or ControllerScopeKind.Chat;
        ShellNavigationButtons.IsHitTestVisible = scope == null;
        ShellGuideButton.IsEnabled = ShellFriendsButton.IsEnabled = scope == null;
        OwnershipPanel.Visibility = scope == null ? Visibility.Collapsed : Visibility.Visible;
        CloseOwnershipButton.IsEnabled = scope?.Kind != ControllerScopeKind.KeyboardHandoff;
        if (scope == null) return;
        if (scope.Owner is { } owner) ControllerService_ControllerFamilyChanged(owner.Family);
        string label = scope.Kind switch {
            ControllerScopeKind.Guide => "Guide", ControllerScopeKind.Friends => "Friends", ControllerScopeKind.Chat => "Chat",
            ControllerScopeKind.HomeContextMenu or ControllerScopeKind.LibraryContextMenu => "Game menu",
            ControllerScopeKind.Support => "Game support", ControllerScopeKind.KnownIssues => "Known issues",
            ControllerScopeKind.Authentication => "Steam sign-in", _ => "Text entry"
        };
        OwnershipStatusText.Text = scope.Kind == ControllerScopeKind.KeyboardHandoff ? "Finish or cancel text entry to return." :
            _controllerInputFaulted ? "Controller input unavailable. Use mouse/keyboard, or restart to retry." :
            scope.Kind == ControllerScopeKind.Authentication ? "Steam sign-in · Esc to close" :
            _inputRouter.NeedsRecovery ? scope.Owner == null ? $"{label} · Press A/Cross to control this menu, or use mouse/keyboard." :
                "Controller disconnected. Release controls, then press A/Cross to take over, or close." :
            $"{label} · {(scope.Owner?.Family == ControllerFamily.PlayStation ? "PlayStation controller" : scope.Owner?.Family == ControllerFamily.Xbox ? "Xbox controller" : "Controller")} · Esc to close";
    }

    private void QueueActiveScopeFocus(FocusReturn? restore = null) {
        long revision = _inputRouter.Revision;
        DispatcherQueue.TryEnqueue(() => {
            if (!_shellForeground || _inputRouter.Revision != revision || _inputRouter.Current?.Kind == ControllerScopeKind.KeyboardHandoff) return;
            var root = ActiveFocusRoot();
            if (restore != null && restore.Screen == _currentScreen && restore.Control != null && restore.Control.TryGetTarget(out var target) &&
                target.XamlRoot == PlayArea.XamlRoot && target.IsEnabled && target.Visibility == Visibility.Visible &&
                (root == null || IsWithin(target, root)) && target.Focus(FocusState.Programmatic)) return;
            if (_inputRouter.Current is { Owner: not null } && _inputRouter.NeedsRecovery) { CloseOwnershipButton.Focus(FocusState.Programmatic); return; }
            if (root != null && FocusManager.GetFocusedElement(PlayArea.XamlRoot) is DependencyObject focused && IsWithin(focused, root)) return;
            switch (_inputRouter.Current?.Kind) {
                case ControllerScopeKind.Guide: GuideMenu.FocusFirstItem(); break;
                case ControllerScopeKind.Friends: FriendsOverlay.FocusFirstItem(); break;
                case ControllerScopeKind.Chat: FriendsOverlay.RestoreChatInputFocus(); break;
                case ControllerScopeKind.HomeContextMenu: _homeView.FocusContextMenu(); break;
                case ControllerScopeKind.LibraryContextMenu: _libraryView.FocusContextMenu(); break;
                case ControllerScopeKind.Support:
                case ControllerScopeKind.KnownIssues: _GamePageView.FocusInitialElement(); break;
                case ControllerScopeKind.Authentication: CloseOwnershipButton.Focus(FocusState.Programmatic); break;
                default: RestoreScreenFocus(); break;
            }
        });
    }
    private void RestoreCurrentScreenFocus() => QueueActiveScopeFocus();

    private void ShowAuthenticationScope() {
        OpenShellScope(ControllerScopeKind.Authentication);
        SteamQrOverlayLayer.Visibility = Visibility.Visible;
        RefreshOwnershipPresentation(); QueueActiveScopeFocus();
    }

    private async Task RequestOwnedKeyboardAsync(string initialText) {
        if (_keyboardRequestPending) return;
        var parent = _inputRouter.Find(ControllerScopeKind.Chat);
        if (parent == null) return;
        long chatVersion = FriendsOverlay.ChatContextVersion;
        var handoff = OpenShellScope(ControllerScopeKind.KeyboardHandoff);
        _keyboardRequestPending = true;
        RefreshOwnershipPresentation();
        try {
            var request = _keyboardHostClient.RequestTextAsync(initialText, handle => {
                if (_inputRouter.Current?.Id == handoff.Id) _gameWindowService.TryFocusWindow(handle);
            });
            string? text = request == null ? null : await request;
            bool ownerConnected = handoff.Owner == null || _controllerService.Devices.Any(d =>
                d.Id == handoff.Owner.Id && d.ConnectionGeneration == handoff.Owner.ConnectionGeneration);
            if (text != null && ownerConnected && FriendsOverlay.ChatContextVersion == chatVersion && _inputRouter.Find(ControllerScopeKind.Chat)?.Id == parent.Id)
                FriendsOverlay.SetChatMessageText(text);
        } catch (Exception error) { Debug.WriteLine($"Keyboard request failed: {error}"); }
        finally {
            _keyboardRequestPending = false;
            bool restore = _inputRouter.Find(ControllerScopeKind.KeyboardHandoff)?.Id == handoff.Id &&
                _inputRouter.Find(ControllerScopeKind.Chat)?.Id == parent.Id;
            CloseShellScope(ControllerScopeKind.KeyboardHandoff, descendants: false);
            if (restore) {
                _gameWindowService.TryFocusWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
                QueueActiveScopeFocus();
            }
        }
    }
}
