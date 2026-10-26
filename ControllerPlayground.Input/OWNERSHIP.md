# Shell controller ownership — Increment 3

Current architecture: [MULTI_CONTROLLER.md](MULTI_CONTROLLER.md). The agreed
Xbox + PS5 DualSense Phase 4A end-to-end checks are complete; see
[the verification report](../tools/phase4a/README.md). Steam Controller is deferred;
Windows x64 packaging verification remains subject to separate approval.

This document retains the Increment 3 shell acceptance record. Cross-process
KeyboardHost ownership subsequently passed Increment 4 Phase 3 final acceptance;
see the current milestone record below and
[the Phase 3 report](../tools/phase3/README.md). Statements below that exclude
KeyboardHost ownership describe the historical Increment 3 boundary.

## Increment 4 Phase 3 acceptance — October 25, 2026

The user verified the final callback-deadline deployment with matching shell/helper
hashes. Repeated opening/closing, delayed-callback cancellation, disconnect takeover,
stale-command rejection and helper termination/reopening all passed. Earlier Xbox
and DualSense tests also verified owner-only typing/commit/cancel, simultaneous
RT/Shift, authorized glyph updates, reconnect gating and safe focus restoration.
Recovery consumes a fresh A/Cross claim without typing and never silently revives
an expired generation. Shell Guide/View shortcuts stay blocked during the handoff.

All 109 Debug/Release test groups and all 28 reflection-free protocol/session groups
passed. Full Debug builds passed; default Release ReadyToRun NETSDK1094 remains
tracked with the existing command-only workaround. Unmatched cross-process full
device identities fail closed; other package/controller combinations remain
unverified. Steam Input Desktop Layout mappings can still inject independent
keyboard/mouse input and were disabled for accepted hardware tests.

Phase 3 is finalized on the separate Phase 2 checkpoint `9cba473`; no GameInput
focus-policy changes or Phase 4 work are included.

## Historical Increment 3 acceptance

Increment 3 passed all manual Xbox + DualSense hardware tests on October 25,
2026, as confirmed by the user. Verified behaviors are shared shell navigation,
exclusive Guide ownership, rejection of non-owner Guide inputs, nested focus
restoration, disconnect takeover, held-input suppression and reconnect behavior,
and correct Xbox D-pad, analog-stick and B behavior. These are user-performed
hardware results, separate from the automated tests below.

The subsequent [Xbox navigation investigation](XBOX_NAVIGATION_INVESTIGATION.md)
documents a corrected WinUI key-filter bypass, temporary diagnostics and the
additional eight regression groups. The final hardware result supersedes the
earlier investigation's pending-test notes.

Increment 2 was checkpointed as `727af96` before these changes. Increment 3
uses the attributed event batch in MainWindow; KeyboardHost retains its scalar
compatibility adapter. Native exports, GameInput focus policies, and KeyboardHost
IPC are unchanged.

## Routing and recovery

`ControllerInputRouter` is UI-independent and thread-affine. At the root, any
connected controller can navigate. Guide, Friends/chat, game context menus,
support, and known-issues interactions push nested scopes. Ownership uses the
complete endpoint ID plus connection generation; child scopes inherit it.
Permission checks precede global Guide/View shortcuts. Non-owner actions are
discarded immediately, with no deferred action queue.

Every scope transition invalidates remaining actions in the already-produced
poll batch and requests a release/neutral boundary on every controller. Normal
navigation cannot leak across opening, closing, or transferring an interaction.
A fresh native Guide edge remains usable after an ownership transition while
ordinary background readings are unavailable; this exception cannot weaken an
existing strict reconnect or lost-history neutral gate.

If the owner disconnects, its generation cannot regain the lease automatically.
After release/neutral, a fresh A/Cross claims the interaction for any connected
controller. That first press is consumed; release and press again to activate.
The transfer also updates suspended parent scopes. Disconnecting a non-owner
does not reset the owner. Pointer/keyboard-opened interactions initially have no
controller owner and use the same explicit claim behavior.

Focus restoration captures a weak control reference and screen identity. Scope
revisions invalidate obsolete dispatcher work; detached controls and controls
outside the active surface are rejected. Page-specific queued focus callbacks
also check the current scope. Closing a parent unwinds children. Asynchronous
authentication/keyboard completion removes only its own scope when appropriate.

