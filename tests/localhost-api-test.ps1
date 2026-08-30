param(
    [string]$Exe = "..\bin\DcsDataService.exe",
    [int]$Port = 18081
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exePath = if ([IO.Path]::IsPathRooted($Exe)) { $Exe } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Exe)) }
$temp = Join-Path $env:TEMP ("dcs-service-api-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null
$config = Join-Path $temp "config.ini"
$runtimeExe = Join-Path $temp "DcsDataService.exe"
Copy-Item -LiteralPath $exePath -Destination $runtimeExe
@"
[Historian]
Server=APP
ConnectionTimeoutSeconds=1
TestTag=TEST
[Events]
Server=invalid
Database=invalid
Schema=dbo
Table=Journal
CommandTimeoutSeconds=1
StateCacheSeconds=1
[Api]
Bind=127.0.0.1
Port=$Port
ApiKey=TEST_KEY
[ApiLimits]
MaxTagsPerRequest=2
MaxEventRows=10
MaxRequestBytes=128
MaxHistorySpanHours=1
MaxSamplesPerRead=10
MaxSamplesPerRequest=20
MaxResponseBytes=65536
RequestTimeoutSeconds=2
[Time]
SourceTimeZone=China Standard Time
[Files]
Logs=$temp\logs
"@ | Set-Content -Encoding ASCII $config
$process = Start-Process -FilePath $runtimeExe -ArgumentList @("serve", "--config", $config) -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($i=0; $i -lt 30; $i++) { try { $c = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port); $c.Close(); $ready=$true; break } catch { Start-Sleep -Milliseconds 100 } }
    if (-not $ready) { throw "Server did not start." }
    function Invoke-Raw([string]$request) {
        $client = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port)
        try { $stream=$client.GetStream(); $stream.ReadTimeout=5000; $stream.WriteTimeout=5000; $bytes=[Text.Encoding]::UTF8.GetBytes($request); $stream.Write($bytes,0,$bytes.Length); $stream.Flush(); $reader=New-Object IO.StreamReader($stream); return $reader.ReadToEnd() } finally { $client.Close() }
    }
    Write-Host "Testing health..."
    $r = Invoke-Raw "GET /health HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`n`r`n"
    if ($r -notmatch "HTTP/1.1 200" -or $r -notmatch '"ok":true' -or $r -notmatch '"status":"ok"') { throw "health failed: $r" }
    Write-Host "Testing API key..."
    $r = Invoke-Raw "GET /health HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: WRONG`r`n`r`n"
    if ($r -notmatch "HTTP/1.1 401") { throw "API key test failed: $r" }
    Write-Host "Testing info and route bounds..."
    $r = Invoke-Raw "GET /api/v1/info HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`n`r`n"
    if ($r -notmatch "HTTP/1.1 200" -or $r -notmatch '"readOnly":true') { throw "info failed: $r" }
    $r = Invoke-Raw "GET /missing HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`n`r`n"
    if ($r -notmatch "HTTP/1.1 404") { throw "unknown route failed: $r" }
    Write-Host "Testing malformed JSON..."
    $body = "{bad"
    $r = Invoke-Raw ("POST /api/v1/tags/resolve HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 400") { throw "invalid JSON test failed: $r" }
    Write-Host "Testing query limits..."
    $body = '{"tags":["A","B","C"]}'
    $r = Invoke-Raw ("POST /api/v1/tags/resolve HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 400") { throw "tag limit test failed: $r" }
    $body = '{"tags":["A"],"start":"2026-08-30 10:00:00","end":"2026-08-30 09:00:00"}'
    $r = Invoke-Raw ("POST /api/v1/history/query HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 400") { throw "history range test failed: $r" }
    $body = '{"after":{"dateTime":"2026-08-30 08:00:00","fracSec":1,"ord":1},"limit":1}'
    $r = Invoke-Raw ("POST /api/v1/events/after HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 400" -or $r -notmatch "sourceGeneration") { throw "event generation requirement failed: $r" }
    Write-Host "Testing unavailable providers..."
    $body = '{"tags":["A"]}'
    $r = Invoke-Raw ("POST /api/v1/tags/resolve HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 503" -or $r -notmatch "historian_unavailable") { throw "Historian unavailable test failed: $r" }
    $body = '{"from":"2026-08-30 08:00:00","to":"2026-08-30 09:00:00","limit":1}'
    $r = Invoke-Raw ("POST /api/v1/events/query HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: " + [Text.Encoding]::UTF8.GetByteCount($body) + "`r`n`r`n" + $body)
    if ($r -notmatch "HTTP/1.1 503" -or $r -notmatch "event_unavailable") { throw "Event unavailable test failed: $r" }
    Write-Host "Testing request timeout..."
    $client = New-Object Net.Sockets.TcpClient("127.0.0.1", $Port)
    try { $stream=$client.GetStream(); $stream.ReadTimeout=5000; $bytes=[Text.Encoding]::ASCII.GetBytes("GET /health HTTP/1.1`r`n"); $stream.Write($bytes,0,$bytes.Length); $reader=New-Object IO.StreamReader($stream); $r=$reader.ReadToEnd(); if ($r -notmatch "HTTP/1.1 400" -or $r -notmatch "request_timeout") { throw "timeout test failed: $r" } } finally { $client.Close() }
    Write-Host "Testing body limit..."
    $r = Invoke-Raw "POST /api/v1/tags/resolve HTTP/1.1`r`nHost: localhost`r`nX-DCS-API-Key: TEST_KEY`r`nContent-Type: application/json`r`nContent-Length: 129`r`n`r`n"
    if ($r -notmatch "HTTP/1.1 413") { throw "request size test failed: $r" }
    Write-Host "LOCALHOST API TEST PASSED"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }
    Remove-Item -LiteralPath $temp -Recurse -Force
}
