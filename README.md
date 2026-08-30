# dcs-service V1

`dcs-service` is a small, read-only local gateway for DeltaV Historian and Event Journal data. It targets Windows 7, .NET Framework 3.5 and x86. The process always listens on `127.0.0.1`; remote access belongs behind an externally managed reverse tunnel.

The service has no API key or other application authentication. Do not expose port 18080 on a LAN interface.

## Build and run

On a DeltaV machine, or with `DELTAV_DLL_DIR` pointing at compatible assemblies:

```bat
build-net35-x86.bat
bin\DcsDataService.exe probe --config config.ini
bin\DcsDataService.exe serve --config config.ini
```

The listener address is not configurable. Only `[Api] Port` is read.

## V1 endpoints

- `GET /health` — liveness JSON only: `{"status":"ok"}`.
- `GET /api/v1/info` — small JSON service metadata.
- `GET /api/v1/tag?tag=...` — JSON tag diagnostic.
- `GET /api/v1/history?tag=...&from=...&to=...` — one tag as CSV.
- `GET /api/v1/events?from=...&to=...&limit=...` — Event range as CSV.
- `GET /api/v1/events?afterTime=...&afterFracSec=...&afterOrd=...&sourceGeneration=...&limit=...` — cursor page as CSV.

All request times and returned timestamps use the configured source-local timezone (`China Standard Time` by default). Percent-encode query parameter values. Range and cursor Event parameters are mutually exclusive.

Example:

```powershell
$tag = [Uri]::EscapeDataString("TI-021007_AI1_PV.CV")
Invoke-WebRequest "http://127.0.0.1:18080/api/v1/history?tag=$tag&from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00" -OutFile history.csv
Invoke-WebRequest "http://127.0.0.1:18080/api/v1/events?from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00&limit=1000" -OutFile events.csv
```

History uses this fixed column order:

```text
Timestamp,Value,DataType,DeltaVStatus,ArchiveStatus,SequenceNo,IsHistoryHole,IsCRHole,IsManuallyDeleted,IsManuallyInserted
```

Event uses this fixed column order:

```text
DateTime,FracSec,Ord,EventType,EventSubType,Category,Area,Node,Unit,Module,ModuleDescription,Attribute,State,EventLevel,Desc1,Desc2,IsArchived
```

CSV is UTF-8 without BOM, RFC-style escaped, and uses invariant number formatting. It is written row by row; large data does not pass through `JavaScriptSerializer`.

History response metadata is carried in `X-DCS-Tag`, `X-DCS-Row-Count`, `X-DCS-Source-TimeZone`, `X-DCS-From`, and `X-DCS-To`. Every Event response carries row count, source timezone, `X-DCS-Source-Generation`, `X-DCS-Has-More`, and—when a row was returned—`X-DCS-Next-DateTime`, `X-DCS-Next-FracSec`, and `X-DCS-Next-Ord`.

Event `limit` is the maximum number of rows per page, not a statement that the complete requested range contains at most that many rows. A range request establishes the first cursor; when `X-DCS-Has-More: true`, continue with the returned `X-DCS-Next-*` cursor and source generation. Cursor mode continues forward through the Journal rather than retaining the original range's `to`, so a client collecting an exact closed range must stop when it reaches that original boundary.

Store the Event cursor together with `X-DCS-Source-Generation`, then send that generation with the next cursor request. A generation mismatch, an expired cursor, `JournalProperties.IsFull`, or any unverifiable/non-empty `EJOverflow` state fails closed with JSON instead of returning incomplete CSV.

## Concurrency and limits

History and Event requests use separate bounded gates. The defaults are two Historian connections and four SQL queries. Every admitted History request creates, exclusively uses, and closes its own `DvCHReadConnection`; connections are never shared between threads. `DvCHDataAccess.Initialize()` runs once per process.

The global `RequestQueueLimit` bounds active plus queued HTTP work. Excess connections receive HTTP 429 with `service_busy`. Provider-slot waits are bounded by `RequestTimeoutSeconds`.

Historian AutoSplit remains enabled. `ReadChunkSamples` limits each DLL `readRaw`, while `MaxSamplesPerHistoryRequest` limits the accumulated HTTP result and returns HTTP 413 before more segments are read.

```ini
[Historian]
Server=APP
ConnectionTimeoutSeconds=30
TestTag=TI-xxxx
ReadChunkSamples=10000

[Events]
Server=EVENTJOURNAL
Database=EventJournal
Schema=dbo
Table=Journal
CommandTimeoutSeconds=30
RuntimeStateCacheSeconds=30

[Api]
Port=18080

[Concurrency]
HistoryMaxConcurrent=2
EventMaxConcurrent=4
RequestQueueLimit=32

[ApiLimits]
MaxHistorySpanHours=24
MaxSamplesPerHistoryRequest=50000
MaxEventRows=5000
RequestTimeoutSeconds=60

[Time]
SourceTimeZone=China Standard Time

[Files]
Logs=logs
```

## Verification

```bat
tests\run-core-tests.bat
powershell -NoProfile -ExecutionPolicy Bypass -File tests\localhost-api-test.ps1
```

Core tests cover CSV quoting, null/Unicode/invariant formatting, cursor safety, Historian budgeting, fixed loopback binding and concurrency gating. The `acceptance` workflow and `ParityVerifier` remain the DCS-side parity checks; run History at concurrency 1 first, then the default 2. Test concurrency 3 only as a measured comparison.
