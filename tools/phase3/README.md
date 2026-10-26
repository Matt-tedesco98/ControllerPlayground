# Increment 4 Phase 3 — controller-specific keyboard execution

Phase 3 remains the accepted implementation checkpoint. The agreed Xbox + PS5
DualSense Phase 4A regressions are now complete; see [the verification record](../phase4a/README.md)
and [current architecture](../../ControllerPlayground.Input/MULTI_CONTROLLER.md).
Steam Controller is deferred, and packaging verification still requires approval.

Phase 2 was reviewed and checkpointed separately as
`9cba473dde0ab045a9b4f9bcbd0b790cf895e907`. Before this phase,
the user verified matching v2 shell/helper opening, editing, commit-to-draft,
cancellation and return focus. Phase 3 has completed final hardware acceptance
and is finalized in the commit titled
`Complete controller-owned KeyboardHost input execution`.

## Execution and ownership

The shell requests all four existing v2 capabilities. A lifecycle-only/older helper
cannot satisfy that request; the client never downgrades. Explicit legacy selection
(`CONTROLLERPLAYGROUND_KEYBOARD_LEGACY=1`) remains separate. Existing lifecycle-only
smoke clients still negotiate their subset and cannot gain controller authorization.

For mediated sessions, KeyboardHost uses `PollActions()` and sends attributed
CaptureBatch messages. Its scalar `PollAction()` executor is unreachable in that
branch. WinUI Gamepad/Navigation keys are filtered by Key and OriginalKey before
framework focus/activation, closing a second unattributed execution path. Physical
keyboard and pointer controls remain available. GameInput focus policies, native
capture, Steam Input settings and game-wide input behavior are unchanged.

After verified Ready/activation, the shell binds the initiating chat controller and
sends SetOwner with the next epoch. Binding requires unique, complete endpoint and
root IDs plus matching family in both live inventories, within the authenticated
package peers whose IDs matched in Phase 0. Each collector keeps its own generation.
Ambiguous/mismatching IDs fail closed, without VID/PID or enumeration-order guesses.
This is deliberately not a claim that GameInput app-local IDs are globally stable;
other package/device combinations need a separately verified binding strategy if
their opaque identities differ. Mouse/physical-keyboard recovery remains available.

The helper resets managed neutral gates and updates glyphs from the authorized
binding, not its scalar compatibility controller. OwnerReady requires foreground,
the neutral gate to clear, and a capture frame newer than owner UI application.
Only that exact bound device/generation can produce proposals. Guide/View and
non-owner commands are discarded; the shell's KeyboardHandoff scope blocks global
controller shortcuts until the keyboard session ends.

The shell also checks its live inventory before forwarding. Each proposed action
becomes an existing ordered execution ticket. A single worker marshals tickets to
the helper UI. Immediately before execution, the UI refreshes the live capture
through the same method used by the timer, retaining the whole batch; it checks
foreground, current device/session and invokes the model's one-shot ticket gate.
Model validation and the synchronous keyboard handler run under the same lock, so
authorization cannot be revoked between that check and the handler call. No lock
is held while awaiting UI work or pipe IO. Executed acknowledgements do not execute
actions. Queue bounds and the existing 250 ms ticket/proposal lifetime still apply.
Expired tickets fail the session visibly rather than silently wedging it or retrying.

Disconnect/focus loss immediately invalidates queued host tickets. Inventory/focus
messages revoke the shell replica's authorization as well. Reconnection does not
renew the old binding. After neutral, a fresh non-repeated Accept from a uniquely
bound eligible controller claims ownership; that claim does not also activate a
key. New SetOwner/epoch/neutral acknowledgement precedes subsequent typing. Parent
Friends/Chat ownership and glyphs follow the authorized transfer in the same valid
handoff. Delayed callbacks from older epochs cannot abort or modify the new owner.

Commit/Cancel continue through the existing terminal protocol. Only a committed
result for the original still-valid chat/scope updates its draft. Changing chats,
closing the scope or shell, EOF, cancellation and helper failures retain Phase 2's
cleanup and focus guards. Disconnect suspends controller authorization while the
session stays open for takeover or pointer/keyboard recovery; it no longer cancels
the entire mediated request merely because its initial generation disappeared.

## RT/Shift correction

The scalar compatibility projection returned only the first action in a poll,
discarding a simultaneous RT edge. Attributed batches retain every action. Within
each captured batch, RT edges precede character/space/commit actions and get fresh
ordered capture sequence numbers, with device identity, repeat flag and native/
observed timestamps retained. This makes simultaneous Shift+Accept apply Shift
before typing. The managed translator also retains navigation alongside an RT-only
edge instead of returning early. Other button/navigation priority, 400 ms initial
repeat, 120 ms repeats, stick hysteresis and trigger threshold remain unchanged.
Shift/Caps behavior and existing keyboard layout/edit handlers are reused.

## Changed files

- `ControllerPlayground.Input/Keyboard/KeyboardControllerBridge.cs`: host/shell UI
  bridge, snapshots, full-identity resolver and modifier batch ordering.
- `KeyboardLifecycleTransport.Controllers.cs`: binding, inventories, neutral/
  takeover state, execution worker, immediate revocation and one-shot UI gate.
- `KeyboardLifecycleTransport.cs`: capability negotiation and actor integration.
- `ControllerPlayground.Input/ControllerDeviceState.cs`: RT+direction retention.
- `ControllerPlayground.Input/ControllerInputRouter.cs`: scoped handoff transfer.
- `Services/KeyboardHostClient.cs`, `MainWindow.Input.cs`: ownership-required
  connection, UI inventory/context checks and parent ownership transfer.
