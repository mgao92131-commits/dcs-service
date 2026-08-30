using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class HistoryHandler : IApiHandler
    {
        private readonly HandlerContext _c; public HistoryHandler(HandlerContext c) { _c = c; }
        public HttpResponse Handle(HttpRequest request)
        {
            IDictionary<string, string> query = QueryStringParser.Parse(request.QueryString);
            string tag = QueryStringParser.Required(query, "tag"); DateTime from = QueryStringParser.RequiredDate(query, "from"); DateTime to = QueryStringParser.RequiredDate(query, "to");
            if (to <= from) throw new ArgumentException("to must be after from.");
            if (to.Subtract(from).TotalHours > _c.Config.MaxHistorySpanHours) throw new HistoryQueryTooLargeException("History span exceeds MaxHistorySpanHours=" + _c.Config.MaxHistorySpanHours.ToString(CultureInfo.InvariantCulture) + ".");
            IList<HistorySample> rows;
            using (_c.HistoryGate.Enter(_c.Config.RequestTimeoutSeconds * 1000)) rows = _c.Historian.ReadRaw(new List<string> { tag }, from, to, _c.Config.HistorianReadChunkSamples, _c.Config.MaxSamplesPerHistoryRequest)[tag];
            _c.Log.Info("History query tag=" + tag + " sampleCount=" + rows.Count.ToString(CultureInfo.InvariantCulture));
            HttpResponse response = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8" };
            response.Headers["X-DCS-Tag"] = tag; response.Headers["X-DCS-Row-Count"] = rows.Count.ToString(CultureInfo.InvariantCulture); response.Headers["X-DCS-Source-TimeZone"] = _c.Config.SourceTimeZone; response.Headers["X-DCS-From"] = FormatDate(from); response.Headers["X-DCS-To"] = FormatDate(to);
            response.BodyWriter = delegate(Stream stream)
            {
                StreamWriter text = new StreamWriter(stream, new UTF8Encoding(false)); CsvWriter csv = new CsvWriter(text);
                csv.WriteRow("Timestamp", "Value", "DataType", "DeltaVStatus", "ArchiveStatus", "SequenceNo", "IsHistoryHole", "IsCRHole", "IsManuallyDeleted", "IsManuallyInserted");
                for (int i = 0; i < rows.Count; i++) { HistorySample s = rows[i]; csv.WriteRow(FormatDate(_c.Time.RawUtcToSource(s.Timestamp)), s.Value, s.DataType, s.DeltaVStatus, s.ArchiveStatus, s.SequenceNo, s.IsHistoryHole, s.IsCRHole, s.IsManuallyDeleted, s.IsManuallyInserted); }
                text.Flush();
            };
            return response;
        }
        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture); }
    }
}
