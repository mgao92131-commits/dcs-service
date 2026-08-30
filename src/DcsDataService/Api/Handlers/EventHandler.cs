using System;
using System.Collections.Generic;
using DcsDataService.DeltaV.Events;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class EventHandler : IApiHandler
    {
        private readonly HandlerContext _c; private readonly bool _afterOnly; public EventHandler(HandlerContext c, bool afterOnly) { _c = c; _afterOnly = afterOnly; }
        public object Handle(HttpRequest request)
        {
            Dictionary<string, object> body = JsonUtil.Object(request.Body); int limit = JsonUtil.Int(body, "limit", _c.Config.MaxEventRows); if (limit < 1 || limit > _c.Config.MaxEventRows) throw new ArgumentException("limit exceeds configured limit."); EventCursor cursor = ParseCursor(JsonUtil.OptionalObject(body, "after")); string generation = JsonUtil.OptionalString(body, "sourceGeneration");
            if (_afterOnly) { if (cursor == null) throw new ArgumentException("after is required."); if (String.IsNullOrEmpty(generation)) throw new ArgumentException("sourceGeneration is required with after."); EventPage afterPage = _c.Events.ReadAfterPage(cursor, limit, generation); _c.Log.Info("Event after rowCount=" + afterPage.Records.Count + " generation=" + afterPage.SourceGeneration); return WirePage(afterPage); }
            if (cursor != null && String.IsNullOrEmpty(generation)) throw new ArgumentException("sourceGeneration is required with after."); DateTime from = _c.Time.SourceToRawUtc(JsonUtil.Date(body, "from")); DateTime to = _c.Time.SourceToRawUtc(JsonUtil.Date(body, "to")); EventPage page = _c.Events.ReadRangePage(from, to, cursor, limit, generation); _c.Log.Info("Event range rowCount=" + page.Records.Count + " generation=" + page.SourceGeneration); return WirePage(page);
        }
        private EventCursor ParseCursor(Dictionary<string, object> o) { if (o == null) return null; int frac = JsonUtil.RequiredInt(o, "fracSec"); if (frac < Int16.MinValue || frac > Int16.MaxValue) throw new ArgumentException("fracSec is outside SmallInt range."); return new EventCursor { DateTimeValue = _c.Time.SourceToRawUtc(JsonUtil.Date(o, "dateTime")), FracSec = Convert.ToInt16(frac), Ord = JsonUtil.RequiredInt(o, "ord") }; }
        private IList<object> Wire(IList<EventRecord> rows) { List<object> result = new List<object>(); for (int i = 0; i < rows.Count; i++) { EventRecord e = rows[i]; DateTime sourceTimestamp = _c.Time.RawUtcToSource(e.DateTimeValue); result.Add(new { timestamp = sourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), fracSec = e.FracSec, ord = e.Ord, eventType = e.EventType, eventSubType = e.EventSubType, category = e.Category, area = e.Area, node = e.Node, unit = e.Unit, module = e.Module, moduleDescription = e.ModuleDescription, attribute = e.Attribute, state = e.State, eventLevel = e.EventLevel, desc1 = e.Desc1, desc2 = e.Desc2, isArchived = e.IsArchived, cursor = new { dateTime = sourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), fracSec = e.FracSec, ord = e.Ord } }); } return result; }
        private object WirePage(EventPage page) { return new { events = Wire(page.Records), nextCursor = WireCursor(page.NextCursor), hasMore = page.HasMore, sourceGeneration = page.SourceGeneration, earliestCursor = WireCursor(page.EarliestCursor), latestCursor = WireCursor(page.LatestCursor), sourceTimeZone = _c.Config.SourceTimeZone, timestampSemantics = "source-local" }; }
        private object WireCursor(EventCursor cursor) { if (cursor == null) return null; DateTime sourceTimestamp = _c.Time.RawUtcToSource(cursor.DateTimeValue); return new { dateTime = sourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), fracSec = cursor.FracSec, ord = cursor.Ord }; }
    }
}
