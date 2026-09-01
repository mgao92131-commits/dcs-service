param([ValidateSet("Normal", "AutoSplit")][string]$Mode = "Normal")
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured
Assert-ServiceRunning
$runDir = Get-RunDirectory

if ($Mode -eq "Normal") {
    $start = $HistoryNormalStart; $end = $HistoryNormalEnd; $max = $HistoryNormalMaxSamples; $prefix = "history-normal"
} else {
    $start = $HistorySplitStart; $end = $HistorySplitEnd; $max = $HistorySplitMaxSamples; $prefix = "history-autosplit"
}

# Preserve evidence from an earlier attempt instead of mixing or deleting it.
$artifactPrefix = $prefix
if ((Test-Path -LiteralPath (Join-Path $runDir ($prefix + "-old"))) -or
    (Test-Path -LiteralPath (Join-Path $runDir ($prefix + "-new.csv")))) {
    $artifactPrefix = $prefix + "-" + (Get-Date -Format "HHmmss")
}

$legacyExe = Join-Path $AcceptanceDir "legacy-history\HistoryReader.exe"
if (-not (Test-Path -LiteralPath $legacyExe)) { throw "Legacy HistoryReader is missing: $legacyExe" }
$oldDir = Join-Path $runDir ($artifactPrefix + "-old")
New-Item -ItemType Directory -Path $oldDir -Force | Out-Null
$legacyLog = Join-Path $runDir ($artifactPrefix + "-legacy-console.txt")

Push-Location $oldDir
try {
    & $legacyExe export --server $HistorianServer --tag $HistoryTag --start $start --end $end --max $max --out-dir $oldDir 2>&1 | Tee-Object -FilePath $legacyLog
    $legacyExit = $LASTEXITCODE
} finally { Pop-Location }
Add-Content -Path $legacyLog -Value ("ExitCode=" + $legacyExit) -Encoding Unicode
if ($legacyExit -ne 0) { throw "Legacy HistoryReader failed with exit code $legacyExit" }

$csv = @(Get-ChildItem -LiteralPath $oldDir -Filter "*.csv")
if ($csv.Count -ne 1) { throw "Expected exactly one legacy CSV, found $($csv.Count)." }

$newCsv = Join-Path $runDir ($artifactPrefix + "-new.csv")
$path = "/api/v1/history?tag=" + [Uri]::EscapeDataString($HistoryTag) + "&from=" + [Uri]::EscapeDataString($start) + "&to=" + [Uri]::EscapeDataString($end)
$response = Invoke-HttpCsv $path $newCsv
Require-Http200 $response ($Mode + " history query")

$newRows = @(Import-Csv -LiteralPath $newCsv)
$sampleCount = $newRows.Count
if ($Mode -eq "AutoSplit" -and $sampleCount -le $max) {
    throw "AUTOSPLIT NOT PROVEN: sampleCount=$sampleCount is not greater than per-read max=$max. Increase the interval or choose a denser tag."
}

$parityLog = Join-Path $runDir ($artifactPrefix + "-parity.txt")
$verifier = Join-Path $ServiceRoot "bin\ParityVerifier.exe"
& $verifier history $csv[0].FullName $newCsv $SourceTimeZone 2>&1 | Tee-Object -FilePath $parityLog
$parityExit = $LASTEXITCODE
Add-Content -Path $parityLog -Value ("ExitCode=" + $parityExit) -Encoding Unicode
if ($parityExit -ne 0) { throw ($Mode + " history parity failed.") }

Write-Host ($Mode.ToUpperInvariant() + " HISTORY PARITY PASSED; sampleCount=" + $sampleCount + "; perReadMax=" + $max)

