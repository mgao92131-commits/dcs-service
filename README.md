# dcs-service

`dcs-service` is a small, read-only local gateway for DeltaV Historian and Event Journal data. It targets Windows 7, .NET Framework 3.5 and x86, and always listens on `127.0.0.1`.

## Build and run

On a DeltaV machine, or with `DELTAV_DLL_DIR` pointing at compatible assemblies:

```bat
build-net35-x86.bat
bin\DcsDataService.exe probe --config config.ini
bin\DcsDataService.exe serve --config config.ini
```

## API

All request times and returned timestamps use the configured source-local timezone (`China Standard Time` by default). Percent-encode query parameter values.

- `GET /health` — liveness JSON.
- `GET /api/v1/info` — service metadata JSON.
- `GET /api/v1/tag?tag=...` — Historian tag diagnostic JSON.
- `GET /api/v1/history?tag=...&from=...&to=...` — complete History CSV for `[from,to)`.
- `GET /api/v1/events?from=...&to=...` — complete Event CSV for `[from,to)`.
- `GET /api/v1/events?afterTime=...&afterFracSec=...&afterOrd=...&sourceGeneration=...&to=...` — complete Event CSV after the checkpoint through the fixed `to` boundary.

Example:

```powershell
$tag = [Uri]::EscapeDataString("TI-021007_AI1_PV.CV")
Invoke-WebRequest "http://127.0.0.1:18080/api/v1/history?tag=$tag&from=2026-08-01T00%3A00%3A00&to=2026-09-01T00%3A00%3A00" -OutFile history.csv
Invoke-WebRequest "http://127.0.0.1:18080/api/v1/events?from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00" -OutFile events.csv
```

History CSV columns:

```text
Timestamp,Value,DataType,DeltaVStatus,ArchiveStatus,SequenceNo,IsHistoryHole,IsCRHole,IsManuallyDeleted,IsManuallyInserted
```

Event CSV columns:

```text
DateTime,FracSec,Ord,EventType,EventSubType,Category,Area,Node,Unit,Module,ModuleDescription,Attribute,State,EventLevel,Desc1,Desc2,IsArchived
```

CSV is UTF-8 without BOM, RFC-style escaped, and uses invariant number formatting. Data responses use HTTP/1.1 chunked transfer encoding. A successful response ends with the terminating chunk; a provider or socket failure after streaming starts closes the connection without that terminator, so the client must discard the partial file and retry the complete request.

History keeps one Historian connection for the request, splits the requested range into `HistorianStreamWindowMinutes` windows, and recursively AutoSplits any truncated `readRaw` window. Only the current normalized segment and the previous emitted sample are retained. Event keeps one SQL connection, splits the requested range into `EventStreamWindowMinutes` half-open windows, executes one command/reader per window, and writes rows directly to the CSV stream.

The response exposes `X-DCS-Tag`, `X-DCS-Source-TimeZone`, `X-DCS-From`, and `X-DCS-To` for History. Event responses expose `X-DCS-Source-TimeZone`, `X-DCS-Source-Generation`, and `X-DCS-To`. Row-count and pagination headers are intentionally absent; clients can count CSV rows locally. Event cursor fields are present in every CSV row and the final row can be stored as the next synchronization checkpoint.

## Concurrency and timeouts

History and Event requests use separate bounded gates. The defaults are two Historian connections and four SQL connections. The global `RequestQueueLimit` bounds active plus queued HTTP work. A request holds its provider slot for the whole download. `ProviderSlotWaitSeconds` bounds slot acquisition, while `SocketReadSeconds` and `SocketWriteSeconds` protect request reads and slow clients; there is no fixed whole-download deadline.

## Configuration

```ini
[Historian]
Server=APP
ConnectionTimeoutSeconds=30
TestTag=TI-xxxx
ReadChunkSamples=10000
StreamWindowMinutes=60

[Events]
Server=EVENTJOURNAL
Database=EventJournal
Schema=dbo
Table=Journal
CommandTimeoutSeconds=60
RuntimeStateCacheSeconds=30
StreamWindowMinutes=60

[Api]
Port=18080

[Concurrency]
HistoryMaxConcurrent=2
EventMaxConcurrent=4
RequestQueueLimit=32

[Timeout]
ProviderSlotWaitSeconds=60
SocketReadSeconds=60
SocketWriteSeconds=120

[Time]
SourceTimeZone=China Standard Time

[Files]
Logs=logs
```

`ReadChunkSamples` is only the maximum number supplied to one DeltaV `readRaw` call. `Historian.StreamWindowMinutes` and `Events.StreamWindowMinutes` are independent internal performance settings, not API data-size restrictions. `/api/v1/info` reports them as `historyStreamWindowMinutes` and `eventStreamWindowMinutes`. Event source safety checks (`IsFull`, `EJOverflow`, generation changes, retention gaps and cursor ordering) remain fail-closed.

## Verification

```bat
tests\run-core-tests.bat
powershell -NoProfile -ExecutionPolicy Bypass -File tests\localhost-api-test.ps1
```

The acceptance workflow under `acceptance` compares complete CSV ranges with the verified legacy readers on a real DCS machine, including a History AutoSplit range and an Event checkpoint range.
