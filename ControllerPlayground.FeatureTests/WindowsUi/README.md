# Inspect the running app with Windows UI Automation

Start one instance of ControllerPlayground on an unlocked Windows desktop.
Run the commands from the repository root in the same session and at the same
privilege level as the app. These commands use Windows PowerShell 5.1 and
Windows' built-in UI Automation client. They take one read-only sample of a
settled UI state. Use the app's controls or a physical controller to reach the
state first. For focus checks, use the delay to switch back to the app and
leave it in the foreground. The delay does not inject input or activate a window.

```powershell
$runner = '.\ControllerPlayground.FeatureTests\WindowsUi\Invoke-UiScenario.ps1'
powershell.exe -NoProfile -MTA -File $runner -Scenario inspect -Report artifacts/ui-inspect.json
powershell.exe -NoProfile -MTA -File $runner -Scenario guide-open -DelaySeconds 5
powershell.exe -NoProfile -MTA -File $runner -Scenario guide-closed -DelaySeconds 5
powershell.exe -NoProfile -MTA -File $runner -Scenario focused -AutomationId LibraryButton -DelaySeconds 5
```

| Scenario | Assertion |
| --- | --- |
| `inspect` | Reads the window, visible marker counts and current in-window keyboard focus |
| `guide-open`, `guide-closed` | The Guide's `DiscordButton` is visible exactly once, or has zero visible instances |
| `focused` | The focused element has the exact, case-sensitive `-AutomationId` |
| `exit-open` | Both confirmation buttons are visible and `CancelExitButton` has focus |
| `exit-closed` | The Settings exit button is visible and both confirmation buttons are hidden |
| `exit-focus-restored` | Confirmation is closed and the same Settings exit button has focus in the same running window |

For the exit sequence, navigate to Settings and focus **Exit ControllerPlayground**
with confirmation closed. Capture the baseline. Then open confirmation with the
controller's Accept action and sample while focus remains on Cancel. Dismiss
with Cancel or the controller's Back action. Take the last sample after
dismissal settles, keeping the app in the foreground. Use the same app instance
and Settings screen throughout.

```powershell
powershell.exe -NoProfile -MTA -File $runner -Scenario focused -AutomationId ExitAppButton -DelaySeconds 5 -Report artifacts/exit-before.json
powershell.exe -NoProfile -MTA -File $runner -Scenario exit-open -DelaySeconds 5 -Report artifacts/exit-open.json
powershell.exe -NoProfile -MTA -File $runner -Scenario exit-focus-restored -Baseline artifacts/exit-before.json -DelaySeconds 5 -Report artifacts/exit-after.json
```

Restoration compares the before and after observations, including the process
start time, main window handle, and UI Automation runtime ID. The intermediate
`exit-open` report records the confirmation separately. If WinUI recreates the
button or loses desktop focus, restoration fails with evidence of the observed
state. A visibility result describes UI Automation exposure and offscreen
state. Controller ownership, GameInput and virtual HID are outside this layer.

Every executed scenario prints schema-versioned JSON. `-Report` also writes
it to a file. Evidence includes the target process and window, visible marker
counts, and the focused element's automation ID, name, type and runtime ID
when Windows reports focus within that window.

| Exit code | Meaning |
| --- | --- |
| 0 | Observation or assertion passed |
| 1 | Observed state failed the assertion |
| 2 | Invalid scenario options, baseline, or report path |
| 3 | App or accessibility tree unavailable |

For `app_not_running`, start the app in the same desktop session. For
`accessibility_tree_unavailable`, wait for startup, restore the window, and
check that the app and the observer are running at the same privilege level.
The runner requires one visible `ShellGuideButton` to establish that the app's
WinUI controls are present before interpreting missing overlay markers.

The Windows CI job exercises the fixed assertions against fixture snapshots,
loads the Windows UI Automation assemblies, and verifies the CLI's JSON with
the app closed. Use the commands above on an interactive Windows desktop to
verify the running WinUI tree. The existing managed runner remains available
through its original `run --all` command.
