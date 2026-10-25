param([Parameter(Mandatory=$true)][guid]$Run)
$ErrorActionPreference='Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$taskRecords = [Collections.Generic.List[object]]::new()
foreach ($taskFile in (Get-ChildItem -LiteralPath $taskRoot -Filter ('phase0-' + $Run.ToString('N') + '-*.log'))) {
    foreach ($taskLine in (Get-Content -LiteralPath $taskFile.FullName)) {
        try { $taskRecords.Add(($taskLine | ConvertFrom-Json)) } catch { } # Last buffered line may be partial while live.
    }
}
function Get-LatencySummary($taskValues) {
    $taskSorted = @($taskValues | Sort-Object)
    if ($taskSorted.Count -eq 0) { return @{Count=0} }
    @{Count=$taskSorted.Count; P50ms=$taskSorted[[Math]::Max(0,[int][Math]::Ceiling(.5*$taskSorted.Count)-1)];
      P95ms=$taskSorted[[Math]::Max(0,[int][Math]::Ceiling(.95*$taskSorted.Count)-1)];
      P99ms=$taskSorted[[Math]::Max(0,[int][Math]::Ceiling(.99*$taskSorted.Count)-1)]; MaxMs=$taskSorted[-1]}
}
$taskRoundtrips = @($taskRecords | Where-Object kind -eq 'ROUNDTRIP')
$taskCaptures = @($taskRecords | Where-Object kind -eq 'CAPTURE')
$taskShellIds = @($taskRecords | Where-Object { $_.kind -eq 'INVENTORY' -and $_.role -eq 'shell' } | ForEach-Object { $_.data } | ForEach-Object { $_.id } | Sort-Object -Unique)
$taskHelperIds = @($taskRecords | Where-Object { $_.kind -eq 'INVENTORY' -and $_.role -eq 'helper' } | ForEach-Object { $_.data } | ForEach-Object { $_.id } | Sort-Object -Unique)
$taskDuplicates = @($taskCaptures | Where-Object { -not $_.data.Repeat -and $_.data.Action -ne 'Guide' } |
    Group-Object { $_.data.Id + ':' + $_.data.NativeTime + ':' + $_.data.Action } |
    Where-Object { @($_.Group.role | Sort-Object -Unique).Count -gt 1 })
[pscustomobject]@{
    Run=$Run; Records=$taskRecords.Count;
    Processes=@($taskRecords | Where-Object kind -eq 'START' | ForEach-Object data);
    ShellIds=$taskShellIds; HelperIds=$taskHelperIds;
    SameNonemptyObservedIds=($taskShellIds.Count -gt 0 -and $taskHelperIds.Count -gt 0 -and -not (Compare-Object $taskShellIds $taskHelperIds));
    Captures=@($taskCaptures | Group-Object role | Select-Object Name,Count);
    ForegroundCaptures=@($taskCaptures | Where-Object {$_.data.Foreground} | Group-Object {$_.role + ':' + $_.data.Family} | Select-Object Name,Count);
    BackgroundCaptures=@($taskCaptures | Where-Object {-not $_.data.Foreground} | Group-Object {$_.role + ':' + $_.data.Action} | Select-Object Name,Count);
    PingUiRoundtrip=(Get-LatencySummary @($taskRoundtrips | Where-Object {$_.data.Kind -eq 'ping'} | ForEach-Object {$_.data.elapsedMs}));
    ActionUiRoundtrip=(Get-LatencySummary @($taskRoundtrips | Where-Object {$_.data.Kind -eq 'action'} | ForEach-Object {$_.data.elapsedMs}));
    AuthorizedOrdinaryUiRoundtrip=(Get-LatencySummary @($taskRoundtrips | Where-Object {$_.data.mockExecuted -and $_.data.input.Action -ne 'Guide'} | ForEach-Object {$_.data.elapsedMs}));
    DiagnosticOnlyExecutions=@($taskRoundtrips | Where-Object {$_.data.mockExecuted}).Count;
    RejectedActions=@($taskRoundtrips | Where-Object {$_.data.Kind -eq 'action' -and -not $_.data.Approved}).Count;
    CrossProcessSameSampleCandidates=$taskDuplicates.Count;
    ProbeQueueDrops=@($taskRecords | Where-Object kind -eq 'PROBE_QUEUE_DROP').Count;
    PipeEnds=@($taskRecords | Where-Object kind -eq 'PIPE_END' | ForEach-Object data);
    FocusTransitions=@($taskRecords | Where-Object kind -eq 'FOCUS' | Select-Object utc,role,data)
} | ConvertTo-Json -Depth 8
