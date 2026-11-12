param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('inspect', 'guide-open', 'guide-closed', 'focused', 'exit-open', 'exit-closed', 'exit-focus-restored')]
    [string]$Scenario,
    [string]$AutomationId,
    [string]$Baseline,
    [ValidateRange(0, 60)]
    [int]$DelaySeconds = 0,
    [string]$Report
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Observation.ps1"

$result = [ordered]@{
    schemaVersion = 1
    layer = 'windows-uia'
    scenario = $Scenario.ToLowerInvariant()
    capturedUtc = $null
    status = 'passed'
    diagnosticCode = $null
    message = $null
    expectedAutomationId = $AutomationId
    baseline = $null
    evidence = $null
}
$exitCode = 0
$baselineReport = $null
try {
    if (($Scenario -eq 'focused') -ne (![string]::IsNullOrWhiteSpace($AutomationId))) {
        throw 'Supply -AutomationId exactly when running the focused scenario.'
    }
    if (($Scenario -eq 'exit-focus-restored') -ne (![string]::IsNullOrWhiteSpace($Baseline))) {
        throw 'Supply -Baseline exactly when running exit-focus-restored.'
    }
    if ($Baseline) {
        $baselineReport = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
        $before = $baselineReport.evidence
        if ($baselineReport.schemaVersion -ne 1 -or $baselineReport.layer -cne 'windows-uia' -or
            $baselineReport.scenario -cne 'focused' -or $baselineReport.status -cne 'passed' -or
            $before.focus.automationId -cne 'ExitAppButton' -or !$before.focus.runtimeId -or
            !$before.processId -or !$before.processStartedUtc -or !$before.windowHandle -or
            $before.markers.ExitAppButton -ne 1 -or $before.markers.CancelExitButton -ne 0 -or
            $before.markers.ConfirmExitButton -ne 0) {
            throw 'Capture a passing focused scenario for ExitAppButton with confirmation closed before opening it.'
        }
        $result.baseline = $before
    }
} catch {
    $result.status = 'error'
    $result.diagnosticCode = 'invalid_arguments'
    $result.message = $_.Exception.Message
    $exitCode = 2
}

if ($exitCode -eq 0) {
    if ($DelaySeconds) { Start-Sleep -Seconds $DelaySeconds }
    $result.capturedUtc = [DateTime]::UtcNow.ToString('o')
    $session = (Get-Process -Id $PID).SessionId
    $apps = @(Get-Process -Name ControllerPlayground -ErrorAction SilentlyContinue |
        Where-Object SessionId -eq $session)

    if ($apps.Count -ne 1) {
        $result.status = 'unavailable'
        if ($apps.Count -eq 0) {
            $result.diagnosticCode = 'app_not_running'
            $result.message = 'Start ControllerPlayground in the same Windows desktop session as this command.'
        } else {
            $result.diagnosticCode = 'multiple_app_instances'
            $result.message = 'Close extra ControllerPlayground instances before inspecting the main window.'
        }
        $exitCode = 3
    } else {
        try {
            $result.evidence = Get-UiSnapshot $apps[0]
        } catch {
            $result.status = 'unavailable'
            $result.diagnosticCode = 'accessibility_tree_unavailable'
            $result.message = "Could not read the ControllerPlayground accessibility tree. Use an unlocked desktop and run the app and runner at the same privilege level. $($_.Exception.Message)"
            $exitCode = 3
        }
        if ($exitCode -eq 0) {
            $failure = Get-UiAssertionFailure $result.scenario $result.evidence $AutomationId $baselineReport
            if ($failure) {
                $result.status = 'failed'
                $result.diagnosticCode = 'assertion_failed'
                $result.message = $failure
                $exitCode = 1
            }
        }
    }
}

if ($Report) {
    try {
        $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Report)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    } catch {
        $result.status = 'error'
        $result.diagnosticCode = 'report_write_failed'
        $result.message = $_.Exception.Message
        $exitCode = 2
    }
}
$result | ConvertTo-Json -Depth 8
exit $exitCode
