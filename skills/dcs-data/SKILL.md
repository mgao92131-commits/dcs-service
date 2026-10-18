---
name: dcs-data
description: Read DeltaV DCS native realtime parameters, Historian process values and Event Journal events through the local dcs-service CLI for tag checks, time-range queries, trends, statistics, and anomaly analysis.
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
python scripts/dcs.py history "PICA-217024" --last 2h
python scripts/dcs.py history "PICA-217024" --role setpoint --last 2h
python scripts/dcs.py history "PICA-217024" --role output --last 2h
python scripts/dcs.py events --from "2026-09-04 08:00" --to "2026-09-04 16:00"
```

`--config PATH` and `--base-url URL` are global options and must appear before the command. They are useful for testing or an explicitly selected service; normally use the checked-in `config.json`.

History accepts either a full Historian TAG or a module name. A module name defaults to the actual value; use `--role setpoint` for a setpoint or `--role output` for a control output. The CLI probes the small, ordered set of role-specific candidate TAGs through `/tag`, stops at the first valid candidate, and then reads history. Do not construct the candidate TAG yourself. A full TAG containing `/` keeps the direct, exact-TAG behavior and does not require module resolution.

For natural-language requests, map “查询模块”、 “实际值”、 “PV” or “当前值” to the default `actual` role; map “设定值”、 “SP” or “目标值” to `setpoint`; and map “输出”、 “OUT” or “控制输出” to `output`. When the user only names a module, query its actual value. Keep the role choice internal to the Agent and pass the canonical CLI value.

The current candidate paths are intentionally generic: actual value tries `PID1/PV.CV`, `AI1/PV.CV`, `DI1/PV_D.CV`, and `DC1/PV_D.CV`; setpoint tries `PID1/SP.CV`; output tries `PID1/OUT.CV`. The service's `/tag` response is authoritative. Do not add module-prefix rules or scan Event Journal for an ordinary module query.

History and Event queries accept either `--from` plus `--to`, or `--last` with a duration such as `1h`, `8h`, or `3d`. Times are source-local China Standard Time by default, have no `Z` or UTC offset, and use the half-open range `[from, to)`. The host clock must agree with the configured DCS source-local time when using `--last`.

## Native realtime reads

Use `realtime "SY-218/AI1/PV.CV"` for one full native parameter path, or
`realtime-batch "SY-218/AI1/PV.CV" "OTHER/AI1/PV.CV"` for a bounded batch.
These commands use the service's in-process native reader, do not use OPC, and do not probe
Historian `/tag`. They require the realtime API to be enabled and deployed.
Do not guess AI1/PID1 or other block names when the actual native path is unknown.
Historian role resolution applies only to `history`; it is not realtime discovery.

Results retain typed `value`, formatted `valueText`, native type, and per-point
`observedAt` in `sourceTimeZone`. `observedAt` is the read observation time, not a
source timestamp. Unavailable `sourceTimestamp` and `quality` remain null.
Batch sampling is sequential, not an atomic snapshot. Order and duplicates are
preserved. A partial batch prints all item results and exits nonzero; do not
silently discard failures. Request-level `realtime_disabled`, `realtime_abi_mismatch`,
`realtime_read_failed`, and `realtime_unavailable` must not be mistaken for a missing Historian tag.

## Output

`health`, `info`, and `tag` print compact JSON. A successful `tag` response has `status: "HistoryTagOK"`; do not use a tag with another status for history analysis.

History/Event commands save a complete CSV by default under `.work/dcs/` in the current working directory and print only a JSON summary:

```json
{"ok":true,"type":"history","tag":"PICA-217024/PID1/PV.CV","module":"PICA-217024","role":"actual","from":"2026-09-04T08:00:00","to":"2026-09-04T16:00:00","row_count":18342,"file":".work\\dcs\\history_PICA-217024_PID1_PV.CV_20260904_080000_20260904_160000.csv"}
```

Use `--output PATH` when the analysis workflow needs a known file. The CLI downloads to `PATH.part`, validates the CSV, and atomically replaces `PATH` only after the complete HTTP response has been received. Never analyze a leftover `.part` file.

For a genuinely small query, add `--json` to History/Event. It returns `row_count` and a `data` array with normalized snake_case keys. JSON mode is capped at 5,000 rows by default; use `--max-rows` only when the result is known to fit. For larger ranges, keep the file and analyze it with Python/Pandas or another local tool instead of sending the CSV into the agent context.

## Errors

The CLI prints one JSON error object and exits nonzero. Handle these stable codes:

- `tag_not_found`: no candidate TAG exists for the module and role; verify the module name or use a full TAG.
- `tag_ambiguous`: the service reports that a candidate TAG is ambiguous; do not guess, use a full TAG.
- `dcs_busy`: wait briefly and retry the complete request.
- `dcs_unavailable`: check `health`/`info` and the DCS Historian or Event Journal.
- `incomplete_download`: discard the failed result and retry the entire same range; do not use the partial file.
- `invalid_request` or `invalid_response`: correct the query or report the service response problem rather than guessing.

Do not add `limit`, pagination parameters, Event cursor parameters, UTC markers, or timezone offsets to ordinary queries.
