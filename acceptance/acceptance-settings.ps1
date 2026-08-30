# Edit this file on the DCS computer before running RUN-00-PREPARE.bat.
# All DateTime values are DeltaV source-local times; no UTC conversion is done.

$DeltaVDllDir = "C:\DeltaV\bin"
$HistorianServer = "APP"
$HistoryTag = "012-P01HZX/PID1/PV.CV"

# A short, known-good interval for ordinary parity.
$HistoryNormalStart = "2026-08-30 10:00:00"
$HistoryNormalEnd = "2026-08-30 10:10:00"
$HistoryNormalMaxSamples = 10000

# Choose a dense tag/interval. The final row count MUST exceed this per-read max.
$HistorySplitStart = "2026-08-29 00:00:00"
$HistorySplitEnd = "2026-08-29 01:00:00"
$HistorySplitMaxSamples = 100

$EventServer = "ES01\DELTAV_CHRONICLE"
$EventDatabase = "EJournal"
$EventSchema = "dbo"
$EventTable = "Journal"
# These are raw Event Journal Date_Time values, selected near the probe's
# latest cursor. They are not the Windows wall clock shown on the DCS desktop.
$EventStart = "2026-08-30 06:00:00"
$EventEnd = "2026-08-30 06:30:00"
$EventLimit = 50

$ApiBind = "127.0.0.1"
$ApiPort = 18080
$ApiKey = "DCS_ACCEPTANCE_LOCAL_20260830"
$SourceTimeZone = "China Standard Time"

# Safety limits used during acceptance.
$MaxHistorySpanHours = 24
$MaxSamplesPerRead = 10000
$MaxSamplesPerRequest = 50000
$MaxResponseBytes = 8388608
$MaxEventRows = 5000

# Existing verified event-agent source; it is compiled only into a read-only
# parity exporter. No SyncEngine, spool, receiver, or checkpoint code is used.
# Resolve from the actual deployment drive. Examples:
#   H:\share\dcs_service -> H:\share\DcsAgent
#   Z:\dcs_service       -> Z:\DcsAgent
$LegacyEventSourceDir = Join-Path (Split-Path -Parent $ServiceRoot) "DcsAgent"
