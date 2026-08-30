using System;
using System.Collections.Generic;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class HistoryHandler : IApiHandler
    {
        private readonly HandlerContext _c; public HistoryHandler(HandlerContext c) { _c = c; }
        public object Handle(HttpRequest request)
        {
            Dictionary<string, object> body = JsonUtil.Object(request.Body); IList<string> tags = JsonUtil.Strings(body, "tags"); if (tags.Count == 0 || tags.Count > _c.Config.MaxTagsPerRequest) throw new ArgumentException("tags count must be between 1 and " + _c.Config.MaxTagsPerRequest + ".");
            DateTime start = JsonUtil.Date(body, "start"); DateTime end = JsonUtil.Date(body, "end"); if (end <= start) throw new ArgumentException("end must be after start."); if (end.Subtract(start).TotalHours > _c.Config.MaxHistorySpanHours) throw new ArgumentException("History span exceeds MaxHistorySpanHours.");
            int max = JsonUtil.Int(body, "maxSamples", _c.Config.MaxSamplesPerRead); if (max < 1 || max > _c.Config.MaxSamplesPerRead) throw new ArgumentException("maxSamples exceeds configured limit.");
            IDictionary<string, IList<DcsDataService.DeltaV.Historian.HistorySample>> samples = _c.Historian.ReadRaw(tags, start, end, max, _c.Config.MaxSamplesPerRequest); int count = 0; Dictionary<string, object> wire = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); foreach (KeyValuePair<string, IList<DcsDataService.DeltaV.Historian.HistorySample>> pair in samples) { List<object> rows = new List<object>(); for (int i = 0; i < pair.Value.Count; i++) { DcsDataService.DeltaV.Historian.HistorySample s = pair.Value[i]; DateTime sourceTimestamp = _c.Time.RawUtcToSource(s.Timestamp); rows.Add(new { tag = s.Tag, timestamp = sourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture), value = s.Value, dataType = s.DataType, deltaVStatus = s.DeltaVStatus, archiveStatus = s.ArchiveStatus, sequenceNo = s.SequenceNo, isHistoryHole = s.IsHistoryHole, isCRHole = s.IsCRHole, isManuallyDeleted = s.IsManuallyDeleted, isManuallyInserted = s.IsManuallyInserted }); } count += rows.Count; wire[pair.Key] = rows; } _c.Log.Info("History query tagCount=" + tags.Count + " sampleCount=" + count); return new { samples = wire, sampleCount = count, sourceTimeZone = _c.Config.SourceTimeZone, timestampSemantics = "source-local" };
        }
    }
}
