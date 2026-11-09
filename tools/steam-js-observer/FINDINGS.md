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

## November 9, 2026 — live JavaScript observer validation

**Evidence:** Locally observed `SharedJSContext` with the read-only listener (no `--trace-calls`), from an uploaded JSONL capture. Steam client build was not recorded. This is a **two-launch sample**, not a broad compatibility test.

- 82 valid observer records: **49 namespace inventories**, **3 successful callback subscriptions**, **1 ready signal**, **29 callback events**.
- API inventory: **782 namespace-qualified method entries** across 49 namespaces (**766 unique unqualified method names**). This is an observed surface listing, **not verification that all methods work**. See [research/steam-api-surface.json](research/steam-api-surface.json).
- **Counter-Strike 2** (Steam app 730): one `GameActionStart`, **13** `GameActionTaskChange` stages, one `GameActionEnd`.
  - Stages in observed order: `UpdatingAppInfo`, `CheckShaderDepotManifest`, `RunningInstallScript`, `SynchronizingCloud`, `SynchronizingStats`, `ShowInterstitials`, `SynchronizingControllerConfig`, `VerifyingFiles`, `SiteLicenseSeatCheckout`, `DelayLaunch`, `CreatingProcess`, `WaitingGameWindow`, `Completed`.
- **Phasmophobia** (Steam app 739630): one `GameActionStart`, **12** task stages, one `GameActionEnd`.
  - Stages in observed order: `ShowLaunchOption`, `CheckShaderDepotManifest`, `RunningInstallScript`, `SynchronizingCloud`, `SynchronizingStats`, `ShowInterstitials`, `SynchronizingControllerConfig`, `SiteLicenseSeatCheckout`, `DelayLaunch`, `CreatingProcess`, `WaitingGameWindow`, `Completed`.
- Callback **argument types only** (values beyond sanitized app IDs/stage names were not retained):
  - `Apps.RegisterForGameActionStart`: `[number, string, string, number]`
  - `Apps.RegisterForGameActionTaskChange`: `[number, string, string, string, string]`
  - `Apps.RegisterForGameActionEnd`: `[number]`
- **Important limitation:** The `GameActionEnd` callback **does not contain a captured app ID**. Its association with each sequential launch is inferred from event order. It means the launch **action ended**, not that the actual game process exited. We have **not** demonstrated game lifetime notifications or process exit detection.
- The sanitized, ordered task sequences and callback-type observations are in [research/game-launch-events.json](research/game-launch-events.json). Neither JSON file contains timestamps, Steam account details, message content, raw argument values, or CDP packets.

**Next experiment:** Carefully register `GameSessions.RegisterForAppLifetimeNotifications` to determine whether it reports real game start/exit events, and document callback type/field shape first. Its semantics are currently **unverified**.

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

- [x] Confirm `/json/list` exposes `SharedJSContext` (Steam build still unknown)
- [x] Run `node listener.mjs` with **no** `--trace-calls`
- [x] Confirm `subscribed` and `ready` records
- [x] Start test games; confirm Start, TaskChange and End callback types
- [x] Review the uploaded capture fields: no tokens, chat content, friend IDs or raw callback data found
- [ ] Ctrl+C, stop debug-enabled Steam, and relaunch normally
- [x] Update findings and sanitized datasets; **never** add the raw JSONL file

Future tracks: downloads/installs, friends/presence, messages, Steam client version drift, external observer lifetime/reconnect, and a read-only adapter for ControllerPlayground. Observe -> reproduce -> integrate, without replacing the working SteamKit2 path until separately validated.
