# Automated Xbox and PS5 controller checks

This opt-in suite launches the Debug WinUI app and creates a virtual wired Xbox One
and a full USB PS5 DualSense with HIDMaestro. It sends the button reports,
checks the app's GameInput routing trace, and asserts the accessibility state.
Each profile gets a fresh app session as the owner, with the other profile
acting as a second controller.

- GameInput must enumerate two different device IDs and attribute each tested
  press to the intended device. A single press must yield one matching action.
- The owner opens Guide, navigates to Settings, and opens exit confirmation.
  The runner checks visibility and focus, and verifies that Back restores focus
  to the same control after closing each overlay.
- The other controller sends Down, Accept, and Back while each overlay is
  open. Each action must be discarded, with focus and the observed controls
  staying unchanged. The test never deliberately activates Confirm or a
  system power action.

Use an unlocked Windows desktop, a fresh Debug build of ControllerPlayground,
PowerShell 7 running on .NET 10, and the .NET 10 Windows Desktop Runtime.
Close any running copy of ControllerPlayground and keep the test window in
the foreground. The script starts and closes its own copy of the app.

One-time setup uses the [HIDMaestro v2.1.0 release](https://github.com/hifihedgehog/HIDMaestro/releases/tag/v2.1.0).
Extract the release for your machine and install its user-mode driver using
the vendor's [setup instructions](https://github.com/hifihedgehog/HIDMaestro).
That setup can require administrator access and adds a locally trusted
signing certificate. The test script checks for the driver before starting.

Place the extracted HIDMaestro.Core.dll beside Invoke-VirtualControllerTests.ps1 and run from an Administrator terminal at the repo root. Use -SdkDll to override the default path.
The default launch uses the registered Debug package. Deploy the **ControllerPlayground
(Package)** Debug profile from Visual Studio first if it is missing or stale.
For a working unpackaged Debug build, you can supply its path with `-AppExe`.

```powershell
pwsh.exe -NoProfile -File .\ControllerPlayground.FeatureTests\WindowsUi\Invoke-VirtualControllerTests.ps1
```

The runner writes `artifacts/virtual-controller-tests.json` with both profiles'
connection records, routing decisions, accessibility observations, and any
failure. Exit code 0 means both scenarios passed. Exit code 1 means setup or
a scenario failed. The regular managed tests and hosted CI have no driver
dependency. The suite targets Guide and exit confirmation. Other screens and
input sources, such as Steam keyboard or mouse remapping, need their own cases.

The default profiles are `xbox-one-original` and `dualsense-composite`.
HIDMaestro's full wired DualSense uses its USB transport, which the SDK may
set up on first use. Use `-XboxOnly` to test with two Xbox One controllers,
or `-XboxProfile xbox-360-wired` to reproduce the Xbox 360 configuration.
