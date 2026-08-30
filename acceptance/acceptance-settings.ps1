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
# Public API request times are Beijing source-local values. The parity script
# converts them to raw UTC only for the legacy DeltaVReader baseline.
$EventStart = "2026-08-30 14:00:00"
$EventEnd = "2026-08-30 14:30:00"
$EventLimit = 50

$ApiBind = "127.0.0.1"
$ApiPort = 18080
$SourceTimeZone = "China Standard Time"

# Safety limits used during acceptance.
$MaxHistorySpanHours = 24
$MaxSamplesPerHistoryRequest = 50000
$MaxEventRows = 5000

# Existing verified event-agent source; it is compiled only into a read-only
# parity exporter. No SyncEngine, spool, receiver, or checkpoint code is used.
# Resolve from the actual deployment drive. Examples:
#   H:\share\dcs_service -> H:\share\DcsAgent
#   Z:\dcs_service       -> Z:\DcsAgent
$LegacyEventSourceDir = Join-Path (Split-Path -Parent $ServiceRoot) "DcsAgent"
