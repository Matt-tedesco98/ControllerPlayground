param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process -Name ControllerPlayground,ControllerPlayground.KeyboardHost -ErrorAction SilentlyContinue) {
    throw 'Close existing shell and KeyboardHost windows before starting Phase 0. This script will not stop existing processes.'
}
$taskShell = Join-Path $taskRoot "bin\x64\$Configuration\net8.0-windows10.0.19041.0\AppX\ControllerPlayground.exe"
$taskHelper = Join-Path $taskRoot "ControllerPlayground.KeyboardHost\bin\x64\$Configuration\net8.0-windows10.0.19041.0\AppX\ControllerPlayground.KeyboardHost.exe"
foreach ($taskExe in @($taskShell, $taskHelper)) {
    if (-not (Test-Path -LiteralPath $taskExe)) { throw "Build/deploy the $Configuration x64 solution first: $taskExe" }
}
$taskManifest = Join-Path $taskRoot 'phase0-deployment-backup.log'
if (Test-Path -LiteralPath $taskManifest) { throw 'A diagnostic deployment backup exists. Run Stop-KeyboardPhase0.ps1 first.' }
$taskBackup = Join-Path $taskRoot ('bin\phase0-backup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskBackup | Out-Null
$taskRestores = @()
foreach ($taskExe in @($taskShell, $taskHelper)) {
    $taskAppx = Split-Path -Parent $taskExe
    $taskBuild = Split-Path -Parent $taskAppx
    $taskAppName = [IO.Path]::GetFileNameWithoutExtension($taskExe)
    foreach ($taskFile in @(($taskAppName + '.dll'),'ControllerPlayground.Input.dll','ControllerInput.dll')) {
        $taskTarget = Join-Path $taskAppx $taskFile
        $taskSaved = Join-Path $taskBackup ($taskAppName + '-' + $taskFile)
        Copy-Item -LiteralPath $taskTarget -Destination $taskSaved
        $taskRestores += [pscustomobject]@{Target=$taskTarget;Saved=$taskSaved}
        $taskRestores | ConvertTo-Json | Set-Content -LiteralPath $taskManifest
        Copy-Item -LiteralPath (Join-Path $taskBuild $taskFile) -Destination $taskTarget
    }
}
$taskRun = [guid]::NewGuid()
foreach ($taskExe in @($taskShell, $taskHelper)) {
    $taskMarker = Join-Path (Split-Path -Parent $taskExe) 'phase0-probe.log'
    if (Test-Path -LiteralPath $taskMarker) { throw 'Previous Phase 0 markers exist. Run Stop-KeyboardPhase0.ps1 first.' }
}
foreach ($taskExe in @($taskShell, $taskHelper)) {
    @{Run=$taskRun.ToString();Directory=$taskRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $taskExe) 'phase0-probe.log')
}
# Activate the registered packages, not their EXEs directly: Windows App SDK
# requires the package dependency graph. No registration or installation occurs.
Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\1f976d86-31bc-42a4-a14e-40fb2eb4fc98_f6s3q6fqr0004!App'
Start-Sleep -Milliseconds 750
Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\2d72be25-6768-4b4e-96a4-f3705bd815ff_f6s3q6fqr0004!App'
[pscustomobject]@{Run=$taskRun;Logs=(Join-Path $taskRoot 'phase0-*.log')}
# These are interactive hardware-test windows. Close both normally after testing.
# No user/machine environment variables, package registrations or Steam settings change.
