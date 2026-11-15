[CmdletBinding(DefaultParameterSetName = 'Package')]
param(
    [string]$SdkDll = (Join-Path $PSScriptRoot 'HIDMaestro.Core.dll'),
    [Parameter(Mandatory, ParameterSetName = 'Executable')][string]$AppExe,
    [Parameter(ParameterSetName = 'Package')][string]$AppId = '1f976d86-31bc-42a4-a14e-40fb2eb4fc98_f6s3q6fqr0004!App',
    [ValidateSet('xbox-one-original', 'xbox-one-rev1', 'xbox-one-s', 'xbox-360-wired')]
    [string]$XboxProfile = 'xbox-one-original',
    [ValidateSet('dualsense', 'dualsense-bt', 'dualsense-bt-full', 'dualsense-composite')]
    [string]$DualSenseProfile = 'dualsense-composite',
    [switch]$XboxOnly,
    [string]$Report = 'artifacts/virtual-controller-tests.json'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Observation.ps1"
$results = [Collections.Generic.List[object]]::new()
$reportData = [ordered]@{ schemaVersion = 1; layer = 'virtual-gameinput-uia'; status = 'failed'; error = $null; scenarios = $results }

function Wait-Observation {
    param([scriptblock]$Read, [string]$Description, [int]$Seconds = 8)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $Read
        if ($value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw "Timed out waiting for $Description."
}

function Read-Ui {
    $app.Refresh()
    if ($app.HasExited) { throw 'ControllerPlayground exited during the scenario.' }
    $scenario.lastObservation = Get-UiSnapshot $app
    $scenario.lastObservation
}

function Get-Trace {
    @(Get-Content -LiteralPath $traceFile -ErrorAction Stop)
}

function Invoke-ControllerPress {
    param($Pad, [string]$Control, [string]$Action, [string]$Decision, [string]$DeviceId, [string]$ExpectedFocus)
    $app.Refresh()
    if ($app.HasExited -or [ControllerTestWindow]::GetForegroundWindow() -ne $app.MainWindowHandle) {
        throw 'ControllerPlayground lost foreground focus. Input was stopped.'
    }
    if ($ExpectedFocus -and (Read-Ui).focus.automationId -cne $ExpectedFocus) {
        throw "Expected focus on $ExpectedFocus before sending $Control. Input was stopped."
    }
    $before = @(Get-Trace).Count
    $state = [HIDMaestro.HMGamepadState]::new()
    $neutral = [HIDMaestro.HMGamepadState]::new()
    if ($Control -eq 'Down') { $state.Hat = [HIDMaestro.HMHat]::South }
    else { $state.Buttons = [HIDMaestro.HMButton]$Control }
    try {
        $Pad.SubmitState([ref]$state)
        Start-Sleep -Milliseconds 90
    } finally {
        $Pad.SubmitState([ref]$neutral)
    }
    $entry = Wait-Observation {
        Get-Trace | Select-Object -Skip $before | Where-Object {
            $_ -match " ACTION .* id=$([regex]::Escape($DeviceId)) .* action=$Action repeat=False result=$Decision(?: |$)"
        } | Select-Object -First 1
    } "$Action from device $DeviceId with routing result $Decision"
    $scenario.actions.Add($entry)
    Start-Sleep -Milliseconds 140
    $attributed = @(Get-Trace | Select-Object -Skip $before | Where-Object {
        $_ -match " ACTION .* id=$([regex]::Escape($DeviceId)) .* action=$Action(?: |$)"
    })
    if ($attributed.Count -ne 1) { throw "One $Control press produced $($attributed.Count) attributed $Action actions." }
}

function Add-TestPad {
    param([string]$Profile, [string]$Family)
    $before = @(Get-Trace).Count
    $pad = $context.CreateController($context.GetProfile($Profile))
    $pads.Add($pad)
    $neutral = [HIDMaestro.HMGamepadState]::new()
    $pad.SubmitState([ref]$neutral)
    $connection = Wait-Observation {
        Get-Trace | Select-Object -Skip $before | Where-Object {
            $_ -match " DEVICE id=\S+ gen=\d+ connected=True family=$Family(?: |$)"
        } | Select-Object -First 1
    } "$Profile to connect through GameInput" 20
    $scenario.connections.Add($connection)
    $id = [regex]::Match($connection, ' DEVICE id=(\S+)').Groups[1].Value
    @{ Pad = $pad; Id = $id }
}

function Assert-Unchanged {
    param($Baseline, [string]$Description)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $current = Read-Ui
        if (($current.focus.runtimeId -join ',') -cne ($Baseline.focus.runtimeId -join ',') -or
            ($current.markers | ConvertTo-Json -Compress) -cne ($Baseline.markers | ConvertTo-Json -Compress)) {
            throw "$Description changed visible controls or focus."
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.ElapsedMilliseconds -lt 600)
}

$context = $null
$app = $null
$pads = [Collections.Generic.List[IDisposable]]::new()
$previousTrace = $env:CONTROLLERPLAYGROUND_INPUT_TRACE
try {
    if ($PSVersionTable.PSEdition -ne 'Core' -or [Environment]::Version.Major -lt 10 -or !$IsWindows) {
        throw 'Run with Windows PowerShell 7 on .NET 10 or newer (pwsh.exe).'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Open PowerShell as Administrator. HIDMaestro requires elevation to create the virtual controllers.'
        }
    } finally {
        $identity.Dispose()
    }
    if (!(Test-Path -LiteralPath $SdkDll -PathType Leaf)) { throw "HIDMaestro SDK DLL not found at $SdkDll. Copy it beside this script or use -SdkDll." }
    $sdk = (Resolve-Path -LiteralPath $SdkDll).Path
    $exe = if ($AppExe) { (Resolve-Path -LiteralPath $AppExe).Path }
    if (Get-Process -Name ControllerPlayground -ErrorAction SilentlyContinue) {
        throw 'Close existing ControllerPlayground windows before running the automated test.'
    }
    Add-Type -Path $sdk
    $context = [HIDMaestro.HMContext]::new()
    if (!$context.IsDriverInstalled) { throw 'HIDMaestro driver unavailable. Complete the one-time vendor driver setup first.' }
    [void]$context.LoadDefaultProfiles()
    $desktop = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'dotnet/shared/Microsoft.WindowsDesktop.App'
    $runtime = Get-ChildItem -LiteralPath $desktop -Directory | Where-Object {
        $_.Name -match '^\d+\.\d+\.\d+$' -and
            ([version]$_.Name).Major -ge 10 -and
            ([version]$_.Name).Major -le [Environment]::Version.Major
    } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$runtime) { throw 'Install the .NET 10 Windows Desktop Runtime (x64) for UI Automation.' }
    Add-Type -Path @("$($runtime.FullName)/UIAutomationTypes.dll", "$($runtime.FullName)/UIAutomationClient.dll")
    Add-Type '
    using System;
    using System.Runtime.InteropServices;
    public static class ControllerTestWindow {
        [DllImport("user32.dll")]
        public static extern System.IntPtr GetForegroundWindow();

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager {
            uint ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options);
        }

        public static int Activate(string appId) {
            var type = Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c"));
            var manager = (IApplicationActivationManager)Activator.CreateInstance(type);
            try { return checked((int)manager.ActivateApplication(appId, "", 0)); }
            finally { Marshal.ReleaseComObject(manager); }
        }
    }'
    $env:CONTROLLERPLAYGROUND_INPUT_TRACE = '1'
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $traceRoots = @(Join-Path $local 'ControllerPlayground/Logs')
    if (!$exe) {
        $family = ($AppId -split '!', 2)[0]
        $traceRoots += Join-Path $local "Packages/$family/LocalCache/Local/ControllerPlayground/Logs"
    }

    $ownerProfiles = @($XboxProfile)
    if (!$XboxOnly) { $ownerProfiles += $DualSenseProfile }
    foreach ($ownerProfile in $ownerProfiles) {
        $scenario = [ordered]@{
            ownerProfile = $ownerProfile
            guestProfile = if ($XboxOnly -or $ownerProfile -ne $XboxProfile) { $XboxProfile } else { $DualSenseProfile }
            status = 'failed'
            error = $null
            connections = [Collections.Generic.List[string]]::new()
            actions = [Collections.Generic.List[string]]::new()
            observations = [ordered]@{}
            lastObservation = $null
        }
        $results.Add($scenario)
        try {
            $app = if ($exe) {
                Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
            } else {
                Get-Process -Id ([ControllerTestWindow]::Activate($AppId))
            }
            $traceFile = Wait-Observation {
                if ($app.HasExited) { throw "ControllerPlayground exited during startup (exit code $($app.ExitCode)). Use the deployed Debug package for a packaged build." }
                Get-ChildItem -LiteralPath $traceRoots -Filter "ownership-*-$($app.Id).log" -ErrorAction SilentlyContinue |
                    Where-Object { $_.CreationTime -ge $app.StartTime.AddSeconds(-2) } |
                    Select-Object -First 1 -ExpandProperty FullName
            } 'the Debug build ownership trace' 15
            $scenario.observations.home = Wait-Observation {
                try {
                    $ui = Read-Ui
                    if ($ui.focus -and !(Get-UiAssertionFailure 'guide-closed' $ui)) { $ui }
                } catch {
                    if ($app.HasExited) { throw }
                }
            } 'the foreground app on its initial screen' 20
            $xbox = Add-TestPad $XboxProfile 'Xbox'
            $second = if ($XboxOnly) {
                Add-TestPad $XboxProfile 'Xbox'
            } else {
                Add-TestPad $DualSenseProfile 'PlayStation'
            }
            if ($xbox.Id -eq $second.Id) { throw 'GameInput reported the same ID for both virtual controllers.' }
            $owner = $xbox
            $guest = $second
            if ($ownerProfile -ne $XboxProfile) { $owner = $second; $guest = $xbox }

            # Use a Home control as the focus baseline after startup and device enumeration.
            $scenario.observations.guideBaseline = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'guide-closed' $ui) -and
                    $ui.focus.automationId -ceq 'TileButton' -and $ui.focus.runtimeId) { $ui }
            } 'Guide closed with a Home tile focused'

            Invoke-ControllerPress $owner.Pad Guide Guide Accepted $owner.Id
            $guide = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'guide-open' $ui) -and $ui.focus.automationId -eq 'LibraryButton') { $ui }
            } 'Guide open with Library focused'
            $scenario.observations.guide = $guide
            foreach ($press in @(@('Down','NavigateDown'), @('A','Accept'), @('B','Back'))) {
                Invoke-ControllerPress $guest.Pad $press[0] $press[1] Discarded $guest.Id 'LibraryButton'
                Assert-Unchanged $guide "Non-owner $($press[1]) in Guide"
            }

            Invoke-ControllerPress $owner.Pad B Back Accepted $owner.Id
            $scenario.observations.guideRestored = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'guide-closed' $ui) -and
                    $ui.focus.automationId -ceq 'TileButton' -and
                    ($ui.focus.runtimeId -join ',') -ceq ($scenario.observations.guideBaseline.focus.runtimeId -join ',')) { $ui }
            } 'Guide closed with original focus restored'

            Invoke-ControllerPress $owner.Pad Guide Guide Accepted $owner.Id
            Invoke-ControllerPress $owner.Pad Down NavigateDown Accepted $owner.Id
            [void](Wait-Observation { $ui = Read-Ui; if ($ui.focus.automationId -eq 'SettingsButton') { $ui } } 'Guide Settings focused')
            Invoke-ControllerPress $owner.Pad A Accept Accepted $owner.Id 'SettingsButton'
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
            $audioId = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'AudioButton')
            $audio = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'guide-closed' $ui)) {
                    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $audioId)
                    if ($button -and !$button.Current.IsOffscreen -and $button.Current.IsEnabled) { $button }
                }
            } 'Settings visible after opening it from Guide'
            $scenario.observations.settingsBeforeSetup = Read-Ui
            if ($scenario.observations.settingsBeforeSetup.focus.automationId -cne 'AudioButton') {
                $scenario.error = 'Settings opened without focusing Audio. UI Automation set focus to continue the exit checks.'
                $audio.SetFocus()
            }
            $scenario.observations.settings = Wait-Observation {
                $ui = Read-Ui
                if ($ui.focus.automationId -ceq 'AudioButton') { $ui }
            } 'Audio focused for the Settings navigation checks'

            foreach ($expected in 'DisplayButton', 'ControllersButton', 'PowerButton') {
                Invoke-ControllerPress $owner.Pad Down NavigateDown Accepted $owner.Id
                [void](Wait-Observation { $ui = Read-Ui; if ($ui.focus.automationId -eq $expected) { $ui } } "$expected focused")
            }
            Invoke-ControllerPress $owner.Pad A Accept Accepted $owner.Id 'PowerButton'
            [void](Wait-Observation { $ui = Read-Ui; if ($ui.focus.automationId -eq 'SleepButton') { $ui } } 'Power panel initial focus')
            foreach ($expected in 'RestartButton', 'ShutdownButton', 'ExitAppButton') {
                Invoke-ControllerPress $owner.Pad Down NavigateDown Accepted $owner.Id
                [void](Wait-Observation { $ui = Read-Ui; if ($ui.focus.automationId -eq $expected) { $ui } } "$expected focused")
            }
            $baseline = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'exit-closed' $ui) -and $ui.focus.automationId -eq 'ExitAppButton') { $ui }
            } 'Power panel with Exit focused'
            $scenario.observations.exitBaseline = $baseline
            Invoke-ControllerPress $owner.Pad A Accept Accepted $owner.Id 'ExitAppButton'
            $confirmation = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'exit-open' $ui)) { $ui }
            } 'exit confirmation open with Cancel focused'
            $scenario.observations.confirmation = $confirmation
            foreach ($press in @(@('Down','NavigateDown'), @('A','Accept'), @('B','Back'))) {
                Invoke-ControllerPress $guest.Pad $press[0] $press[1] Discarded $guest.Id 'CancelExitButton'
                Assert-Unchanged $confirmation "Non-owner $($press[1]) in exit confirmation"
            }
            Invoke-ControllerPress $owner.Pad B Back Accepted $owner.Id
            $scenario.observations.exitRestored = Wait-Observation {
                $ui = Read-Ui
                if (!(Get-UiAssertionFailure 'exit-focus-restored' $ui -Baseline @{ evidence = $baseline })) { $ui }
            } 'exit confirmation closed with focus restored'
            if (!$scenario.error) { $scenario.status = 'passed' }
        } catch {
            $scenario.error = (@($scenario.error, $_.Exception.Message) | Where-Object { $_ }) -join ' '
        } finally {
            try {
                foreach ($pad in $pads) { $pad.Dispose() }
                $pads.Clear()
            } finally {
                if ($app -and !$app.HasExited) {
                    [void]$app.CloseMainWindow()
                    if (!$app.WaitForExit(5000)) { throw 'The test app did not close. Close it before the next run.' }
                }
                if ($app) { $app.Dispose() }
                $app = $null
            }
        }
    }
    if (!($results | Where-Object status -ne 'passed')) { $reportData.status = 'passed' }
} catch {
    $reportData.error = $_.Exception.Message
} finally {
    $env:CONTROLLERPLAYGROUND_INPUT_TRACE = $previousTrace
    if ($context) { $context.Dispose() }
}
$path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Report)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
$json = $reportData | ConvertTo-Json -Depth 12
Set-Content -LiteralPath $path -Value $json -Encoding utf8
$json
if ($reportData.status -ne 'passed') { exit 1 }
