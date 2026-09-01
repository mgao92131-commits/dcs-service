param([string]$Exe = "..\bin\DcsDataService.exe", [int]$Port = 18081)
$ErrorActionPreference = "Stop"
$exePath = if ([IO.Path]::IsPathRooted($Exe)) { $Exe } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Exe)) }
$temp = Join-Path $env:TEMP ("dcs-service-api-test-" + [Guid]::NewGuid().ToString("N")); New-Item -ItemType Directory -Path $temp | Out-Null
$config = Join-Path $temp "config.ini"; $runtimeExe = Join-Path $temp "DcsDataService.exe"; Copy-Item -LiteralPath $exePath -Destination $runtimeExe
@"
[Historian]
Server=APP
ConnectionTimeoutSeconds=1
TestTag=TEST
ReadChunkSamples=10
StreamWindowMinutes=60
[Events]
Server=invalid
Database=invalid
Schema=dbo
Table=Journal
CommandTimeoutSeconds=1
RuntimeStateCacheSeconds=1
[Api]
Port=$Port
[Concurrency]
HistoryMaxConcurrent=2
EventMaxConcurrent=4
RequestQueueLimit=8
[Timeout]
ProviderSlotWaitSeconds=2
SocketReadSeconds=2
SocketWriteSeconds=2
[Time]
SourceTimeZone=China Standard Time
[Files]
Logs=$temp\logs
"@ | Set-Content -Encoding ASCII $config
$process = Start-Process -FilePath $runtimeExe -ArgumentList @("serve", "--config", $config) -PassThru -WindowStyle Hidden
try {
    $ready = $false; for ($i=0; $i -lt 30; $i++) { try { $c = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port); $c.Close(); $ready=$true; break } catch { Start-Sleep -Milliseconds 100 } }; if (-not $ready) { throw "Server did not start." }
    function Invoke-Raw([string]$request) { $client = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port); try { $stream=$client.GetStream(); $stream.ReadTimeout=5000; $bytes=[Text.Encoding]::UTF8.GetBytes($request); $stream.Write($bytes,0,$bytes.Length); $stream.Flush(); $reader=New-Object IO.StreamReader($stream); return $reader.ReadToEnd() } finally { $client.Close() } }
    $r = Invoke-Raw "GET /health HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 200" -or $r -notmatch '"status":"ok"') { throw "health failed: $r" }
    $r = Invoke-Raw "GET /api/v1/info HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch '"historyMaxConcurrent":2' -or $r -notmatch '"eventMaxConcurrent":4') { throw "info failed: $r" }
    $r = Invoke-Raw "POST /api/v1/history/query HTTP/1.1`r`nHost: localhost`r`nContent-Length: 0`r`n`r`n"; if ($r -notmatch "HTTP/1.1 404") { throw "old history route was not removed: $r" }
    $r = Invoke-Raw "GET /api/v1/history?tag=A&from=2026-08-30T10%3A00%3A00&to=2026-08-30T09%3A00%3A00 HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 400" -or $r -notmatch "application/json") { throw "history validation failed: $r" }
    $r = Invoke-Raw "GET /api/v1/events?from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00&afterTime=2026-08-30T08%3A00%3A00&afterFracSec=1&afterOrd=1 HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 400") { throw "event mode exclusivity failed: $r" }
    $r = Invoke-Raw "GET /api/v1/events?afterTime=2026-08-30T08%3A00%3A00&afterFracSec=1&afterOrd=1&sourceGeneration=G HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 400") { throw "event cursor without to was accepted: $r" }
    $r = Invoke-Raw "GET /api/v1/tag?tag=A HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 503" -or $r -notmatch "historian_unavailable") { throw "Historian unavailable test failed: $r" }
    $r = Invoke-Raw "GET /api/v1/events?from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00&limit=1 HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 400" -or $r -notmatch "invalid_request") { throw "Removed event limit was not rejected: $r" }
    $r = Invoke-Raw "GET /api/v1/events?from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00 HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 503" -or $r -notmatch "event_unavailable") { throw "Event unavailable test failed: $r" }
    $held = @()
    try {
        for ($i=0; $i -lt 8; $i++) { $client = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port); $bytes=[Text.Encoding]::ASCII.GetBytes("GET /health HTTP/1.1`r`n"); $client.GetStream().Write($bytes,0,$bytes.Length); $held += $client }
        $r = Invoke-Raw "GET /health HTTP/1.1`r`nHost: localhost`r`n`r`n"; if ($r -notmatch "HTTP/1.1 429" -or $r -notmatch "service_busy") { throw "queue limit failed: $r" }
    } finally { foreach ($client in $held) { $client.Close() } }; Start-Sleep -Milliseconds 200
    $r = Invoke-Raw "POST /missing HTTP/1.1`r`nHost: localhost`r`nContent-Length: 1048577`r`n`r`n"; if ($r -notmatch "HTTP/1.1 413") { throw "request size test failed: $r" }
    Write-Host "LOCALHOST API TEST PASSED"
} finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }; Remove-Item -LiteralPath $temp -Recurse -Force }
