# Increment 4 Phase 0 — isolated feasibility probe

This is an opt-in diagnostic experiment, not the production keyboard protocol.
The shell and KeyboardHost keep their existing capture, mappings, layouts, text
entry, focus handling and pipe unchanged. GameInput policies are unchanged. The
user subsequently authorized checkpointing this experiment before Phase 1.

## Running and reversing the experiment

Build the x64 Debug solution, and close shell/KeyboardHost before starting:

```powershell
.\tools\phase0\Start-KeyboardPhase0.ps1
```

The script backs up and refreshes the main managed DLL, shared input DLL and
matching native DLL in each existing Debug AppX layout, then activates the two
already-registered packages. It does not install/register packages or alter
Steam settings. Directly launching their EXEs without package activation failed
Windows App SDK initialization and is not a valid packaged-app test.

Generated `phase0-probe.log` markers in the two AppX directories enable the
observer for one run GUID. Without the markers or `CONTROLLERPLAYGROUND_PHASE0`
environment variable, no observer/experimental pipe is started. Logs go to
repository-root `phase0-<run>-<role>-<pid>.log`; no text or credentials are logged.
Both applications must be closed before cleanup:

```powershell
.\tools\phase0\Summarize-KeyboardPhase0.ps1 -Run <run-guid>
.\tools\phase0\Stop-KeyboardPhase0.ps1
```

Stop removes the markers and restores the backed-up deployment DLLs. Logs and
backups remain as ignored build/diagnostic artifacts. It never terminates an
existing application or discards source changes. Rebuild/deploy in Visual Studio
normally when returning to ordinary development. The production source hooks
are nullable observers and never poll GameInput a second time.

## What is measured

Each process records its real package identity, collector session, full device
and root IDs, local connection generation, family, lifecycle, raw samples,
translated actions and foreground state. Diagnostic raw-event sequence numbers
remain local to their own native collector.

A GUID-named, current-user-only test pipe sends helper foreground actions to a
shell UI-thread callback. That callback compares full ID with the current shell
scope owner and checks the shell connection is still current. It deliberately
does not equate generation numbers across collectors. This is an identity-equality
experiment, not sufficient production binding or stale-session protection.

The reply returns to a helper UI-thread callback. Approved replies increment a
diagnostic counter only. They never call `Keyboard.HandleControllerAction`, edit
text, focus a window, or commit/cancel a real session. The legacy keyboard path
continues to execute normally during the experiment. Foreground action capture
is observed for every device even though that legacy executor selects only one.

Round-trip duration uses one Stopwatch clock in the helper from its managed
capture observer through queueing, pipe transit, shell UI authorization, return
transit and helper UI dispatch. Pings measure the same dispatch/pipe route
without a physical input event. These measurements exclude hardware-to-poll
delay, actual keyboard-control work, layout/rendering and display latency. Probe
queues are bounded and never replay after a connection failure. Queue drops are
reported separately from native event drops. Diagnostic IO can affect timings.

## Exact hardware checklist

Keep Steam Input Desktop Layout keyboard/mouse mappings disabled. Do not send a
chat message while testing; keyboard commit merely returns text to the draft.

1. Keep both controllers connected and awake. Click KeyboardHost and press Xbox
   A three times, with DualSense untouched. Then Cross three times with Xbox
   untouched. Exercise each controller's D-pad and left stick separately. Verify
   attributed captures contain both Xbox and PlayStation families, not merely
   visible keyboard responses. Record each full ID and local generation.
2. Focus the shell and repeat separate presses. Alt-Tab back and forth while
   controls are neutral. Compare inventories and root IDs across the actual
   distinct package identities. Compare connection generations only within a
   collector. Also compare after controller disconnect/reconnect and app restart.
3. With Xbox, open Friends, select Chat, focus the message field and invoke the
   existing keyboard. Release controls before proceeding. Press D-pad/A/X/Y/RT
   in the helper, then use the other controller. Expect diagnostic approvals only
   for the shell owner. Repeat with DualSense as owner. Existing local typing is
   still legacy behavior and does not demonstrate production mediated ownership.
4. Verify selected-key activation, Backspace, Space, Shift, Caps, shifted symbols,
   Tab and commit/cancel. Move mode must retain 32-pixel steps and A/B exit without
   typing/cancelling. Compare with the existing single-controller behavior.
5. Return normally to Chat, release controls, then navigate. Repeat while holding
   a direction or A/B across the focus transition. Record any replay/extra action.
   Press Guide/PS separately while each window has focus; background Guide is
   expected to remain a special case under the existing policy.
6. Disconnect the owner, reconnect it, and repeat the separate capture checks.
   Phase 0 records this lifecycle; it does not implement a new takeover protocol.
7. Close/reopen either process and repeat a short exchange. The probe should
   reconnect without replaying queued diagnostic actions. Then close both and
   run Stop to restore the pre-probe deployment.

## Identity and routing conclusions to apply to the design

Microsoft documents the IDs as application-local, not portable physical-device
identifiers: [GameInput devices and users](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/user/users-input-devices).
Equality on one machine/runtime/package pair is evidence for that configuration,
not a cross-application contract. Root IDs are application-local too.

The installed v3 GameInput header also exposes `GameInputDeviceInfo.pnpPath`,
`containerId`, `usage` and `supportedInput`, plus `FindDeviceFromPlatformString`.
If local IDs differ, investigate a descriptor-only bridge using an exact Windows
endpoint/PnP path and container/collection metadata. Require a unique one-to-one
endpoint match on both sides and rebuild the binding after hotplug/transport
changes. A container alone can contain several endpoints. VID/PID, family,
enumeration order, and coincident button presses are not identity proofs. The
existing native bridge does not expose these descriptors, so that fallback is
not implemented or hardware-verified by this observer. Ambiguous/missing metadata
must block automatic binding and require explicit reselection or cancellation.

Production must designate one active capture collector per keyboard session,
then execute only shell-authorized commands with session/owner epochs, source
collector identity and command deduplication. It must remove local scalar
execution for mediated sessions and must not also execute the shell's duplicate
observations. Unchanged focus policy is useful but must not be the only defense
against duplicate execution during activation races or background Guide.

## Automated checks

All 48 existing groups plus 4 diagnostic comparison/serialization groups passed
in Debug and Release (52 total). Full x64 Debug solution build passed. The normal
native ABI smoke check passed; its initial zero-device sample is not a hardware
capture success. No production protocol, capture policy, layout or mapping was
changed. Full Release ReadyToRun remains the separately documented existing issue.

The hardware findings are recorded separately in PHASE0_FINDINGS.md. Stop after
Phase 0 for review; do not treat the prototype as Phase 1 implementation.
