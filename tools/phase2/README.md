# Increment 4 Phase 2: lifecycle transport

Historical Phase 2 record, checkpointed as `9cba473` on Phase 1's `3093db8`.
Statements below about unimplemented controller execution describe Phase 2's
boundary. Phase 3 subsequently delivered mediated execution, and the agreed
Xbox + PS5 DualSense Phase 4A regressions passed. See
[current architecture](../../ControllerPlayground.Input/MULTI_CONTROLLER.md) and
[the Phase 4A report](../phase4a/README.md).

## Contract and migration

The new local endpoint is `ControllerPlayground.KeyboardHost.v2.<WindowsSessionId>`.
The original `ControllerPlayground.KeyboardHost` endpoint and line protocol remain
available. Both listeners share one UI lease: they cannot open competing keyboard
sessions. The shell uses v2 lifecycle sessions by default; explicitly setting
`CONTROLLERPLAYGROUND_KEYBOARD_LEGACY=1` selects the compatibility path. There is
no error-triggered fallback. Requests requiring controller ownership fail explicitly
until Phase 3 rather than claiming lifecycle transport provides that guarantee.

Hello/HelloAck negotiate version 2 and lifecycle capabilities (ownership epochs
and bounded framing). Attribution/execution capabilities are deliberately not
advertised. Capture, Execute, and owner-binding messages are rejected. The helper's
existing scalar controller polling and keyboard handler remain the only execution
path. The earlier controller/glyph mismatch is therefore still possible.

Open (epoch 1) → Ready → Commit/Cancel → shell Close (next epoch) → Closed forms
one session per connection. Cancellation can close a session before Ready. A
result is returned only after Closed; EOF after Commit cannot manufacture success.
First terminal response wins. Actor serialization assigns sequence numbers and
applies the existing session validator to every incoming/outgoing message.
Stale identities, epochs and duplicate responses cannot mutate a newer session.

`KeyboardDuplexConnection` owns one continuous reader and one serialized writer,
with 16-element incoming/outgoing channels. Lifecycle producers feed a bounded
64-element actor queue; overflow aborts rather than replaying input. Existing
256 KiB frame, 16,384 UTF-16 text, strict JSON and inventory bounds apply.
Writes have a 3-second deadline; startup is bounded to 5 seconds after connection,
heartbeat interval is 500 ms, peer inactivity is 5 seconds, and Close is 3 seconds.
Launch and initial pipe connection also have explicit deadlines. Failures release
the matching UI lease, cancel queued startup work and return no implicit draft.

Both endpoints use `PipeOptions.CurrentUserOnly`. The server rejects network
clients (including SMB loopback), and both peers verify the OS-observed pipe PID,
Windows session, package family/publisher and exact executable path within that
package. Process start time guards PID reuse. Ready's HWND must be a visible
top-level window owned by that verified helper, rechecked immediately before
foreground activation. Wire-supplied process metadata is insufficient.

The helper defers activation on cold v2 launch, shows without activation for Ready,
and focuses the first key after activation. Session checks guard queued focus and
close callbacks. The shell retains chat version, original chat scope and exact
owner generation guards. Invalidated context/disconnect cancels the request.
Only a committed result with still-valid context updates the draft; no message
is sent automatically. Focus restoration additionally requires foreground still
belonging to this shell or its verified helper, avoiding unrelated-app focus theft.
There are no changes to GameInput policy, native capture, mappings, layout or editing.

## Files

- `ControllerPlayground.Input/Keyboard/KeyboardPipeNames.cs`, `KeyboardPipePeer.cs`:
  endpoint and OS peer/window verification.
- `KeyboardDuplexConnection.cs`, `KeyboardLifecycleTransport.cs`: bounded duplex
  IO and lifecycle coordinator; protocol/validation/session files negotiate subsets.
- `Services/KeyboardHostClient.cs`: v2 startup, verified connection, activation callback.
- `ControllerPlayground.KeyboardHost/Services/KeyboardPipeServer.cs` and
  `KeyboardPipeServer.Version2.cs`: dual listeners and shared lease.
- Helper `App.xaml.cs`, `MainWindow.xaml.cs`, `Controls/OnScreenKeyboard.xaml.cs`:
  single-instance activation and cancellable session UI/focus guards.
- Shell `MainWindow.Input.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`,
  `Overlays/FriendsOverlay.xaml.cs`, `Services/GameWindowService.cs`: context,
  cancellation, focus recovery, visible failure, Debug smoke hook.
- `Services/KeyboardV2Smoke.cs`, this directory's scripts: explicit Debug-only
  real-package diagnostic. `ControllerPlayground.Input.Tests/KeyboardTransportTests.cs`
  and `Program.cs`: integration runner and disposable child helper.

