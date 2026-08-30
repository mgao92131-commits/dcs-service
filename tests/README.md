# Verification

`localhost-api-test.ps1` checks the HTTP health route, API key rejection, malformed JSON mapping, and request-size rejection without querying either DeltaV source.

Provider parity is a DCS-machine gate. Build `ParityVerifier.exe` with `build-parity-verifier.bat`. Use identical tag/start/end/max values with the retained `dcs_data` HistoryReader and `DcsDataService` history endpoint, save the legacy CSV and complete API response JSON, then run:

```bat
bin\ParityVerifier.exe history old.csv new-response.json "TAG"
```

It compares sample count and every timestamp/value/data type and reports first/last timestamps. Repeat with a range known to trigger `dataTruncated`; the new request must either return the complete normalized sequence or fail explicitly.

For events, use identical from/to/limit/after values with the retained `dcs_event` agent reader and the two event endpoints. Save the old agent `WireBatch` JSON and the new API response, then run:

```bat
bin\ParityVerifier.exe event old-batch.json new-response.json
```

It compares row count, every selected event field, and reports the first/last `(Date_Time, FracSec, Ord)` cursor. Record commands and outputs as deployment evidence. These checks cannot be truthfully completed away from the real DeltaV Historian and Event Journal.