Mouse buttons open Guide/Friends and dismiss interactions. Physical Escape
closes the current interaction, F1 toggles Guide, and F10 opens the focused game
menu at the root. Tab/Enter/arrows retain WinUI behavior. Unattributed WinUI
Gamepad virtual keys are blocked locally to prevent duplicate router bypass.
Keyboard events generated by third-party controller-to-keyboard mappings cannot
be distinguished from physical keys and remain a recovery path.

Authentication blocks controller shortcuts; Escape closes its surface without
cancelling the underlying Steam authentication operation. During external text
entry the shell holds a blocking scope: finish/cancel KeyboardHost to return
(Alt+F4 on its window is also available). Returned text and focus restoration
are checked against the surviving chat/lease and owner connection. This does
**not** assign the helper's controller: cross-process ownership is deferred.

Launching a game clears shell UI leases. Ordinary background shell actions are
ignored, while existing Guide activation remains available. No OS hooks, global
input suppression, or changes to input delivered to gameplay are introduced.

## Steam Input Desktop Layout compatibility

The navigation anomalies were caused by Steam Input Desktop Layout mappings
injecting keyboard/mouse input alongside GameInput. Disabling those desktop
mappings eliminated tile skipping and B-triggered activation in the user's
Xbox + DualSense hardware tests. This finding concerns desktop mappings; it
does not require disabling Steam Input for gameplay profiles.

The shell filters identifiable WinUI Gamepad/Navigation key events so that they
cannot bypass ownership. Steam-emulated ordinary keyboard/mouse events cannot
be reliably distinguished from physical keyboard/mouse recovery input. Avoid
active Desktop Layout mappings that duplicate shell actions while using
ControllerPlayground. The shell does not alter Steam's configuration, suppress
ordinary keyboard/mouse input, or change GameInput foreground/background policy.

## Files

- `ControllerPlayground.Input/ControllerInputRouter.cs`: scope stack, permissions,
  generation validation, batch barriers, recovery, focus revision tokens.
- `ControllerDeviceState.cs`, `ControllerService.cs`: ownership release gate,
  neutral diagnostics, legacy unattributed Guide indication.
- `MainWindow.Input.cs`, `MainWindow.xaml.cs`, `MainWindow.xaml`: routing, surface
  lifecycle, mouse/keyboard recovery, focus and asynchronous handoff guards.
- `Views/HomeView.xaml[.cs]`, `Views/LibraryView.xaml[.cs]`,
  `Views/GamePageView.xaml.cs`, `Overlays/FriendsOverlay.xaml.cs`: scope lifecycle,
  modal backdrops, focus guards and chat version tracking.
- `Overlays/GuideMenu.xaml[.cs]`, `Overlays/GameContextMenu.xaml[.cs]`,
  `Controls/SteamFriendCard.xaml.cs`: pointer/physical keyboard activation paths.
- `Services/OwnershipTrace.cs`: Debug-only metadata file logging.
- `ControllerPlayground.Input.Tests/OwnershipTests.cs`, `Program.cs`,
  `NavigationRegressionTests.cs`, `ControllerMonitor.cs`,
  `OwnershipDiagnosticSession.cs`: tests and optional
  native hardware/router diagnostic.

## Automated and manual verification

Run the dependency-free runner in Debug and Release. It contains the existing
21 input groups plus 19 ownership and 8 navigation regression groups (48 total),
including integrated synthetic held input/disconnect/reconnect tests for both
controller families. These tests do
not establish actual WinUI focus or hardware neutral-gating behavior.

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug
dotnet run --project ControllerPlayground.Input.Tests -c Release
```

After building x64 Debug, close shell/KeyboardHost before this standalone tool:

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug --no-build -- --ownership-monitor
```

It runs for two minutes (optional seconds argument), prints and saves
`controller-ownership-*.log` in the current directory. Guide/PS toggles Guide;
View opens/closes Friends; A/Cross in Friends opens Chat; Menu opens a game-menu
scope; Back closes the top scope. Physical Escape dismisses the top scope.
The title shows ownership/recovery. This tests the real native/managed router
pipeline with simulated surfaces, **not WinUI focus restoration**.

