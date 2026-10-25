# Native device-aware capture — Increment 1

This increment changes only the native DLL and its diagnostics/tests. Managed
polling, shell ownership, KeyboardHost IPC, and the existing GameInput focus
policy are unchanged. The shell is **not yet a multi-controller ownership UI**.

## Build and automated checks

Use an **x64 Native Tools Command Prompt for Visual Studio**, at the repository
root. Existing restored dependencies are sufficient; no new packages are needed.

```bat
msbuild ControllerPlayground.slnx /t:Build /p:Configuration=Debug /p:Platform=x64
cl /nologo /std:c++20 /EHsc /W4 /MD /Fox64\Debug\CaptureStateTests.obj /Fex64\Debug\CaptureStateTests.exe ControllerInput\tests\CaptureStateTests.cpp
cl /nologo /std:c++20 /EHsc /W4 /MD /Fox64\Debug\ControllerInputDiagnostics.obj /Fex64\Debug\ControllerInputDiagnostics.exe ControllerInput\diagnostics\ControllerInputDiagnostics.cpp x64\Debug\ControllerInput.lib user32.lib
x64\Debug\CaptureStateTests.exe
x64\Debug\ControllerInputDiagnostics.exe --smoke
```

The state tests use synthetic input and cover full-byte identity comparison,
independent device generations, disconnect/reconnect, Guide source deduplication,
stale Guide readings, separate button transitions, short taps/stick flicks, and
bounded event overflow. They do not emulate the GameInput COM implementation.
The smoke test checks ABI validation, installed v3 runtime creation, device/Guide
callback registration, legacy entry points, and repeated initialize/shutdown.
It also works with no connected controllers. Neither test opens the shell or
contacts Steam.

## Two-controller hardware diagnostic

Close ControllerPlayground and KeyboardHost first so their unchanged exclusive
foreground policy and background Guide listeners do not compete with this tool.

```bat
x64\Debug\ControllerInputDiagnostics.exe 120
```

A small foreground window receives controller input; the console prints events.
Keep that window focused for ordinary input. Close the window for orderly shutdown.
Do not use Ctrl+C when testing cleanup, since it terminates the process abruptly.
The tool uses exactly the DLL's existing focus policy, including exclusive
foreground ordinary input and background Guide support. It does not launch games,
change controller mappings, or inject/suppress input through hooks or drivers.

Event kinds: **1 connected, 2 disconnected, 3 input, 4 Guide press, 5 history reset**.
Each record includes a 64-character hexadecimal ID, connection generation,
sequence, GameInput timestamp, connection/reading availability, VID/PID, buttons,
transition masks, sticks, and triggers. Root IDs are available in the snapshot API.
The same records can be drained by a future managed diagnostic consumer;
`ControllerInput_SetDiagnosticsEnabledV2(true)` additionally enables concise
connection/Guide/history messages through `OutputDebugString`.

1. Connect two controllers, ideally also testing two identical models. Expect
   distinct full IDs even when VID/PID match. Do not use enumeration order as ID.
2. Press A/Cross on one and B/Circle on the other, then operate both simultaneously.
   Verify each event's buttons/sticks/triggers stay under the correct device ID.
3. Tap and hold Guide/PS on each. Expect one kind-4 event per press, attributed to
   its own ID. Exercise repeated presses, foreground/background changes, and a
   press spanning a focus change. Repeat with DualSense USB and Bluetooth.
4. Disconnect one while holding a button/stick. Expect a kind-2 event with only
   that ID disconnected, cleared state, and its existing generation. Continue
   using the other controller and confirm its generation/state remain independent.
5. Reconnect at the same endpoint. If GameInput returns the same ID, expect its
   generation to increase and fresh reading cursors. A different port, transport,
   or virtual device may have a different ID; it must not inherit the prior ID.
6. Repeat with Steam Input enabled/disabled. Physical and virtual endpoints are
   reported separately; this increment does not automatically merge them.
7. Exit diagnostics and run the normal shell with one controller. Recheck D-pad,
   stick repeat, quick taps/flicks, A/B, shoulder tabs, Menu/View, Guide/PS, and
   KeyboardHost shortcuts. Repeat after reconnect and game launch/return.
8. For gameplay, verify that the unchanged foreground/background policy behaves
   as before. This increment does not guarantee suppression of input to games
   that accept background controller input through other APIs.

## ABI v2 contract

The four legacy exports and their structure layouts remain present. New exports:

- `ControllerInput_GetApiVersion()` returns **2**, the bridge ABI version.
- `ControllerInput_PollDevicesV2()` copies snapshots of all known gamepad endpoints,
  including disconnected tombstones. Query the count, allocate, and retry if a
  hotplug causes `HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER)`. Count queries
  and undersized buffers do not advance capture. A successful supplied-buffer
  call advances each connected device's separate history cursor.
- `ControllerInput_DrainEventsV2()` drains at most the supplied capacity. Poll and
  drain have one logical v2 consumer per process. Legacy calls use separate
  cursors and Guide latches and cannot consume v2 transition history.
- `ControllerInput_GetDiagnosticsV2()` reports initialization and callback HRESULTs,
  GameInput API version, connected count, event drops, history resets, and
  unattributed Raw Input Guide presses.
- `ControllerInput_SetDiagnosticsEnabledV2()` controls debug output, off by default.
- `ControllerInput_Shutdown()` unregisters callbacks and releases retained COM
  references. Call on the registered window thread before destroying the HWND;
  never from `DllMain`. Legacy hosts are unchanged and still rely on process exit.

