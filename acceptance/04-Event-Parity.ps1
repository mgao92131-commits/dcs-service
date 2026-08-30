. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Common.ps1")
Assert-Configured
Assert-ServiceRunning
$runDir = Get-RunDirectory
$exporter = Join-Path $ServiceRoot "bin\EventParityExport.exe"
if (-not (Test-Path -LiteralPath $exporter)) { throw "Event reference exporter is missing. Run RUN-01-BUILD-PROBE.bat first." }

$artifactPrefix = "event"
if ((Test-Path -LiteralPath (Join-Path $runDir "event-range-old.json")) -or
    (Test-Path -LiteralPath (Join-Path $runDir "event-range-new.json"))) {
    $artifactPrefix = "event-" + (Get-Date -Format "HHmmss")
}

$rangeOld = Join-Path $runDir ($artifactPrefix + "-range-old.json")
$rangeNew = Join-Path $runDir ($artifactPrefix + "-range-new.json")
$rangeLegacyLog = Join-Path $runDir ($artifactPrefix + "-range-legacy-console.txt")
$rawEventStart = Convert-SourceToRawUtcText $EventStart
$rawEventEnd = Convert-SourceToRawUtcText $EventEnd
& $exporter range --server $EventServer --database $EventDatabase --schema $EventSchema --table $EventTable --timeout 30 --from $rawEventStart --to $rawEventEnd --limit $EventLimit --out $rangeOld 2>&1 | Tee-Object -FilePath $rangeLegacyLog
if ($LASTEXITCODE -ne 0) { throw "Legacy Event range export failed." }

$rangeBody = '{"from":' + (Quote-Json $EventStart) + ',"to":' + (Quote-Json $EventEnd) + ',"limit":' + $EventLimit + '}'
$rangeResponse = Invoke-HttpJson "POST" "/api/v1/events/query" $rangeBody $rangeNew
Require-Http200 $rangeResponse "event range"

$rangeParity = Join-Path $runDir ($artifactPrefix + "-range-parity.txt")
& (Join-Path $ServiceRoot "bin\ParityVerifier.exe") event $rangeOld $rangeNew 2>&1 | Tee-Object -FilePath $rangeParity
if ($LASTEXITCODE -ne 0) { throw "Event range parity failed." }

$range = Read-Json $rangeNew
$data = $range["data"]
$events = $data["events"]
if ($events -eq $null -or $events.Count -eq 0) { throw "Event range returned no rows. Adjust EventStart/EventEnd." }
$generation = [string]$data["sourceGeneration"]
# Use the first returned cursor so both implementations read the same following page.
$cursor = $events[0]["cursor"]
$cursorDate = [string]$cursor["dateTime"]
$rawCursorDate = Convert-SourceCursorToRawUtcText $cursorDate
$cursorFrac = [int]$cursor["fracSec"]
$cursorOrd = [int]$cursor["ord"]

$afterOld = Join-Path $runDir ($artifactPrefix + "-after-old.json")
$afterNew = Join-Path $runDir ($artifactPrefix + "-after-new.json")
$afterLegacyLog = Join-Path $runDir ($artifactPrefix + "-after-legacy-console.txt")
& $exporter after --server $EventServer --database $EventDatabase --schema $EventSchema --table $EventTable --timeout 30 --cursor-date $rawCursorDate --cursor-frac $cursorFrac --cursor-ord $cursorOrd --limit $EventLimit --out $afterOld 2>&1 | Tee-Object -FilePath $afterLegacyLog
if ($LASTEXITCODE -ne 0) { throw "Legacy Event after export failed." }
$oldAfter = Read-Json $afterOld
if ($oldAfter["records"].Count -ne $EventLimit) {
    throw "Legacy Event after returned fewer than EventLimit rows. Choose an older EventStart so the live journal cannot change parity between the two reads."
}

$afterBody = '{"sourceGeneration":' + (Quote-Json $generation) + ',"after":{"dateTime":' + (Quote-Json $cursorDate) + ',"fracSec":' + $cursorFrac + ',"ord":' + $cursorOrd + '},"limit":' + $EventLimit + '}'
$afterResponse = Invoke-HttpJson "POST" "/api/v1/events/after" $afterBody $afterNew
Require-Http200 $afterResponse "event after"

$afterParity = Join-Path $runDir ($artifactPrefix + "-after-parity.txt")
& (Join-Path $ServiceRoot "bin\ParityVerifier.exe") event $afterOld $afterNew 2>&1 | Tee-Object -FilePath $afterParity
if ($LASTEXITCODE -ne 0) { throw "Event after parity failed." }

Write-Host "EVENT RANGE AND AFTER PARITY PASSED"
