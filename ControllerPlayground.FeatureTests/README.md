# AI-invokable ControllerPlayground feature tests

This is a small **deterministic command-line runner**, not an AI agent. Codex,
Claude Code, a human developer, or CI can ask it to list and run *registered*
scenarios. Only the runner's C# assertions decide pass/fail.

The managed scenarios invoke the **real managed `ControllerInputRouter`**
with synthetic, controller-attributed actions. They do not exercise the WinUI
application, Microsoft GameInput, actual USB/Bluetooth devices, or virtual HID
controllers. A passing result is **not an end-to-end UI or hardware pass**.

## Commands (repository root, .NET 8)

```powershell
dotnet run --project ControllerPlayground.FeatureTests -- list
dotnet run --project ControllerPlayground.FeatureTests -- list --json
dotnet run --project ControllerPlayground.FeatureTests -- run --feature exit-confirmation
dotnet run --project ControllerPlayground.FeatureTests -- run --id controller-ownership.disconnect-fresh-claim
dotnet run --project ControllerPlayground.FeatureTests -- run --all --json --report artifacts/feature-tests.json
```

Registered feature selectors: `exit-confirmation`, `guide-overlay`, and
`controller-ownership`. Use `list --json` to discover scenario IDs, feature
names, descriptions, and their evidence layer programmatically.

The CLI requires **exactly one** of `--feature`, `--id`, or `--all` for
`run`. Unknown selectors are errors, not silent passes. The test catalog is
fixed in code; AI cannot replace assertions at execution time.

Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Every selected scenario passed |
| 1 | At least one selected scenario failed |
| 2 | Invalid invocation or failure to write the requested report |

`--json` prints a machine-readable result with schema version, timestamp,
scenario IDs, **layer**, timings, status, and exception details. `--report`
writes the same JSON to disk, whether or not `--json` is selected.

## How to invoke from an AI coding assistant

For example, ask Codex or Claude Code:

> List available ControllerPlayground feature tests. Run the
> `exit-confirmation` scenarios and report their exact pass/fail evidence.
> Do not change the assertions or production code.

The assistant calls the CLI via its terminal tools. **No MCP server is required.**
AI usage is for choosing/invoking tests and interpreting reports, not executing
each simulated button press.

## Trust boundary and next increments

1. **Now:** deterministic managed-router assertions using synthetic
   `ControllerActionEvent`s; these tests are fast and device-free.
2. **Now, opt-in:** [Windows UI Automation inspection](WindowsUi/README.md)
   checks observable accessibility state in a running WinUI app. It uses a
   separate entry point and report.
3. **Then:** a virtual-controller adapter, *after* confirming its Xbox/other
   virtual devices are discovered by the native GameInput capture. Virtual
   device presence alone does not prove Guide-button semantics, identities,
   detach/reconnect behavior, or physical device compatibility.
4. **Optional later:** an MCP wrapper that exposes `list_tests`,
   `run_feature`, and `read_report` backed by these exact CLI selectors.

Keep real-hardware qualification separate. Do not claim an AI screenshot or
scripted keyboard input validates native GameInput ownership or Windows focus.
This project has **no write access to the running shell and no arbitrary
command-execution API**.
