# dcs_service

`dcs_service` is a small, read-only gateway for DeltaV Historian raw samples and DeltaV Event Journal rows. It targets C#/.NET Framework 3.5, x86, and Windows 7. The architecture is deliberately `DeltaV private API -> Provider -> domain model -> HTTP`; HTTP never invokes a CLI or parses CSV. Code implementation is complete; DCS-machine build, probe, and parity acceptance remain deployment gates.

## Build

Install .NET Framework 3.5 and build from a 32-bit-compatible DeltaV machine:

```bat
set DELTAV_DLL_DIR=C:\DeltaV\bin
build-net35-x86.bat
```

The build emits `bin\DcsDataService.exe`. It references the installed DeltaV assemblies and stages the exact same-machine Historian runtime dependency closure beside the executable because target DCS installations do not necessarily register these private assemblies in the GAC. The staged DLLs remain ignored deployment artifacts and are never committed. Never stage DLLs from another DeltaV release. The local sibling `..\dcs_data\hda` is only a development fallback for compilation.

## Configure and run

Copy `config.example.ini` to the ignored `config.ini`, replace the test tag, Event Journal server/database, and API key, then run:

```bat
bin\DcsDataService.exe --version
bin\DcsDataService.exe probe --config config.ini
bin\DcsDataService.exe serve --config config.ini
```

`probe` is fail-closed and returns a nonzero exit code unless all of these work: strong-typed DvCH initialization, read connection via `connection(connectionId)`, server state, test-tag resolution, a five-minute raw read, Event Journal probe, safe `IsFull`/`EJOverflow` state, and earliest/latest three-field cursors. It never falls back to the scanner/capture `getConnection()` API.

Times are accepted and returned as source-local `DateTime` values. They are not silently converted to UTC or suffixed with `Z`; responses include `sourceTimeZone` from configuration.

## HTTP API

The server is a bounded `TcpListener` HTTP/1.1 implementation. V1 only supports `GET`, `POST`, `Content-Length`, JSON bodies, one request per connection, a 32 KiB header limit, configured body and query limits, socket timeouts, and `Connection: close`. V1 refuses non-loopback bind addresses.

Every endpoint requires `X-DCS-API-Key`. `/health` is intentionally a process liveness check; it does not claim that Historian and Event Journal are ready:

```text
GET  /health
GET  /api/v1/info
POST /api/v1/tags/resolve
POST /api/v1/history/query
POST /api/v1/events/query
POST /api/v1/events/after
```

Examples:

```powershell
$headers = @{ "X-DCS-API-Key" = "replace-me" }
Invoke-RestMethod http://127.0.0.1:18080/health -Headers $headers

Invoke-RestMethod http://127.0.0.1:18080/api/v1/tags/resolve -Method Post -Headers $headers -ContentType application/json -Body '{"tags":["TI-100/PV.CV"]}'

Invoke-RestMethod http://127.0.0.1:18080/api/v1/history/query -Method Post -Headers $headers -ContentType application/json -Body '{"tags":["TI-100/PV.CV"],"start":"2026-08-30 08:00:00","end":"2026-08-30 09:00:00","maxSamples":10000}'

Invoke-RestMethod http://127.0.0.1:18080/api/v1/events/query -Method Post -Headers $headers -ContentType application/json -Body '{"from":"2026-08-30 08:00:00","to":"2026-08-30 09:00:00","limit":500}'

Invoke-RestMethod http://127.0.0.1:18080/api/v1/events/after -Method Post -Headers $headers -ContentType application/json -Body '{"sourceGeneration":"APP|2026-08-30T00:00:00.000","after":{"dateTime":"2026-08-30 08:10:00.123","fracSec":123,"ord":42},"limit":500}'
```

Event responses include `sourceGeneration`, `nextCursor`, `hasMore`, `earliestCursor`, and `latestCursor`. Clients must persist generation with the three-field cursor and return both on subsequent `after` requests. Expired/ahead cursors and generation changes return HTTP 409 instead of silently skipping data.

Responses use `{ "ok": true, "data": ... }` or `{ "ok": false, "error": { "code": ..., "message": ... } }`. Invalid arguments map to 400, API-key failures to 401, cursor/source conflicts to 409, oversized requests, responses, or History results to 413, unavailable/unsafe providers to 503, and unexpected failures to 500. Stack traces are written only to `logs/service_yyyyMMdd.log`.

## Provider behavior and safety

Historian calls are serialized by a provider gate. `DvCHDataAccess.Initialize()` runs once per process. A multi-tag request opens one strong-typed `IDvCHDataAccess.connection()` and reuses it for every tag, then closes it in `finally`; each raw segment creates/releases its own time span. Truncated reads split recursively at the time midpoint (maximum depth 20, minimum slice two seconds), merge, timestamp-sort, and de-duplicate. A still-truncated leaf throws instead of returning incomplete data. `MaxSamplesPerRequest` is accumulated across all completed AutoSplit leaves and all tags, so excessive queries stop before further Historian reads. `MaxResponseBytes` is an independent final serialization bound.

Tag resolution names `HistoryTagOK`, `HistoryTagUnknown`, and `HistoryTagAmbiguous` explicitly and keeps a process-local handle/metadata cache. `getTagTypeInfo()` receives resolved server handles; metadata failures are logged rather than silently swallowed. `Value` remains an object in the domain model so numeric samples are not prematurely converted to strings.

Event queries retain the verified `System.Data.SqlClient`, integrated-security, `READ UNCOMMITTED` semantics from `dcs_event`. Incremental ordering and predicates always use all three fields: `Date_Time`, `FracSec`, and `Ord`. Before reads, a lightweight runtime state check validates source generation, `IsFull`, and `EJOverflow` with a configurable short cache. Cursor reads validate earliest/latest before the query and earliest again afterward to detect retention advancing during a read. No sync engine, spool, receiver, checkpoint writer, or database mutation is included.

## Verification

Run the offline localhost checks with:

```powershell
tests\run-core-tests.bat
powershell -ExecutionPolicy Bypass -File tests\localhost-api-test.ps1
```

The real-data parity procedure is documented in `tests\README.md`. It is a mandatory deployment gate because Historian truncation behavior, tag ambiguity, Event Journal generation/overflow state, integrated Windows identity, and DeltaV assembly binding can only be verified on the target DCS computer. Keep `dcs_data` and `dcs_event` intact as the known-good comparison tools.

For the staged DCS deployment, a real-machine acceptance runner is provided in `acceptance`. Edit `acceptance-settings.ps1`, then run the numbered `RUN-00` through `RUN-06` batch files in order. Each gate writes build/probe output and old/new CSV or JSON parity evidence under `evidence\run_yyyyMMdd_HHmmss`. The deployed package also contains ignored, deployment-only legacy HistoryReader artifacts built from `dcs_data` commit `18874cf8da52abe4cc893169b2110d37837847a1`; their SHA256 values are checked before use. Event parity resolves the existing sibling `DcsAgent\DeltaV\DeltaVReader.cs` relative to the actual deployment drive (for example `Z:\DcsAgent`) and compiles it into a read-only exporter without sync, spool, receiver, or checkpoint components.

## Deployment cautions

- Keep the default loopback bind and use an authenticated tunnel or tightly controlled local forward for later remote access.
- Replace `CHANGE_ME`; `config.ini` is ignored and must be protected with Windows ACLs.
- Run with a least-privileged Windows identity that has read access only.
- Do not install firewall rules, expose port 18080 directly, or copy DeltaV DLLs from another product revision.
- Logs contain operation metadata and exceptions, never individual sample or event payloads.