The actual Debug shell logs controller metadata, routing decisions, scope
changes and neutral state to
`%LOCALAPPDATA%\ControllerPlayground\Logs\ownership-*.log`. No message text or
credentials are logged. Inspect `result=Discarded`, `result=Claimed`, exact
ID/generation, and `NEUTRAL ... waiting=True/False` transitions.
Packaged Visual Studio Debug deployments can redirect these logs beneath
`%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ControllerPlayground\Logs`.
Detailed native/key/focus/command tracing remains opt-in through
`CONTROLLERPLAYGROUND_INPUT_TRACE=1`; normal hardware testing does not require it.

Use Xbox and DualSense in the actual shell for the following checks, then swap
their roles:

1. Navigate Home/Library with either controller, including quick stick flicks,
   shoulders, context menus, and existing button mappings. Open a modal with
   Xbox. DualSense navigation, Accept, Back, Guide/PS and View must have no effect.
   Closing it must not replay any DualSense actions.
2. Open Friends, Chat, then Guide with the owner. Close in reverse order. Confirm
   focus returns to the prior control and chat/draft survives. Exercise context
   menus, Support/Known Issues and page changes while data refreshes.
3. Hold a direction or Accept on the other controller while opening/closing a
   modal. No repeat/activation may cross the transition. Release/center before
   normal navigation resumes. Press several buttons simultaneously as well.
4. Disconnect the owner. Expect a recovery banner, no automatic takeover and no
   old-generation actions. Release the remaining controller, press A/Cross to
   claim only, release, then press again to activate. Reconnect the former owner:
   it cannot steal the lease. A non-owner disconnect must not pause the owner.
5. **Explicit held-input reconnect test (subsequently user-verified):** hold
   A/Cross or a stick through unplug/reconnect. Expect increased generation and
   neutral waiting; no action/claim while held. Release/center, observe waiting
   clear, then use a fresh press to claim, followed by another press to activate.
   Repeat for both controller families and test held Guide/PS too.
6. Recover with mouse Close, physical Escape/Tab/Enter and root Guide/Friends
   buttons. Verify the active modal traps focus and disabled underlying controls
   cannot activate. Pointer-opened scopes must consume a controller's first
   A/Cross as a claim. Steam sign-in should allow Escape recovery.
7. Open text entry from Chat. Shell Guide/View must remain blocked during the
   helper session; existing helper keyboard behavior must still work. Finish or
   cancel and verify chat focus. This does not verify helper ownership.
8. Launch/resume a game and reopen Guide with a fresh press after release. Check
   normal gameplay input and physical keyboard behavior. Steam Input and system
   Guide interactions continue to follow the pre-existing configuration.

The user has completed the manual Xbox + DualSense checklist, including actual
nested WinUI focus and held-input reconnect gating. Cross-process KeyboardHost
ownership remains outside Increment 3. Do not begin Increment 4 automatically.

## Verification record — October 25, 2026

- All 48 groups (21 managed input, 19 ownership, 8 navigation regression) passed
  in Debug and Release.
- Full x64 Debug solution build passed with 113 warnings and zero errors. Existing
  warnings include native/managed Windows target mismatch, missing publish
  profile, nullable annotations and reference comparisons.
- Default full Release build reached `NETSDK1094` in the shell and unchanged
  KeyboardHost: the ReadyToRun runtime package is unavailable. A command-line
  `/p:PublishReadyToRun=false` build passed; no project settings were changed.
- Five-second ownership diagnostic startup/poll/shutdown smoke test detected the
  live DualSense, with zero dropped events and zero history recoveries. Xbox
  actions, takeover and WinUI focus were not exercised by this smoke test.
- All manual Xbox + DualSense tests passed as reported by the user, with Steam
  Input Desktop Layout mappings disabled. Earlier smoke-test limitations do not
  describe the final hardware verification status.
- No native ControllerInput, KeyboardHost or KeyboardHostClient changes relative
  to the Increment 2 checkpoint. The default Release ReadyToRun packaging issue
  remains outstanding; the build override is a workaround, not a project fix.
