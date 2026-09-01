# Verification

`localhost-api-test.ps1` checks unauthenticated liveness, the route surface, range/cursor exclusivity, removed pagination-parameter rejection, unavailable-provider mappings, and request-size rejection without requiring a working DeltaV source.

`CoreTests.cs` covers CSV escaping and invariant formatting, chunk framing and terminating-chunk failure semantics, safe download names, fixed loopback binding, bounded concurrency, Event overflow/full fail-closed behavior, retention/generation cursor rejection, and streaming configuration defaults.

Build and run the offline checks with:

```bat
tests\run-core-tests.bat
tests\build-parity-verifier.bat
powershell -NoProfile -ExecutionPolicy Bypass -File tests\localhost-api-test.ps1
```

Provider parity remains a real DCS-machine gate. Compare an old HistoryReader CSV with the complete History CSV:

```bat
bin\ParityVerifier.exe history old.csv new-response.csv "China Standard Time"
```

Compare an old Event exporter JSON range with the complete Event CSV:

```bat
bin\ParityVerifier.exe event old-batch.json new-response.csv "China Standard Time"
```

The verifier compares row counts, timestamps after source-time conversion, values/data types or every Event field, and reports first/last edges. The scripts under `acceptance` capture these artifacts and cannot be truthfully completed away from the real DeltaV Historian and Event Journal.
