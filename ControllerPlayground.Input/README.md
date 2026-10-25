# Managed per-device input — Increment 2

For the current shell ownership integration and Increment 3 verification steps,
see [OWNERSHIP.md](OWNERSHIP.md). The description below records Increment 2;
MainWindow now uses the attributed batch and root navigation accepts either
controller. KeyboardHost still uses the scalar compatibility adapter.

Increment 1 is checkpointed in commit `22bf245`. Increment 2 adds managed device
identity, per-device normalization, and attributed actions. Shell modal ownership,
KeyboardHost IPC, and native foreground/background policies remain unchanged.

## Contracts

- `ControllerDeviceId` preserves all 32 opaque GameInput bytes. It supports value
  equality, hashing, hex display, and byte round trips; it is not a GUID or VID/PID.
- `ControllerDevice` carries endpoint/root IDs, native connection generation,
  vendor/product IDs, and family. Separate endpoints with identical VID/PID remain
  separate. Do not assume USB/Bluetooth/Steam Input endpoints share an identity.
- `ControllerService.PollActions()` returns all controller-attributed one-shot
  actions and navigation repeats from a poll. `ActionReceived` publishes the same
  events; route one representation, not both. There is no backlog replay.
- Every action has a service-lifetime collector session GUID, monotonic managed
  sequence, device/generation, family, native sample timestamp (microseconds),
  local observation time (`Environment.TickCount64` milliseconds), and repeat flag.
  Native sample timestamps can repeat during held navigation or background Guide.
  Sequence order is poll/device/mapping order, not a claim of physical ordering
  between simultaneous controllers. These IDs do not coordinate ownership across
  processes; shell and KeyboardHost still run their own input service.
- `Devices` is a copy of currently connected descriptors.
  `DeviceConnectionChanged` reports individual lifecycle changes, with native event
  sequence/timestamp where available. Snapshot-inferred changes have no native
  sequence. Aggregate `IsConnected` / `ConnectionChanged` mean at least one device
  is connected; reading/focus unavailability does not mean unplugged.
- `PollAction()` is the existing scalar compatibility API. It keeps one endpoint
  selected until disconnect, returns one action in existing priority order, and
  accepts Guide from any device as before. Other controllers' events are still
  published, but not queued into later scalar polls. `Family` and its existing
  event describe this compatibility endpoint; use `action.Device.Family` for the
  attributed stream. This selection is not modal ownership.

## State and lifecycle

Button edges, trigger edges, stick direction/hysteresis, and held-navigation repeat
timers are stored independently per endpoint/generation. Mapping thresholds remain
0.65 stick press, 0.30 stick release, 0.15 direction-switch margin, 0.60 right trigger,
400 ms initial repeat, and 120 ms subsequent repeat. All simultaneous one-shot
presses survive in the new event batch; the scalar adapter retains its old priority.

Disconnect removes that device's mutable state. A new generation, `ResyncInputState`,
or lost native event continuity requires a valid neutral/release sample before
actions resume. Buttons/system buttons must be released, sticks centered within
0.30, triggers below/equal 0.30, and no transient button/Guide pulse may remain.
The rearming sample emits no action. An unavailable sample cannot rearm a device.
Initial discovery of generation 1 preserves startup behavior. Old generations,
older samples, and a connected snapshot arriving after the same generation's
disconnect cannot revive stale input. Read unavailability stops navigation repeat
without changing connection state. Native history recovery does not discard a
fresh observable button edge; native queue loss instead forces neutral recovery.

The source drains lifecycle events before reconciling snapshots, handles hotplug
buffer resizing and bounded event draining, detects native sequence gaps/drops,
and does not replay Input/Guide diagnostic events alongside snapshot edges.
Callbacks that arrive after a snapshot can therefore retire its input safely.

Call the service on one thread. Notifications are synchronous on that thread and
polling cannot be reentered. Use exactly one v2 source/consumer per process. Hosts
retain responsibility for native initialization, window registration and shutdown;
the managed source never changes focus policy. Construct a new source/service
after restarting the native capture lifetime (generations/sequences then restart).

## Additive navigation query

Native v2 snapshots expose actual axes, while the old scalar API also projects
brief stick flicks that returned to center between polls. To preserve that behavior
the bridge now exposes `ControllerInput_CopyNavigationProjectionV2(id, generation,
state)`: a non-consuming query of the last v2 poll's existing projection. It checks
the exact endpoint and generation and resets its cache on disconnect. Native
snapshot/event layouts and all prior exports are unchanged.

