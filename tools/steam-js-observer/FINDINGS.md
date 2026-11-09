# Steam integration research log

This file contains **manually reviewed, non-sensitive findings only**. Keep raw captures local and excluded from Git.

## Baseline evidence from prior manual Steam experiments

| Area | Observation | Evidence level |
| --- | --- | --- |
| CEF debugging | Local DevTools discovery listed 19 targets, including `SharedJSContext` | Observed in prior test session |
| JS bridge | `SteamClient` exposed 49 top-level namespaces, including `Apps`, `Downloads`, `Installs`, `Friends`, `GameSessions`, and `WebUITransport` | Names observed, functionality not all validated |
| Game launch | `Apps.RegisterForGameActionStart` emitted an event during test-game launch | Verified callback in prior session |
| Launch stages | `Apps.RegisterForGameActionTaskChange` emitted `CreatingProcess`, `WaitingGameWindow`, `Completed`, and other action steps | Verified callback in prior session |
| Launch action | `Apps.RegisterForGameActionEnd` emitted after the launch action | Verified callback; **not** proof game exited |
| Native integration | Ability for standalone C# to subscribe directly to `SteamClient` without Steam CEF debugging | **Not demonstrated** |
| Native IPC | Exact Steam JS-to-native transport, authorization and session contract | **Not characterized** |
| All-call tracing | This observer captures every Steam frontend-to-backend call | **False**: target/context and attached-method limitations apply |

**Historical test date:** October 23, 2026. Steam version/build not preserved in this log; do not assume current compatibility.

## Research capture template (copy below for each validated observation)

- Date / Steam client build:
- Debug target / context:
- User action tested:
- API namespace and method:
- Listener/method wrapper:
- Observed sanitized event structure:
- Repeatability / count:
- Limitations, false positives, side effects:
- Next verification step:

## First local validation checklist

- [ ] Confirm Steam build and `/json/list` target selection
- [ ] Run `node listener.mjs` with **no** `--trace-calls`
- [ ] Confirm `subscribed` and `ready` records
- [ ] Start a test game; confirm Start, TaskChange and End callback types
- [ ] Confirm no tokens, messages, friend IDs or raw callback data in logs
- [ ] Ctrl+C, stop debug-enabled Steam, and relaunch normally
- [ ] Update this file with a reviewed result; **never** add the raw JSONL file

Future tracks: downloads/installs, friends/presence, messages, Steam client version drift, external observer lifetime/reconnect, and a read-only adapter for ControllerPlayground. Observe -> reproduce -> integrate, without replacing the working SteamKit2 path until separately validated.
