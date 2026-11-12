$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Observation.ps1"

function Assert-Scenario {
    param($Scenario, [bool]$ShouldPass, $AutomationId, $Baseline)

    $failure = Get-UiAssertionFailure $Scenario $snapshot $AutomationId $Baseline
    if (($null -eq $failure) -ne $ShouldPass) {
        throw "Unexpected result for $Scenario (expected pass = $ShouldPass). $failure"
    }
}

$snapshot = @{
    processId = 42
    processStartedUtc = '2026-01-01T00:00:00Z'
    windowHandle = '0x123'
    markers = @{ DiscordButton = 0; ExitAppButton = 1; CancelExitButton = 0; ConfirmExitButton = 0 }
    focus = @{ automationId = 'ExitAppButton'; runtimeId = @(1, 2, 3) }
}
$baseline = @{ evidence = $snapshot.Clone() }

Assert-Scenario guide-closed $true
Assert-Scenario guide-open $false
$snapshot.markers.DiscordButton = 1
Assert-Scenario guide-open $true
Assert-Scenario guide-closed $false
$snapshot.markers.DiscordButton = 2
Assert-Scenario guide-open $false
$snapshot.markers.DiscordButton = 0

Assert-Scenario focused $true ExitAppButton
Assert-Scenario focused $false exitappbutton
Assert-Scenario exit-closed $true
Assert-Scenario exit-focus-restored $true -Baseline $baseline
$snapshot.focus = @{ automationId = 'ExitAppButton'; runtimeId = @(1, 2, 4) }
Assert-Scenario exit-focus-restored $false -Baseline $baseline
$snapshot.focus = $baseline.evidence.focus
$snapshot.processStartedUtc = '2026-01-02T00:00:00Z'
Assert-Scenario exit-focus-restored $false -Baseline $baseline

$snapshot.markers.CancelExitButton = 1
Assert-Scenario exit-closed $false
Assert-Scenario exit-open $false
$snapshot.markers.ConfirmExitButton = 1
Assert-Scenario exit-open $false
$snapshot.focus = @{ automationId = 'CancelExitButton'; runtimeId = @(1, 2, 5) }
Assert-Scenario exit-open $true
$snapshot.focus = $null
Assert-Scenario exit-open $false
Assert-Scenario focused $false CancelExitButton

try {
    Get-UiSnapshot @{ MainWindowHandle = [IntPtr]::Zero }
    throw 'Expected the missing-window diagnostic.'
} catch {
    if ($_.Exception.Message -notlike '*has no main window yet*') { throw }
}

$runner = Join-Path $PSScriptRoot 'Invoke-UiScenario.ps1'
$reportPath = Join-Path ([IO.Path]::GetTempPath()) "controller-uia-$([guid]::NewGuid()).json"
try {
    $output = & powershell.exe -NoProfile -MTA -ExecutionPolicy Bypass -File $runner -Scenario focused
    if ($LASTEXITCODE -ne 2 -or ($output | ConvertFrom-Json).diagnosticCode -cne 'invalid_arguments') {
        throw "Expected a JSON usage error for focused without -AutomationId. $output"
    }

    $output = & powershell.exe -NoProfile -MTA -ExecutionPolicy Bypass -File $runner -Scenario inspect -Report $reportPath
    $result = $output | ConvertFrom-Json
    $saved = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($LASTEXITCODE -ne 3 -or $result.schemaVersion -ne 1 -or
        $result.layer -cne 'windows-uia' -or $result.status -cne 'unavailable' -or
        $result.diagnosticCode -cne 'app_not_running' -or $null -ne $result.evidence -or
        ($result | ConvertTo-Json -Depth 8 -Compress) -cne ($saved | ConvertTo-Json -Depth 8 -Compress)) {
        throw "Expected the app-not-running report in stdout and on disk. Close ControllerPlayground before running these tests. $output"
    }
} finally {
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
}

Write-Output 'Windows UI Automation scenario assertions and CLI diagnostics passed.'