Managed normalization uses actual held state for edge/neutral tracking, pressed
masks for short D-pad taps, and the projection for short stick flicks. A projection
from an unchanged timestamp is not replayed as a new flick. This requires deploying
the newly rebuilt native DLL with the managed assembly; an older v2 DLL lacks the
additional export. Native diagnostic overflow and truly expired history still
cannot guarantee reconstruction of every rapid tap. Unattributed legacy Raw Input
Guide fallback is preserved in scalar `PollAction()` by observing the native
counter; it cannot become an attributed event by guessing an owner. The new stream
emits no fabricated device identity for that case.

## Automated checks

The solution includes a dependency-free console test runner rather than a NuGet
test framework. From the repository root, with the existing build prerequisites:

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug
dotnet run --project ControllerPlayground.Input.Tests -c Release
```

Twenty-one test groups cover ABI sizes/offsets/full-byte identities, interleaved and
simultaneous presses, identical-model devices, independent repeats/hysteresis and
triggers, disconnect/reconnect, neutral gating, stale generations/timestamps,
snapshot/disconnect races, rapid reconnects, unavailable readings, Guide pulses,
brief taps/flicks, history recovery, resync/overflow, scalar compatibility, and
reentrancy. These synthetic tests do not establish hardware behavior.

After building the full x64 Debug solution, run real P/Invoke smoke checks:

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug --no-build -- --native-smoke
```

The test project copies the matching native Debug/Release DLL when available.
Build the native configuration first before running native checks for that
configuration. Smoke checks verify runtime polling, descriptor marshalling, and
the generation guard on navigation projection queries.

## Manual Xbox + DualSense verification

Close the shell and KeyboardHost before running the standalone monitor to avoid
competing foreground/Guide listeners. This uses the unchanged native focus policy.

```powershell
dotnet run --project ControllerPlayground.Input.Tests -c Debug --no-build -- --monitor
```

Focus the diagnostic window. It prints device changes and attributed actions for
up to two minutes; close the window to stop cleanly.

1. Press A on Xbox and Cross on DualSense, interleaved and simultaneously. Expect
   separate full IDs/families, increasing action sequence numbers, and no held-key
   retriggers. Also press multiple face buttons together on one controller.
2. Hold opposing directions on the two controllers, starting 100 ms apart. Each
   should repeat on its own 400/120 ms schedule. Exercise stick diagonals,
   opposite directions, quick flicks, shoulder tabs, and RT/R2 independently.
3. Press Guide/PS separately, including with the window out of focus. Each
   observed Guide must carry its originating ID. System/Steam Guide behavior
   remains governed by the existing policy; no global capture guarantee is added.
4. Disconnect Xbox while holding input. DualSense must remain usable. Reconnect
   Xbox at the same endpoint while holding A or a direction: no action may fire
   until its controls are released/centered and a clean sample has been observed.
   Then press A again: expect the same ID and increased generation. Old generation
   actions must not appear. Repeat with the controllers swapped.
5. Stop monitoring, then use the normal shell with one controller. Recheck Home,
   Library, context menus, Game Details, Guide, and existing KeyboardHost shortcuts.
   After keyboard close/resume, release held controls before navigating again.
6. With two controllers in the normal shell, ordinary scalar navigation remains
   on the selected endpoint until disconnect. The standalone monitor, not shell
   modal ownership, is the way to verify both event streams in this increment.

USB/Bluetooth changes and Steam Input can create different endpoint IDs. Test
those as separate configurations; no physical-device merging, player assignment,
cross-process ownership, or gameplay input suppression is implemented here.

## Files and verification

Modified: `ControllerInput/ControllerInput.h` and `.cpp` (additive projection query),
`ControllerPlayground.Input/ControllerInputNative.cs`, `ControllerService.cs`,
`ControllerPlayground.csproj` (exclude test sources from WinUI), and
`ControllerPlayground.slnx` (build the test runner).

Added under `ControllerPlayground.Input`: `ControllerDeviceId.cs`,
`ControllerActionEvent.cs`, `ControllerDeviceState.cs`, `IControllerInputSource.cs`,
`NativeControllerInputSource.cs`, and this README. Added under
`ControllerPlayground.Input.Tests`: its `.csproj`, `Program.cs`, and
`ControllerMonitor.cs`.

Verified October 25, 2026: x64 Debug solution build passed with existing C# and
configuration warnings and no errors; Release managed input/test and native builds passed without warnings
or errors; all 21 managed test groups passed in Debug and Release; managed/native
smoke checks passed and marshalled the live DualSense ID/root ID/generation/family;
the legacy native smoke test passed. The host probe saw one controller (DualSense).
Subsequent supplied Xbox + DualSense monitor logs confirmed separate identities,
independent repeats, reconnection generations, stale-event prevention, and zero
event drops. The user subsequently confirmed the explicit held-input reconnect
neutral-gating hardware check passed on October 25, 2026. This is user-reported
hardware verification, separate from the earlier reconnect logs and automated tests.
