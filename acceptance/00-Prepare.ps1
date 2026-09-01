. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runDir = Join-Path $ServiceRoot ("evidence\run_" + $stamp)
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
Save-Text (Join-Path $AcceptanceDir "current-run.txt") $runDir

$config = @"
[Historian]
Server=$HistorianServer
ConnectionTimeoutSeconds=30
TestTag=$HistoryTag
ReadChunkSamples=$HistorySplitMaxSamples
StreamWindowMinutes=$StreamWindowMinutes

[Events]
Server=$EventServer
Database=$EventDatabase
Schema=$EventSchema
Table=$EventTable
CommandTimeoutSeconds=60
RuntimeStateCacheSeconds=30

[Api]
Port=$ApiPort

[Concurrency]
HistoryMaxConcurrent=2
EventMaxConcurrent=4
RequestQueueLimit=32

[Timeout]
ProviderSlotWaitSeconds=$ProviderSlotWaitSeconds
SocketReadSeconds=$SocketReadSeconds
SocketWriteSeconds=$SocketWriteSeconds

[Time]
SourceTimeZone=$SourceTimeZone

[Files]
Logs=$ServiceRoot\logs
"@
Save-Text (Join-Path $ServiceRoot "config.ini") $config

$gitCommit = "not available in deployment copy"
$gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
if ($gitCommand -ne $null) {
    try { $gitCommit = (& $gitCommand.Path -C $ServiceRoot rev-parse HEAD 2>$null) } catch { }
}
$environment = @"
AcceptanceStarted=$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
ComputerName=$env:COMPUTERNAME
User=$env:USERDOMAIN\$env:USERNAME
ServiceRoot=$ServiceRoot
DeltaVDllDir=$DeltaVDllDir
HistorianServer=$HistorianServer
HistoryTag=$HistoryTag
EventServer=$EventServer
EventDatabase=$EventDatabase
GitCommit=$gitCommit
"@
Save-Text (Join-Path $runDir "00-environment.txt") $environment

Write-Host "Prepared acceptance run: $runDir"
Write-Host "Generated config.ini (not tracked by Git)."
