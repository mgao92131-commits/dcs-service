# Verification

`localhost-api-test.ps1` checks unauthenticated liveness, the route surface, range/cursor exclusivity, removed pagination-parameter rejection, unavailable-provider mappings, and request-size rejection without requiring a working DeltaV source.

`CoreTests.cs` covers CSV escaping and invariant formatting, chunk framing and terminating-chunk failure semantics, safe download names, fixed loopback binding, bounded concurrency, Event overflow/full fail-closed behavior, retention/generation/cursor-boundary rejection, independent History/Event window configuration, contiguous time-window splitting, and the distinction between range SQL and TOP 1 safety lookups.

Build and run the offline checks with:

```bat
tests\run-core-tests.bat
tests\build-parity-verifier.bat
powershell -NoProfile -ExecutionPolicy Bypass -File tests\localhost-api-test.ps1
```

Provider parity remains a real DCS-machine gate. Compare an old HistoryReader CSV with the complete History CSV:

```bat
test-tools\ParityVerifier.exe history old.csv new-response.csv "China Standard Time"
```

Compare an old Event exporter JSON range with the complete Event CSV:

```bat
test-tools\ParityVerifier.exe event old-batch.json new-response.csv "China Standard Time"
```

The verifier compares row counts, timestamps after source-time conversion, values/data types or every Event field, and reports first/last edges. The scripts under `acceptance` capture these artifacts and cannot be truthfully completed away from the real DeltaV Historian and Event Journal.

## Realtime verification

After building, run `tests\run-realtime-tests.bat`. An in-process fake runtime checks single/batch values, partial/all failures, Unicode/precision, one-time initialization, consistent thread ownership, concurrent serialization, queue timeout without cancellation, initialization failure caching, disposal during an active read, input validation and actual HTTP routes. No fixture worker or child-process protocol remains.

Field tools are built into `test-tools` by `acceptance\realtime\build-field-tests.bat`. Production bin contains only the service EXE. Field acceptance must recheck native cleanup and process memory after the refactor.