- Helper `MainWindow.xaml.cs`, `Services/KeyboardPipeServer.Version2.cs`: exclusive
  capture branch, UI gate, foreground/device refresh and authorized glyph updates.
- `ControllerPlayground.Input.Tests/KeyboardMediatedTests.cs`, `Program.cs`: tests.
- This report, the keyboard protocol README, root roadmap notes and
  `ControllerPlayground.Input/OWNERSHIP.md`: current boundary and final acceptance.

## Automated verification

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug
dotnet run --project ControllerPlayground.Input.Tests -c Release
dotnet run --project ControllerPlayground.Input.Tests -c Debug -- --keyboard-mediated-tests
```

The focused groups cover exact/ambiguous bindings, independent generations, RT plus
button/direction and repeat timing, scoped transfer, non-owner shortcuts/modifiers,
glyph inheritance/transfer, shifted commit, one-shot execution, disconnect takeover,
reconnect neutral gating, old callbacks after a newer epoch, foreground revocation,
shell-side disconnect before helper inventory, cancellation, ticket expiry and Back.
The complete suite also retains Phase 1 protocol/session and Phase 2 lifecycle,
malformed-message, duplicate-terminal and real process-termination coverage.

Debug and Release solution builds are required. The pre-existing default Release
ReadyToRun NETSDK1094 remains; use command-only `/p:PublishReadyToRun=false` for
compilation validation without altering project settings or installing packages.

## Xbox + DualSense verification

Keep Steam Input Desktop Layout mappings disabled as in the prior verified setup.
Build Debug x64, close both apps, then run the Phase 2
`Update-KeyboardDebugDeployment.ps1` to hash-verify matching registered deployments.
No diagnostic markers are needed. Release all controls after opening/transfer.

1. Using DualSense, open Friends → Chat → message field → keyboard. Verify PS
   glyphs, D-pad/stick single steps and repeats, Cross typing, Square backspace,
   Triangle space, R2 Shift/Caps and move mode. Try Xbox directions/A/B/Menu/RT/
   Guide/View: none should type, close, commit or open underlying shell surfaces.
2. Repeat with Xbox owner. Test RT+A together, RT+D-pad, RT then A, and Shift/Caps.
   Check one character per short press and exact shifted text; no skipped modifier.
   Menu commits only to the original draft. B cancels without replacing that draft.
3. Open with DualSense owner; disconnect it while keyboard remains open. Release
   Xbox, press A once to claim (no character), release, then navigate/type. Glyphs
   must switch to Xbox. Reconnect DualSense while holding input: it must not regain
   control or replay input. Reverse the controllers and repeat the takeover.
4. With an owner disconnected, reconnect that same controller while holding A/
   Cross or a direction. Release to neutral, then fresh A/Cross claims without
   typing. A subsequent press types. Check no background game or shell command.
5. Alt-Tab away while holding navigation and return: no queued input should replay;
   neutral and a fresh claim rearm controller typing. Pointer/physical keys remain
   usable. Commit/cancel must restore focus only to the original valid chat; an
   unrelated foreground app must not lose focus to a delayed completion.
6. Close/terminate KeyboardHost mid-draft. No implicit commit or auto-send may
   occur. Start a fresh keyboard request and verify reopening, commit and cancel.
   Change/close the original chat during a pending session where reachable; no
   terminal text may land in a different chat. Never press Send during this test.

## Verification record — October 25, 2026

The user confirmed initial owner/glyph/typing, non-owner rejection, simultaneous
inputs and commit/cancel hardware checks. After deploying the pre-execution live
refresh and ticket-expiry safeguards, the user additionally confirmed both
disconnect takeover directions, reconnect/held-input rejection, fresh Accept
claims without typing, RT/R2 simultaneous input, commit/cancel focus restoration,
Alt-Tab recovery and closing/terminating/reopening KeyboardHost. The response was
explicitly clarified as **all passed; fresh claim required**. Shell logs independently
show both full-identity ownership transfers with fresh OwnerReady acknowledgements
and generation-2 non-owner Xbox input discarded after reconnect.

The complete Debug/Release suite contains 109 groups, including 16 mediated
execution groups; final outputs are retained in `phase3-tests-debug.log` and
`phase3-tests-release.log` (ignored diagnostic artifacts). All 28 protocol/session
groups also pass with JSON reflection disabled. Final Debug and Release compilation
uses the commands above; Release requires the existing ReadyToRun override.

Final acceptance includes the latest explicit startup deadlines for bridge UI
callbacks and the passing unresponsive-callback integration test. The user deployed
these final binaries through `tools/phase2/Update-KeyboardDebugDeployment.ps1` and
confirmed successful matching shell/helper hash verification. The previous pending
deployment status is resolved.

All four final hardware acceptance checks passed, as explicitly reported by the user:

- Repeated KeyboardHost opening and closing.
- Cancellation with delayed-callback protection.
- Controller disconnect, takeover and stale-command rejection.
- KeyboardHost termination, recovery and successful reopening.

No tests with a different package identity, second Windows account or controller
family are claimed. Unmatched/ambiguous full device identities remain fail-closed
and require a future verified binding method. The existing default Release
ReadyToRun NETSDK1094 and Steam Input Desktop Layout compatibility consideration
remain tracked; neither is changed by this commit. Legacy compatibility remains
explicit and does not provide mediated ownership guarantees.

Finalization includes only verified Phase 3 source, tests and documentation; build
outputs, logs, deployment backups and temporary diagnostics are excluded. The
Phase 2 checkpoint is preserved separately as this commit's direct parent.
The record above describes Phase 3 acceptance. Subsequent Phase 4A results are
linked at the top; GameInput focus policies remain unchanged.
