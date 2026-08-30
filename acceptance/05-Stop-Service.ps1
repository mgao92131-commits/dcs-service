. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
$pidPath = Join-Path $AcceptanceDir "service.pid"
if (-not (Test-Path -LiteralPath $pidPath)) { Write-Host "No recorded service PID."; exit 0 }
$servicePid = [int]([IO.File]::ReadAllText($pidPath).Trim())
$process = Get-Process -Id $servicePid -ErrorAction SilentlyContinue
if ($process -ne $null) {
    if ($process.ProcessName -ne "DcsDataService") { throw "Refusing to stop PID $servicePid because it is $($process.ProcessName), not DcsDataService." }
    Stop-Process -Id $servicePid
    $process.WaitForExit(10000) | Out-Null
    Write-Host "Stopped DcsDataService PID=$servicePid"
} else {
    Write-Host "Recorded service process is no longer running: PID=$servicePid"
}
Remove-Item -LiteralPath $pidPath -Force
