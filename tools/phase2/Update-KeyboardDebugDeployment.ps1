$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process -Name ControllerPlayground,ControllerPlayground.KeyboardHost -ErrorAction SilentlyContinue) { throw 'Close both apps normally before refreshing their deployment. Existing processes will not be stopped.' }
if (Test-Path -LiteralPath (Join-Path $taskRoot 'phase2-deployment-backup.log')) { throw 'Restore the diagnostic smoke deployment with Stop-KeyboardPhase2.ps1 first.' }
$taskBackup = Join-Path $taskRoot ('bin\phase2-deployment-backup-' + [guid]::NewGuid().ToString('N'))
$taskCopies = @()
foreach ($taskRelative in @('bin\x64\Debug\net8.0-windows10.0.19041.0', 'ControllerPlayground.KeyboardHost\bin\x64\Debug\net8.0-windows10.0.19041.0')) {
    $taskBuild = Join-Path $taskRoot $taskRelative
    $taskAppx = Join-Path $taskBuild 'AppX'
    $taskName = if ($taskRelative.StartsWith('ControllerPlayground.KeyboardHost')) { 'ControllerPlayground.KeyboardHost' } else { 'ControllerPlayground' }
    foreach ($taskMarker in @('phase0-probe.log','phase2-smoke.log','phase2-delay-open.log')) {
        if (Test-Path -LiteralPath (Join-Path $taskAppx $taskMarker)) { throw "Diagnostic marker exists: $taskAppx\$taskMarker. Disable diagnostics before normal deployment." }
    }
    foreach ($taskFile in @(($taskName+'.dll'), 'ControllerPlayground.Input.dll', 'ControllerInput.dll', 'resources.pri')) {
        $taskSource = Join-Path $taskBuild $taskFile
        $taskTarget = Join-Path $taskAppx $taskFile
        if (-not (Test-Path -LiteralPath $taskSource) -or -not (Test-Path -LiteralPath $taskTarget)) { throw "Build and deploy Debug x64 first: $taskSource" }
        $taskCopies += [pscustomobject]@{Source=$taskSource;Target=$taskTarget;Saved=(Join-Path $taskBackup ($taskName+'-'+$taskFile))}
    }
}
New-Item -ItemType Directory -Path $taskBackup | Out-Null
# Preflight every path and back up every target before changing any deployment.
foreach ($taskCopy in $taskCopies) { Copy-Item -LiteralPath $taskCopy.Target -Destination $taskCopy.Saved }
$taskCopies | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBackup 'manifest.json')
foreach ($taskCopy in $taskCopies) {
    Copy-Item -LiteralPath $taskCopy.Source -Destination $taskCopy.Target
    if ((Get-FileHash -LiteralPath $taskCopy.Source).Hash -ne (Get-FileHash -LiteralPath $taskCopy.Target).Hash) { throw 'Deployment hash verification failed.' }
}
Write-Output "Matching Debug shell/helper deployment refreshed and hash-verified. Prior files retained in $taskBackup. No diagnostic markers, activation, registration or settings changes."