## Validation and reproduction

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug
dotnet run --project ControllerPlayground.Input.Tests -c Release
dotnet run --project ControllerPlayground.Input.Tests -c Debug -- --keyboard-transport-tests
```

93 groups pass in Debug/Release, including 13 real-pipe lifecycle transport groups:
concurrent duplex framing, heartbeats, Unicode commit, cancellation before/after
Ready, startup timeout, context/window rejection, capability refusal, duplicate
terminal callbacks and wire responses, reconnection, wrong peer executable,
malformed framing cleanup and actual child-process termination. Protocol-only
source-generated JSON validation remains separately available (28 groups).

Actual packaged Debug smoke passed: Open/Ready/Cancel/Close/Closed, cancellation
before delayed Ready, verified helper exit and relaunch, malformed-frame teardown
and recovery, and legacy Open/cancellation. Shell logs record `PHASE2_SMOKE PASS`
for seven checks and `PHASE2_SMOKE COMPLETE`. No controller/text injection or chat
send is involved. Temporary deployed files were backed up and restored afterward.

To repeat, close both apps, build Debug x64, then run `Start-KeyboardPhase2.ps1`
in PowerShell (a process-only execution-policy override may be necessary). Existing
AppX registration is required; the script does not register/install packages.
It backs up and refreshes the two DLLs, native DLL and PRI in each registered
Debug AppX layout, then creates explicit smoke/delayed-start marker files. Keep
controllers idle. Read the shell's packaged `ControllerPlayground/Logs` ownership
log, close both apps normally and run `Stop-KeyboardPhase2.ps1`. This restores the
prior deployment and removes markers; logs/backups remain for inspection.

For normal testing after smoke cleanup, build Debug x64, close both apps, and run
`Update-KeyboardDebugDeployment.ps1`. This keeps both registered AppX layouts at
the matching build (hash-verified), with backups and no diagnostic markers.
Launching only the shell from Visual Studio does not necessarily deploy its
separately registered helper. An older helper exposes only the legacy endpoint;
the v2 shell correctly refuses fallback but previously showed an unhelpful error.
The client now logs an unavailable/busy v2 endpoint explicitly and the shell
reports that the helper may need a matching deployment.

Hardware retest reported a mixed deployment on October 25: the running helper
DLL differed from build output, only the legacy pipe existed, and shell logs
showed initial v2 connect timeouts without any successful negotiation. This is
distinct from an Open/Ready session failure. Reconnect logs also show neutral
gating and a fresh Accept claim restoring chat ownership to generation 2; the
keyboard opening remained blocked by the mismatched helper.

The follow-up real-process retest also reproduced an immediate retry while the
verified helper was still exiting after its pipe closed. The client now gives
an existing helper endpoint one additional bounded 2-second connection attempt;
if the process exits during that interval it can launch a fresh helper, otherwise
it reports busy/unavailable. No competing helper or legacy fallback is started.
Shell ownership trace writes/disposal are serialized because transport and UI
callbacks can log concurrently (previously producing corrupt diagnostic lines).

RT/Shift remains an open hardware issue. Code inspection shows `PollAction()`
returns only the first action for its compatibility-selected device; a concurrent
button action can discard an RT edge even though `PollActions()` retains both.
The helper also lacks the shell's unattributed WinUI controller-key filter, so
framework gamepad focus/activation is another possible execution path. That path
has not been instrumented in this retest and must not be presented as a verified
cause of non-owner commits. Both issues belong in Phase 3's single authorized
execution path; no controller polling or keyboard key handling changed here.

Full Debug builds pass. Default full Release still encounters pre-existing
NETSDK1094 (missing ReadyToRun optimization runtime package). Release compilation
can be verified with command-only `/p:PublishReadyToRun=false`; project settings
and dependencies are unchanged.

## Manual acceptance still needed

1. With Steam Input Desktop Layout mappings disabled, open Friends → Chat →
   message field using Xbox, then separately DualSense. Check keyboard appears,
   existing editing/layout/move-mode behavior, cancel without changing draft,
   and commit exact text without automatically sending the chat message.
2. Repeat keyboard opening/cancel/commit several times. Verify focus returns to
   the same valid chat, with no underlying selection activation on close.
3. While keyboard is open, change/close the chat or disconnect the original
   controller. Verify the session closes and no stale text reaches another chat.
   Reconnect and start a fresh interaction; no old completion should apply.
4. Alt-Tab elsewhere before closing the helper. Verify focus is not stolen back.
   Close/terminate the helper mid-entry, then request again: failure must not
   commit a draft; a new helper/session should open normally.
5. In a separately launched shell with the explicit legacy environment setting,
   verify the old path still opens/commits/cancels. Remove the setting afterward.

Controller-specific execution and mixed-owner prompt correctness remain Phase 3
acceptance criteria, not Phase 2 claims. Multi-user/remote-client denial and OS
foreground refusal are implemented fail-closed; tests do not simulate a second
Windows account. Hardware/chat focus acceptance above has not been performed by
the automated smoke. Stop after Phase 2 for review.