V2 calls return signed 32-bit HRESULTs. Pass exact native structure strides:
snapshot **144**, event **160**, diagnostics **48** bytes, with 8-byte packing.
IDs are 32 opaque bytes copied from `APP_LOCAL_DEVICE_ID`, not GUIDs. Snapshots
separate actual held state from pressed/released masks. `readingAvailable == 0`
does not mean disconnected. Generations and sequences are process-local; shutdown
starts a new capture lifetime, so future IPC must add its own collector session ID.

The event queue holds 1,024 entries and drops the oldest on overflow, incrementing
the diagnostic counter and leaving a sequence gap. Consumers must resynchronize
from snapshots after a gap. Input events are poll summaries, not an ordered record
of every intra-poll button transition. Each device history walk is capped at 4,096
readings. Before walking history, capture checks the latest sample timestamp:
unchanged idle samples reuse the accepted state and never cause reset loops.
Expired history or a walk limit produces a history-discontinuity indication and
reconciles the latest state against the retained button baseline. This preserves
observable state changes, but cannot reconstruct every tap inside a lost interval.
After reconnect, samples older than that connection's callback timestamp are
rejected; the first fresh sample is compared against a neutral connection baseline.
Known device tombstones remain until shutdown to preserve reconnect generations.

Legacy scalar polling pins one connected endpoint until it disconnects; its
VID/PID query uses that same endpoint. Guide remains available from any controller,
as before. This compatibility behavior is not controller ownership. Selection
among multiple devices and managed family changes await the next increment.

Raw Input is correlated by device path through GameInput, never by VID/PID alone.
If correlation fails, the existing legacy PS-button fallback remains, but no
fabricated device-attributed v2 Guide event is emitted. The unattributed counter
and debug message make this visible. Hardware validation of this mapping and
Guide-source ordering is required before treating attribution as proven.

## Baseline environment and scope

Observed before editing: clean Git working tree; Microsoft.GameInput **3.5.268**
local headers exposing **API v3**; system `GameInput.dll` **0.2309.26100.9756** and
`GameInputRedist.dll` **4.0.302.0**; successful v3 runtime initialization. The baseline
x64 Debug solution build passed with existing C# and configuration warnings.
Sandboxed PDB linking failed with LNK1101; host-permission builds succeeded.

The native x64 Debug project previously selected `/MD` while defining `_DEBUG`.
New STL containers exposed the resulting `_CrtDbgReport` link failure. Increment 1
corrects that configuration to `/MDd`; no managed build configuration is changed.

Standalone diagnostics/tests are intentionally not added to the WinUI solution's
startup or build dependencies. No existing test projects were found.

## Implementation verification

Final x64 Debug solution build: **passed**, 0 errors (60 existing warnings on the
final build; baseline reported 118 including repeated compilation/copy warnings).
Native x64 Release build: **passed**, 0 warnings and 0 errors. Native state tests
and diagnostic smoke tests: **passed**. Export inspection confirms all four legacy
names and six additive exports. `git diff --check` passed.

The initial runtime smoke test completed three initialize/register/poll/shutdown cycles
with **zero connected controllers observed**. Actual two-controller attribution,
USB/Bluetooth reconnect, Raw Input correlation, Guide ordering, and single-controller
shell navigation still require hardware validation. Build success and synthetic
tests do not establish those hardware results.

## Two-controller recovery fix and retest — October 25, 2026

The first physical test found 2,874 history-reset indications, missing Xbox press
edges after reconnect, and a pre-disconnect sample published under generation 2.
A separate native probe reproduced Xbox `GetNextReading` returning
`0x838A0004` (`GAMEINPUT_E_REFERENCE_READING_TOO_OLD`) for an unchanged idle sample,
while `GetCurrentReading` continued returning that same sample. Repeatedly
discarding the cursor also discarded the baseline needed to compute press edges.

Capture now checks latest timestamps before traversing history, retains a separate
baseline across history recovery, and enforces a connection timestamp floor after
reconnect. New regression tests exercise 3,000 unchanged polls, history-expiry
reconciliation, stale-generation rejection, and first-sample press/release edges.
The diagnostic also defers window destruction until native shutdown has completed.

Hardware retest with Xbox `045E:0B00` and DualSense `054C:0CE6`:

- Different full device IDs, controller-specific input, and one observed Guide
  press from each controller were recorded.
- Xbox disconnect/reconnect retained its ID and advanced generation 1 to 2.
- DualSense Cross continued working during Xbox's disconnection.
- Both requested Xbox A presses after reconnect carried `pressed=00000004`,
  followed by `released=00000004`; generation-2 samples were newer than reconnect.
- Final counters: **2 connected, 0 event drops, 0 unattributed Raw Input Guide
  presses, 4 history recoveries**. The recoveries mark actual lost history ranges,
  not an unchanged-sample reset loop. Lossless reconstruction across those ranges
  is not guaranteed.
- Updated x64 Debug solution and native x64 Release builds passed, as did state
  tests and three runtime smoke cycles. Existing solution warnings remain.

The recovery log is `controller-hardware-recovery-test.log` at the repository root
(ignored build/test artifact). Bluetooth transport variants, Steam Input variants,
and the normal shell's full single-controller navigation/gameplay matrix remain
outside this retest. No managed routing, ownership, IPC, or focus policy changed.
