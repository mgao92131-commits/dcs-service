using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class HistoryHandler : IApiHandler
    {
        private readonly HandlerContext _c;
        public HistoryHandler(HandlerContext c) { _c = c; }

        public HttpResponse Handle(HttpRequest request)
        {
            IDictionary<string, string> query = QueryStringParser.Parse(request.QueryString);
            string tag = QueryStringParser.Required(query, "tag"); DateTime from = QueryStringParser.RequiredDate(query, "from"); DateTime to = QueryStringParser.RequiredDate(query, "to");
            if (to <= from) throw new ArgumentException("to must be after from.");

            IDisposable gate = null; HistorianProvider.HistorianStream prepared = null;
            try
            {
                gate = _c.HistoryGate.Enter(_c.Config.ProviderSlotWaitSeconds * 1000);
                prepared = _c.Historian.PrepareRawStream(tag, from, to, _c.Config.HistorianReadChunkSamples, TimeSpan.FromMinutes(_c.Config.HistorianStreamWindowMinutes));
                _c.Log.Info("History stream start tag=" + tag + " from=" + FormatDate(from) + " to=" + FormatDate(to));

                HistorianProvider.HistorianStream streamForResponse = prepared;
                HttpResponse response = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8", IsChunked = true };
                response.Headers["X-DCS-Tag"] = tag;
                response.Headers["X-DCS-Source-TimeZone"] = _c.Config.SourceTimeZone;
                response.Headers["X-DCS-From"] = FormatDate(from);
                response.Headers["X-DCS-To"] = FormatDate(to);
                response.Headers["Content-Disposition"] = "attachment; filename=\"" + DownloadFileName.History(tag, from, to) + "\"";
                response.BodyResource = new DisposablePair(prepared, gate);
                prepared = null; gate = null;
                response.BodyWriter = delegate(Stream stream)
                {
                    Stopwatch clock = Stopwatch.StartNew(); long totalSamples = 0;
                    StreamWriter text = new StreamWriter(stream, new UTF8Encoding(false)); CsvWriter csv = new CsvWriter(text);
                    csv.WriteRow("Timestamp", "Value", "DataType", "DeltaVStatus", "ArchiveStatus", "SequenceNo", "IsHistoryHole", "IsCRHole", "IsManuallyDeleted", "IsManuallyInserted"); text.Flush();
                    try
                    {
                        streamForResponse.Stream(delegate(IList<HistorySample> batch)
                        {
                            for (int i = 0; i < batch.Count; i++)
                            {
                                HistorySample sample = batch[i];
                                csv.WriteRow(FormatDate(_c.Time.RawUtcToSource(sample.Timestamp)), sample.Value, sample.DataType, sample.DeltaVStatus, sample.ArchiveStatus, sample.SequenceNo, sample.IsHistoryHole, sample.IsCRHole, sample.IsManuallyDeleted, sample.IsManuallyInserted);
                            }
                            totalSamples += batch.Count; text.Flush();
                        });
                        text.Flush(); clock.Stop();
                        _c.Log.Info("History stream complete tag=" + tag + " durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " totalSamples=" + totalSamples.ToString(CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex)
                    {
                        clock.Stop(); _c.Log.Error("History stream aborted tag=" + tag + " durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " totalSamples=" + totalSamples.ToString(CultureInfo.InvariantCulture), ex); throw;
                    }
                };
                return response;
            }
            catch
            {
                if (prepared != null) prepared.Dispose();
                if (gate != null) gate.Dispose();
                throw;
            }
        }

        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture); }
    }
}
