. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured
$runDir = Get-RunDirectory

if (-not (Test-Path -LiteralPath (Join-Path $DeltaVDllDir "DeltaV.Historian.DvCHDataAccess.dll"))) {
    throw "DvCHDataAccess DLL not found in DeltaVDllDir: $DeltaVDllDir"
}
$env:DELTAV_DLL_DIR = $DeltaVDllDir
$env:LEGACY_EVENT_DIR = $LegacyEventSourceDir

$legacyHistory = Join-Path $AcceptanceDir "legacy-history\HistoryReader.exe"
$legacyLibrary = Join-Path $AcceptanceDir "legacy-history\DcsData.Historian.dll"
if ((Get-Sha256 $legacyHistory) -ne "a23da914e1ce033b08dfcd1f228b5bf31b92408eecf14802212680038839eb93") { throw "Legacy HistoryReader hash mismatch." }
if ((Get-Sha256 $legacyLibrary) -ne "62ca257db93d5b5600aa14b92dc0ed8f0834f028607bd8a115b95bf305a943ff") { throw "Legacy Historian library hash mismatch." }

function Run-Captured([string]$Name, [string]$Command) {
    $output = Join-Path $runDir $Name
    & $env:COMSPEC /d /c $Command 2>&1 | Tee-Object -FilePath $output
    $code = $LASTEXITCODE
    Add-Content -Path $output -Value ("ExitCode=" + $code) -Encoding Unicode
    if ($code -ne 0) { throw ($Name + " failed with exit code " + $code) }
}

Push-Location $ServiceRoot
try {
    Run-Captured "01-build.txt" 'call build-net35-x86.bat'
    Run-Captured "02-core-tests.txt" 'call tests\run-core-tests.bat'
    Run-Captured "03-parity-tool-build.txt" 'call acceptance\build-event-reference.bat && call tests\build-parity-verifier.bat'
    Run-Captured "04-version.txt" 'bin\DcsDataService.exe --version'
    Run-Captured "05-probe.txt" 'bin\DcsDataService.exe probe --config config.ini'
} finally {
    Pop-Location
}

Write-Host "BUILD AND PROBE PASSED"
Write-Host "Evidence: $runDir"
