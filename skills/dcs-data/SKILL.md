---
name: dcs-data
description: Read DeltaV DCS Historian process values and Event Journal events through the local dcs-service CLI for tag checks, time-range queries, trends, statistics, and anomaly analysis.
metadata:
  short-description: Query DCS history and events
---

# DCS Data

Use this skill when a user needs read-only DeltaV DCS data: a Historian tag diagnostic, historical process values, or Event Journal events for a bounded time range.

## Data source

The CLI in `scripts/dcs.py` is the only interface to the data service. Do not call DeltaV DLLs, Historian databases, Event Journal SQL, FRP, or the service HTTP endpoints directly. The default endpoint is in `config.json`; do not replace it with the DCS computer's loopback address.

All operations are read-only. Event cursor fields are intentionally not exposed by this skill; cursor checkpoints belong to a synchronizer, not an ordinary analysis query.

## Commands

Run these from the skill directory, or use an absolute path to `scripts/dcs.py`:

```text
python scripts/dcs.py health
python scripts/dcs.py info
python scripts/dcs.py tag "TI-021007_AI1_PV.CV"
python scripts/dcs.py history "TI-021007_AI1_PV.CV" --from "2026-09-04 08:00" --to "2026-09-04 16:00"
python scripts/dcs.py events --from "2026-09-04 08:00" --to "2026-09-04 16:00"
```

`--config PATH` and `--base-url URL` are global options and must appear before the command. They are useful for testing or an explicitly selected service; normally use the checked-in `config.json`.

History and Event queries accept either `--from` plus `--to`, or `--last` with a duration such as `1h`, `8h`, or `3d`. Times are source-local China Standard Time by default, have no `Z` or UTC offset, and use the half-open range `[from, to)`. The host clock must agree with the configured DCS source-local time when using `--last`.

## Output

`health`, `info`, and `tag` print compact JSON. A successful `tag` response has `status: "HistoryTagOK"`; do not use a tag with another status for history analysis.

History/Event commands save a complete CSV by default under `.work/dcs/` in the current working directory and print only a JSON summary:

```json
{"ok":true,"type":"history","tag":"TI-021007_AI1_PV.CV","from":"2026-09-04T08:00:00","to":"2026-09-04T16:00:00","row_count":18342,"file":".work\\dcs\\history_TI-021007_AI1_PV.CV_20260904_080000_20260904_160000.csv"}
```

Use `--output PATH` when the analysis workflow needs a known file. The CLI downloads to `PATH.part`, validates the CSV, and atomically replaces `PATH` only after the complete HTTP response has been received. Never analyze a leftover `.part` file.

For a genuinely small query, add `--json` to History/Event. It returns `row_count` and a `data` array with normalized snake_case keys. JSON mode is capped at 5,000 rows by default; use `--max-rows` only when the result is known to fit. For larger ranges, keep the file and analyze it with Python/Pandas or another local tool instead of sending the CSV into the agent context.

## Errors

The CLI prints one JSON error object and exits nonzero. Handle these stable codes:

- `tag_not_found` or `tag_ambiguous`: choose or verify the TAG.
- `dcs_busy`: wait briefly and retry the complete request.
- `dcs_unavailable`: check `health`/`info` and the DCS Historian or Event Journal.
- `incomplete_download`: discard the failed result and retry the entire same range; do not use the partial file.
- `invalid_request` or `invalid_response`: correct the query or report the service response problem rather than guessing.

Do not add `limit`, pagination parameters, Event cursor parameters, UTC markers, or timezone offsets to ordinary queries.
