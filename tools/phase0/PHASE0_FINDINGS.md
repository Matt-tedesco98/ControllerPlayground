# Increment 4 Phase 0 findings — October 25, 2026

Historical feasibility evidence. The later production implementation and current
verification scope are documented in
[MULTI_CONTROLLER.md](../../ControllerPlayground.Input/MULTI_CONTROLLER.md).
The controller-selection mismatch described below was addressed by mediated
execution; it is not a current unresolved Xbox + DualSense ownership defect.

**Conclusion:** foreground-process capture plus shell authorization is feasible
in the tested Debug deployment without changing GameInput focus policies. Phase
0 also reproduced the existing shell/helper controller-selection mismatch. The
prototype is not production session IPC and does not make KeyboardHost owned.
This record captures the completed Phase 0 review boundary. The user subsequently
authorized checkpointing Phase 0 and implementing Phase 1's disconnected protocol
and session model; production keyboard integration remains outside that approval.

## Evidence and scope

The controlled two-controller run was
`0e439968-3b7a-4d6e-a6b6-78b5d458362c`. Raw per-process JSONL logs and
`phase0-final-summary.log` are retained at the repository root as ignored
diagnostic artifacts. The original first run had only DualSense in GameInput's
inventory despite a subjective report that both responded; it was not used as
proof of Xbox capture. Xbox was subsequently woken/connected and captured in the
controlled run. The user performed the physical inputs with Steam Input Desktop
Layout mappings disabled.

These are the real package identities, not two copies of a console test host:

- Shell: `1f976d86-31bc-42a4-a14e-40fb2eb4fc98_f6s3q6fqr0004`.
- KeyboardHost: `2d72be25-6768-4b4e-96a4-f3705bd815ff_f6s3q6fqr0004`.

KeyboardHost recorded **70 foreground Xbox actions and 23 foreground PlayStation
actions** while the shell was running. The shell recorded 3 foreground Xbox and
11 foreground PlayStation actions. Logs contain normal focus switches and a
helper restart with a new collector identity. The second helper was invoked
through the existing chat route, not the experimental pipe.

## Full identity comparison

Both processes reported the following complete 256-bit IDs:

| Controller | Shell ID = KeyboardHost ID |
|---|---|
| Xbox | `025792CD29E22FACC7D4D4691931ED7B96E3DA3FD75FACF113C89E43C68D26E1` |
| DualSense | `06ED6F0E545964C06F07B076C24B8214D89A4FC08E5C077922EA51AEBF581771` |

For these endpoints, each root ID also equalled its device ID in both processes.
The helper restart retained endpoint IDs but received a new collector GUID.
Connection generations were recorded but were never equated across processes.

This establishes equality for this machine, runtime, package pair and tested
connections. It does **not** establish a universal cross-process ID contract.
Microsoft documents these IDs as application-local; see the references in
README.md. Reconnect/transport changes and identical-model controller pairs were
not comprehensively tested in Phase 0.

If IDs differ in another supported configuration, the installed GameInput v3
header exposes `pnpPath`, `containerId`, `usage` and `supportedInput` in
`GameInputDeviceInfo`. It also exposes `FindDeviceFromPlatformString`. A future
descriptor query could match the exact Windows endpoint path plus container/
collection metadata, requiring a one-to-one result. Never match by VID/PID alone,
family, enumeration order or simultaneous button presses. Root IDs are also
application-local. The current native ABI does not expose these extra fields;
this fallback was inspected, not implemented or hardware-verified. Ambiguity
must prevent automatic ownership transfer.

## Focus, duplicates and the reported mismatch

No background ordinary actions were recorded in the controlled run, and there
were zero cross-process same-ID/native-time/action candidates among non-repeat
ordinary actions. This is an observation, not an exactly-once guarantee during
all activation races. **Two background shell Guide actions were recorded**;
background Guide remains enabled by the unchanged policy.

The user reported that Xbox did not control the visible keyboard, then that
DualSense opened the keyboard while Xbox prompts appeared. This is consistent
with the existing independent scalar selector:

- A running helper that initially discovers only DualSense pins it until
  disconnect, even after Xbox connects.
- A new helper discovering both chooses the lexically first full ID (Xbox in
  this run) for `PollAction()` and its family/glyph notifications.
- The shell's Chat/KeyboardHandoff owner can still be DualSense. The existing
  text-only production pipe does not transfer that ownership to the helper.

