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
            Dictionary<string, object> body = JsonUtil.Object(request.Body); int limit = JsonUtil.Int(body, "limit", _c.Config.MaxEventRows); if (limit < 1 || limit > _c.Config.MaxEventRows) throw new ArgumentException("limit exceeds configured limit."); EventCursor cursor = ParseCursor(JsonUtil.OptionalObject(body, "after"));
            if (_afterOnly) { if (cursor == null) throw new ArgumentException("after is required."); IList<EventRecord> afterRows = _c.Events.ReadAfter(cursor, limit); _c.Log.Info("Event after rowCount=" + afterRows.Count); return new { events = Wire(afterRows), sourceTimeZone = _c.Config.SourceTimeZone }; }
            DateTime from = JsonUtil.Date(body, "from"); DateTime to = JsonUtil.Date(body, "to"); IList<EventRecord> rows = _c.Events.ReadRange(from, to, cursor, limit); _c.Log.Info("Event range rowCount=" + rows.Count); return new { events = Wire(rows), sourceTimeZone = _c.Config.SourceTimeZone };
        }
        private static EventCursor ParseCursor(Dictionary<string, object> o) { if (o == null) return null; int frac = JsonUtil.RequiredInt(o, "fracSec"); if (frac < Int16.MinValue || frac > Int16.MaxValue) throw new ArgumentException("fracSec is outside SmallInt range."); return new EventCursor { DateTimeValue = JsonUtil.Date(o, "dateTime"), FracSec = Convert.ToInt16(frac), Ord = JsonUtil.RequiredInt(o, "ord") }; }
        private static IList<object> Wire(IList<EventRecord> rows) { List<object> result = new List<object>(); for (int i = 0; i < rows.Count; i++) { EventRecord e = rows[i]; result.Add(new { timestamp = e.DateTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), fracSec = e.FracSec, ord = e.Ord, eventType = e.EventType, eventSubType = e.EventSubType, category = e.Category, area = e.Area, node = e.Node, unit = e.Unit, module = e.Module, moduleDescription = e.ModuleDescription, attribute = e.Attribute, state = e.State, eventLevel = e.EventLevel, desc1 = e.Desc1, desc2 = e.Desc2, isArchived = e.IsArchived, cursor = new { dateTime = e.DateTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), fracSec = e.FracSec, ord = e.Ord } }); } return result; }
    }
}
