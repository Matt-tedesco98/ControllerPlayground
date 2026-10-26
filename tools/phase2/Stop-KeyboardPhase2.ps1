$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process -Name ControllerPlayground,ControllerPlayground.KeyboardHost -ErrorAction SilentlyContinue) { throw 'Close both apps first. Existing processes will not be stopped.' }
foreach ($taskRelative in @('bin\x64\Debug\net8.0-windows10.0.19041.0\AppX\phase2-smoke.log','ControllerPlayground.KeyboardHost\bin\x64\Debug\net8.0-windows10.0.19041.0\AppX\phase2-delay-open.log')) {
    $taskMarker = Join-Path $taskRoot $taskRelative
    if (Test-Path -LiteralPath $taskMarker) { Remove-Item -LiteralPath $taskMarker }
}
$taskManifest = Join-Path $taskRoot 'phase2-deployment-backup.log'
if (Test-Path -LiteralPath $taskManifest) {
    foreach ($taskEntry in (Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json)) {
        $taskTarget = [IO.Path]::GetFullPath($taskEntry.Target)
        $taskSaved = [IO.Path]::GetFullPath($taskEntry.Saved)
        if (-not $taskTarget.StartsWith($taskRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $taskSaved.StartsWith((Join-Path $taskRoot 'bin\phase2-backup-'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected restore path.' }
        Copy-Item -LiteralPath $taskSaved -Destination $taskTarget
    }
    Remove-Item -LiteralPath $taskManifest
}
Write-Output 'Smoke markers removed and prior deployment restored. Logs and backup files retained.'
