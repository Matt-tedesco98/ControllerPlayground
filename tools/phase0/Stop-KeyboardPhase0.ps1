param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name ControllerPlayground,ControllerPlayground.KeyboardHost -ErrorAction SilentlyContinue) {
    throw 'Close both application windows first. This script will not terminate them.'
}
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
foreach ($taskRelative in @("bin\x64\$Configuration\net8.0-windows10.0.19041.0\AppX", "ControllerPlayground.KeyboardHost\bin\x64\$Configuration\net8.0-windows10.0.19041.0\AppX")) {
    $taskMarker = Join-Path (Join-Path $taskRoot $taskRelative) 'phase0-probe.log'
    if (Test-Path -LiteralPath $taskMarker) { Remove-Item -LiteralPath $taskMarker }
}
$taskManifest = Join-Path $taskRoot 'phase0-deployment-backup.log'
if (Test-Path -LiteralPath $taskManifest) {
    foreach ($taskEntry in (Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json)) {
        $taskTarget = [IO.Path]::GetFullPath($taskEntry.Target)
        $taskSaved = [IO.Path]::GetFullPath($taskEntry.Saved)
        if (-not $taskTarget.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not $taskSaved.StartsWith((Join-Path $taskRoot 'bin\phase0-backup-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected backup path; restore aborted.' }
        Copy-Item -LiteralPath $taskSaved -Destination $taskTarget
    }
    Remove-Item -LiteralPath $taskManifest
}
Write-Output 'Phase 0 disabled; backed-up Debug deployment binaries restored when present. Logs retained.'
