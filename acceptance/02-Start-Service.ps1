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

$portCheck = New-Object Net.Sockets.TcpClient
$portInUse = $false
try {
    $portCheck.Connect($ApiBind, $ApiPort)
    $portInUse = $true
} catch { } finally { $portCheck.Close() }
if ($portInUse) { throw "Port $ApiBind`:$ApiPort is already in use. Stop the existing listener or choose another acceptance port." }

$exe = Join-Path $ServiceRoot "bin\DcsDataService.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Service is not built. Run RUN-01-BUILD-PROBE.bat first." }
$configPath = Join-Path $ServiceRoot "config.ini"
$startEvidence = Join-Path $runDir "06-service-start.txt"
$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $exe
$startInfo.Arguments = 'serve --config "' + $configPath + '"'
$startInfo.WorkingDirectory = $ServiceRoot
$startInfo.UseShellExecute = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$process = [Diagnostics.Process]::Start($startInfo)
if ($process -eq $null) { throw "ProcessStartInfo did not return a service process." }
Save-Text $pidPath ([string]$process.Id)
Save-Text $startEvidence ("Started=" + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + "`r`nPID=" + $process.Id + "`r`nExecutable=" + $exe + "`r`nConfig=" + $configPath + "`r`nServiceLogDirectory=" + (Join-Path $ServiceRoot "logs") + "`r`n")

try {
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        if ($process.HasExited) { throw "Service exited during startup with code $($process.ExitCode). See the service log under $(Join-Path $ServiceRoot 'logs')." }
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
} catch {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if (Test-Path -LiteralPath $pidPath) { Remove-Item -LiteralPath $pidPath -Force }
    throw
}

