. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured
Assert-ServiceRunning
$runDir = Get-RunDirectory
$exporter = Join-Path $ServiceRoot "bin\EventParityExport.exe"
if (-not (Test-Path -LiteralPath $exporter)) { throw "Event reference exporter is missing. Run RUN-01-BUILD-PROBE.bat first." }

$artifactPrefix = "event"
if ((Test-Path -LiteralPath (Join-Path $runDir "event-range-old.json")) -or
    (Test-Path -LiteralPath (Join-Path $runDir "event-range-new.csv"))) {
    $artifactPrefix = "event-" + (Get-Date -Format "HHmmss")
}

$rangeOld = Join-Path $runDir ($artifactPrefix + "-range-old.json")
$rangeNew = Join-Path $runDir ($artifactPrefix + "-range-new.csv")
$rangeLegacyLog = Join-Path $runDir ($artifactPrefix + "-range-legacy-console.txt")
$rawEventStart = Convert-SourceToRawUtcText $EventStart
$rawEventEnd = Convert-SourceToRawUtcText $EventEnd
& $exporter range --server $EventServer --database $EventDatabase --schema $EventSchema --table $EventTable --timeout 30 --from $rawEventStart --to $rawEventEnd --out $rangeOld 2>&1 | Tee-Object -FilePath $rangeLegacyLog
if ($LASTEXITCODE -ne 0) { throw "Legacy Event range export failed." }

$rangePath = "/api/v1/events?from=" + [Uri]::EscapeDataString($EventStart) + "&to=" + [Uri]::EscapeDataString($EventEnd)
$rangeResponse = Invoke-HttpCsv $rangePath $rangeNew
Require-Http200 $rangeResponse "event range"

$rangeParity = Join-Path $runDir ($artifactPrefix + "-range-parity.txt")
& (Join-Path $ServiceRoot "bin\ParityVerifier.exe") event $rangeOld $rangeNew $SourceTimeZone 2>&1 | Tee-Object -FilePath $rangeParity
if ($LASTEXITCODE -ne 0) { throw "Event range parity failed." }

$events = @(Import-Csv -LiteralPath $rangeNew)
if ($events.Count -eq 0) { throw "Event range returned no rows. Adjust EventStart/EventEnd." }
$generation = [string]$rangeResponse.Headers["X-DCS-Source-Generation"]
$cursorDate = [string]$events[0].DateTime
$cursorFracText = [string]$events[0].FracSec
$cursorOrdText = [string]$events[0].Ord
$rawCursorDate = Convert-SourceCursorToRawUtcText $cursorDate
$cursorFrac = [int]$cursorFracText
$cursorOrd = [int]$cursorOrdText

$afterOld = Join-Path $runDir ($artifactPrefix + "-after-old.json")
$afterNew = Join-Path $runDir ($artifactPrefix + "-after-new.csv")
$afterLegacyLog = Join-Path $runDir ($artifactPrefix + "-after-legacy-console.txt")
& $exporter after --server $EventServer --database $EventDatabase --schema $EventSchema --table $EventTable --timeout 30 --cursor-date $rawCursorDate --cursor-frac $cursorFrac --cursor-ord $cursorOrd --to $rawEventEnd --out $afterOld 2>&1 | Tee-Object -FilePath $afterLegacyLog
if ($LASTEXITCODE -ne 0) { throw "Legacy Event after export failed." }
$oldAfter = Read-Json $afterOld

$afterPath = "/api/v1/events?afterTime=" + [Uri]::EscapeDataString($cursorDate) + "&afterFracSec=" + $cursorFrac + "&afterOrd=" + $cursorOrd + "&sourceGeneration=" + [Uri]::EscapeDataString($generation) + "&to=" + [Uri]::EscapeDataString($EventEnd)
$afterResponse = Invoke-HttpCsv $afterPath $afterNew
Require-Http200 $afterResponse "event after"

$afterParity = Join-Path $runDir ($artifactPrefix + "-after-parity.txt")
& (Join-Path $ServiceRoot "bin\ParityVerifier.exe") event $afterOld $afterNew $SourceTimeZone 2>&1 | Tee-Object -FilePath $afterParity
if ($LASTEXITCODE -ne 0) { throw "Event after parity failed." }

Write-Host "EVENT RANGE AND AFTER PARITY PASSED"