The attributed observer captured Xbox successfully even when the old local
executor did not select it. Do not reinterpret this as a native capture failure
or change button mappings to compensate. The user confirmed reaching the chat
keyboard but reported mismatched routing/prompts; this is **not** a successful
test of controller-specific production text entry. Phase 0 intentionally leaves
that existing path unchanged.

Production requirements made explicit by this result:

1. In mediated sessions, remove local `PollAction()` execution and compatibility
   family selection. Use `PollActions()` only as the active collector's source.
2. Both action execution and glyphs must follow the shell's current session owner,
   not the helper's compatibility endpoint or most recently active controller.
3. Authorize one capture source/collector epoch per session, and block shell
   shortcuts before global Guide/View handling while the keyboard lease exists.
4. Never also execute raw helper captures or background shell observations.
   Execute only authorized commands, validating session/owner/source epochs and
   command sequence again inside queued UI callbacks. Acknowledge execution and
   never blindly replay uncertain text mutations.
5. Use neutral/freshness barriers on focus transitions and takeover. Maintain
   separate lifecycle generations for each process and explicit endpoint bindings.

## Round-trip measurements

Timing spans helper managed capture observation (or synthetic ping), outbound
queue/pipe, shell UI-thread authorization, return pipe and helper UI-thread
callback. Approved commands incremented a diagnostic counter only; no forwarded
command changed keyboard text. The existing keyboard executed independently.

| Sample | Count | Median | p95 | p99 | Maximum |
|---|---:|---:|---:|---:|---:|
| All captured-action round trips | 93 | 0.438 ms | 1.977 ms | 4.908 ms | 4.908 ms |
| Approved ordinary-action counter executions | 19 | 0.438 ms | 1.947 ms | 1.947 ms | 1.947 ms |
| Periodic UI/pipe pings | 3,058 | 0.362 ms | 1.391 ms | 17.000 ms | 63.258 ms |

There were 21 diagnostic counter executions: 19 ordinary actions and two Guide
identity-comparison experiments. The latter are not evidence that Guide should
execute during a keyboard session. The toy authorizer compares the current scope
owner and exact ID; it is not a production session/type/epoch gate. There were
72 rejected action proposals and zero probe queue drops.

The 19 ordinary executions demonstrate the intended authorization/dispatch route
using the actual UI processes. They do not demonstrate production-owned typing.
The measurements exclude controller-to-poll delay, actual keyboard-control work,
rendering and display latency. The 63 ms ping tail shows that UI scheduling stalls
still matter; keep bounded queues, age limits and no-replay recovery in the plan.
Gaming load, long-duration behavior and release-mode latency were not measured.

## Policy, behavior and test status

`ControllerInput.cpp` is unchanged, including
`GameInputExclusiveForegroundInput | GameInputEnableBackgroundGuideButton`.
Neither pipe used by the working keyboard nor its controls, layout, glyph assets,
button mappings or text operations changed. The prototype never injects input,
changes focus, or calls `Keyboard.HandleControllerAction`. Shared-input state and
the router are not modified by the observer.

The evidence supports retaining the current GameInput policies for the proposed
foreground-capture design. It does not establish game compatibility under every
foreground transition or a correct production takeover implementation. The full
mapping/layout/single-controller acceptance matrix must be rerun after Phase 1
integration; existing behavior was preserved by leaving its implementation intact.

- Full x64 Debug solution build passed with existing configuration/source warnings.
- All 48 existing groups plus four Phase 0 diagnostic groups passed in Debug and
  Release (52 total).
- Native ABI smoke passed; its initial zero-device inventory was not counted as
  successful controller capture.
- All three PowerShell scripts passed syntax checks. Launch, summary and cleanup
  were exercised against the actual registered packages.
- `git diff --check` passed. No native, production pipe, or keyboard-control diff.
- Release ReadyToRun remains the existing unresolved packaging issue; it was not
  changed or worked around in project settings.

Cross-process held-input transition, full disconnect/takeover, exact text behavior,
identical-model devices, and stress/crash recovery still require the production
session design and focused tests. They are not silently marked verified by this
feasibility experiment.

## Reversal and review status

The two test windows were closed normally. Stop-KeyboardPhase0 removed the opt-in
markers and restored the pre-probe managed/native Debug deployment DLLs. Ignored
logs and backups are retained; no registry/user environment, Steam configuration,
package registration or GameInput policy was changed. The user authorized a Git
checkpoint of this isolated experiment before Phase 1. Its incomplete hardware
coverage remains explicitly documented above.

Reproduction steps, exact commands, caveats and the remaining hardware checklist
are in README.md beside this file.
