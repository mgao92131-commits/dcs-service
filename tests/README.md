# Verification

`localhost-api-test.ps1` checks liveness, API key rejection, route/query bounds, required Event generation, unavailable-provider mappings, malformed JSON, timeout, and request-size rejection without requiring a working DeltaV source. `CoreTests.cs` additionally covers Event overflow/full fail-closed behavior, retention/generation cursor rejection, History aggregate sample budget, and UTF-8 response-size limits.

Provider parity is a DCS-machine gate. Build `ParityVerifier.exe` with `build-parity-verifier.bat`. Use identical tag/start/end/max values with the retained `dcs_data` HistoryReader and `DcsDataService` history endpoint, save the legacy CSV and complete API response JSON, then run:

```bat
bin\ParityVerifier.exe history old.csv new-response.json "TAG"
```

It compares sample count, every value/data type, and every timestamp after converting the legacy raw UTC timestamp to the configured API source timezone. It reports first/last Beijing timestamps. Repeat with a range known to trigger `dataTruncated`; the new request must either return the complete normalized sequence or fail explicitly.

For events, use identical from/to/limit/after values with the retained `dcs_event` agent reader and the two event endpoints. Save the old agent `WireBatch` JSON and the new API response, then run:

```bat
bin\ParityVerifier.exe event old-batch.json new-response.json
```

It compares row count, every selected event field, and reports the first/last `(Date_Time, FracSec, Ord)` cursor. Record commands and outputs as deployment evidence. These checks cannot be truthfully completed away from the real DeltaV Historian and Event Journal.
