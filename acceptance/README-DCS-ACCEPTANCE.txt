DCS SERVICE REAL-MACHINE ACCEPTANCE
===================================

Deployment directory: H:\share\dcs_service

This package performs read-only builds and reads. It does not write Historian,
Event Journal, spool, receiver, or checkpoint data.

Before running, edit acceptance\acceptance-settings.ps1. Set the real History
tag and normal/AutoSplit intervals, Event server/database and interval,
and DeltaV DLL directory. Use source-local DeltaV times; do not append Z.
All public API request and response times are Beijing source-local values. The
parity runner converts those values to raw UTC only when invoking the legacy
HistoryReader/DeltaVReader baseline. Choose a completed Event window with at
least two rows so the After check can use the first CSV row as its checkpoint.

Run in this exact order:

  RUN-00-PREPARE.bat
  RUN-01-BUILD-PROBE.bat
  RUN-02-START-SERVICE.bat
  RUN-03-HISTORY-NORMAL.bat
  RUN-04-HISTORY-AUTOSPLIT.bat
  RUN-05-EVENT-PARITY.bat
  RUN-06-STOP-SERVICE.bat

Stop when a step fails; do not skip a failed gate. AutoSplit is accepted only
if the new service returns more rows than the configured per-read maximum and
every row matches legacy HistoryReader output. If AUTOSPLIT NOT PROVEN appears,
extend the interval or choose a denser tag so the configured ReadChunkSamples
value is exceeded. The service does not impose a total History sample or span
limit.

Event parity compiles DeltaVReader.cs from the sibling DcsAgent directory on
the actual deployment drive (for example Z:\DcsAgent) into a
dedicated read-only exporter. It never compiles or invokes SyncEngine, spool,
receiver, or checkpoint code.

The generated config uses independent Historian and Event window sizes. The
default acceptance settings use a five-minute Event window over a 30-minute
range, so RUN-05-EVENT-PARITY crosses multiple SQL command/reader windows
while still making one HTTP GET. The service log records each completed Event
window and the final window count.

The build stages the required DeltaV Historian DLL dependency closure from the
configured DeltaVDllDir into bin beside DcsDataService.exe. These are ignored
deployment artifacts from this DCS machine, not repository files. Never point
DeltaVDllDir at DLLs copied from another DeltaV release.

Every run creates H:\share\dcs_service\evidence\run_yyyyMMdd_HHmmss. Keep the
complete directory. The service remains bound to 127.0.0.1. Do not intentionally
fill Journal/EJOverflow, and never mix DeltaV DLLs from another product release.

