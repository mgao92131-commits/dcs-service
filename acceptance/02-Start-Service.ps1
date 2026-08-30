. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured
$runDir = Get-RunDirectory
$pidPath = Join-Path $AcceptanceDir "service.pid"
if (Test-Path -LiteralPath $pidPath) {
    $oldPid = [IO.File]::ReadAllText($pidPath).Trim()
    if ($oldPid -match '^\d+$' -and (Get-Process -Id ([int]$oldPid) -ErrorAction SilentlyContinue)) {
        throw "A recorded service process is already running: PID $oldPid"
    }
    Remove-Item -LiteralPath $pidPath -Force
}

$exe = Join-Path $ServiceRoot "bin\DcsDataService.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Service is not built. Run RUN-01-BUILD-PROBE.bat first." }
$stdout = Join-Path $runDir "06-service-stdout.txt"
$stderr = Join-Path $runDir "06-service-stderr.txt"
$process = Start-Process -FilePath $exe -ArgumentList @("serve", "--config", (Join-Path $ServiceRoot "config.ini")) -WorkingDirectory $ServiceRoot -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
Save-Text $pidPath ([string]$process.Id)

$ready = $false
for ($i = 0; $i -lt 50; $i++) {
    if ($process.HasExited) { throw "Service exited during startup. See $stderr" }
    try {
        $client = New-Object Net.Sockets.TcpClient($ApiBind, $ApiPort)
        $client.Close()
        $ready = $true
        break
    } catch { Start-Sleep -Milliseconds 200 }
}
if (-not $ready) { throw "Service did not listen within 10 seconds." }

$healthPath = Join-Path $runDir "07-health.json"
$health = Invoke-HttpJson "GET" "/health" "" $healthPath
Require-Http200 $health "health"
Write-Host ("SERVICE STARTED PID=" + $process.Id)
Write-Host "Health response: $healthPath"

