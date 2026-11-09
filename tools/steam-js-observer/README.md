# Steam JavaScript observer (research only)

This independent Node.js probe connects to Steam's **local Chromium DevTools Protocol (CDP)** `SharedJSContext` target. It records a sanitized inventory of `SteamClient` method names and subscribes to the three **previously observed** `SteamClient.Apps` game-action callbacks. It intentionally does **not** launch games, send messages, change settings, or invoke Steam's private action methods.

**Status:** Prototype, not verified against the current Steam client from this repository. No production ControllerPlayground integration. The research branch should not be merged wholesale into the application.

## Run on the Windows Steam machine

Requires Node.js 22.4+; no npm dependencies.

1. Exit Steam completely. Start Steam with `steam.exe -cef-enable-debugging` in a trusted, local Windows session. Confirm `http://127.0.0.1:8080/json/list` exposes a `SharedJSContext` target.
2. From this folder run `node listener.mjs`. This connects only to localhost and saves a new `captures/steam-*.jsonl` file.
3. Launch and exit a test game in Steam; inspect the JSON Lines file for `surface`, `subscribed`, and `callback` entries. `GameActionEnd` means the *launch action* finished, not necessarily that the game process exited.
4. Press Ctrl+C to unsubscribe. Restart Steam **without** the CEF debugging flag when finished.

For opt-in function-entry research, use `node listener.mjs --trace-calls`. This temporarily wraps eligible JS methods in `Apps`, `Downloads`, and `GameSessions` in the selected context; it can affect Steam behavior, so **do not enable it for normal use**. The wrapper logs **method names and argument types only**. It observes only calls through those properties **after attachment**, not arbitrary native IPC calls, calls in other Steam browser targets, or references already cached before injection. Stop the probe to restore wrappers where possible; a Steam restart is safest after tracing.

Optional flags: `--port 8080`, `--out captures/my-run.jsonl`. Output files are created with exclusive creation so existing evidence isn't overwritten.

Run the dependency-free probe tests with `node --test tests/*.test.mjs`.

## Evidence and privacy policy

- `captures/` and `*.jsonl` are gitignored. **Never commit raw event logs**, API results, user identifiers, authorization tokens, friends/chat content, cookies, personal information, or full CDP packets.
- Commit *curated findings* as Markdown: Steam client version/date, observed API name, manually reviewed non-sensitive event structure, confidence, and reproducible test steps. Minimize test app identifiers if not required.
- Events are best-effort and may change with Steam updates. A `SharedJSContext` observer is **not** proof of external Steam IPC access or independent C# communication.
- CDP allows high-privilege operations. Use only loopback, don't expose the port externally, don't run untrusted scripts while debugging is active, and disable it promptly afterward.

## Known starting evidence (from previous manual testing)

A Steam client debugging session exposed 49 top-level `SteamClient` namespaces. `Apps.RegisterForGameActionStart`, `Apps.RegisterForGameActionTaskChange`, and `Apps.RegisterForGameActionEnd` generated real callbacks during a test launch. The precise signatures and subscription objects must be rechecked against the Steam version under test. `ipc_SteamClient.log` observations do not establish that every frontend-to-backend call is captured by this JS observer.

Future validated milestones: expand subscriptions incrementally (downloads, installs, friends/presence), compare same action across Steam versions, identify which context originates each call, test a separate local relay only after sanitization and lifecycle correctness, and finally build an optional read-only service adapter without replacing SteamKit2.
