$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process -Name ControllerPlayground,ControllerPlayground.KeyboardHost -ErrorAction SilentlyContinue) { throw 'Close both apps first. Existing processes will not be stopped.' }
$taskManifest = Join-Path $taskRoot 'phase2-deployment-backup.log'
if (Test-Path -LiteralPath $taskManifest) { throw 'Run Stop-KeyboardPhase2.ps1 to restore the previous diagnostic deployment first.' }
$taskBackup = Join-Path $taskRoot ('bin\phase2-backup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskBackup | Out-Null
$taskRestores = @()
foreach ($taskRelative in @('bin\x64\Debug\net8.0-windows10.0.19041.0', 'ControllerPlayground.KeyboardHost\bin\x64\Debug\net8.0-windows10.0.19041.0')) {
    $taskBuild = Join-Path $taskRoot $taskRelative
    $taskAppx = Join-Path $taskBuild 'AppX'
    $taskName = if ($taskRelative.StartsWith('ControllerPlayground.KeyboardHost')) { 'ControllerPlayground.KeyboardHost' } else { 'ControllerPlayground' }
    foreach ($taskFile in @(($taskName+'.dll'), 'ControllerPlayground.Input.dll', 'ControllerInput.dll', 'resources.pri')) {
        $taskTarget = Join-Path $taskAppx $taskFile
        $taskSaved = Join-Path $taskBackup ($taskName+'-'+$taskFile)
        Copy-Item -LiteralPath $taskTarget -Destination $taskSaved
        $taskRestores += [pscustomobject]@{Target=$taskTarget;Saved=$taskSaved}
        $taskRestores | ConvertTo-Json | Set-Content -LiteralPath $taskManifest
        Copy-Item -LiteralPath (Join-Path $taskBuild $taskFile) -Destination $taskTarget
    }
    $taskMarker = if ($taskName -eq 'ControllerPlayground') { 'phase2-smoke.log' } else { 'phase2-delay-open.log' }
    New-Item -ItemType File -Path (Join-Path $taskAppx $taskMarker) | Out-Null
}
# Uses existing package registration; no installation or policy changes.
Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\1f976d86-31bc-42a4-a14e-40fb2eb4fc98_f6s3q6fqr0004!App'
Write-Output 'Debug lifecycle smoke started. Leave controllers idle. Inspect packaged shell ownership log for PHASE2_SMOKE COMPLETE, then close both apps and run Stop-KeyboardPhase2.ps1.'
