using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DcsDataService.DeltaV.Events;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class EventHandler : IApiHandler
    {
        private readonly HandlerContext _c; public EventHandler(HandlerContext c) { _c = c; }
        public HttpResponse Handle(HttpRequest request)
        {
            IDictionary<string, string> query = QueryStringParser.Parse(request.QueryString); int limit = QueryStringParser.OptionalInt(query, "limit", _c.Config.MaxEventRows);
            if (limit < 1 || limit > _c.Config.MaxEventRows) throw new ArgumentException("limit must be between 1 and MaxEventRows.");
            bool hasRange = Has(query, "from") || Has(query, "to"); bool hasCursor = Has(query, "afterTime") || Has(query, "afterFracSec") || Has(query, "afterOrd");
            if (hasRange == hasCursor) throw new ArgumentException("Specify exactly one mode: from/to or afterTime/afterFracSec/afterOrd.");
            EventPage page;
            using (_c.EventGate.Enter(_c.Config.RequestTimeoutSeconds * 1000))
            {
                if (hasRange)
                {
                    DateTime from = _c.Time.SourceToRawUtc(QueryStringParser.RequiredDate(query, "from")); DateTime to = _c.Time.SourceToRawUtc(QueryStringParser.RequiredDate(query, "to"));
                    page = _c.Events.ReadRangePage(from, to, null, limit, null);
                }
                else
                {
                    int frac = QueryStringParser.RequiredInt(query, "afterFracSec"); if (frac < Int16.MinValue || frac > Int16.MaxValue) throw new ArgumentException("afterFracSec must be inside SmallInt range.");
                    int ord = QueryStringParser.RequiredInt(query, "afterOrd");
                    EventCursor cursor = new EventCursor { DateTimeValue = _c.Time.SourceToRawUtc(QueryStringParser.RequiredDate(query, "afterTime")), FracSec = Convert.ToInt16(frac), Ord = ord };
                    string generation = QueryStringParser.Required(query, "sourceGeneration"); page = _c.Events.ReadAfterPage(cursor, limit, generation);
                }
            }
            _c.Log.Info("Event query rowCount=" + page.Records.Count.ToString(CultureInfo.InvariantCulture) + " generation=" + page.SourceGeneration);
            return Csv(page);
        }
        private HttpResponse Csv(EventPage page)
        {
            HttpResponse response = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8" };
            response.Headers["X-DCS-Row-Count"] = page.Records.Count.ToString(CultureInfo.InvariantCulture); response.Headers["X-DCS-Source-TimeZone"] = _c.Config.SourceTimeZone; response.Headers["X-DCS-Source-Generation"] = page.SourceGeneration;
            ApplyPagingHeaders(response, page, _c.Time);
            IList<EventRecord> rows = page.Records;
            response.BodyWriter = delegate(Stream stream)
            {
                StreamWriter text = new StreamWriter(stream, new UTF8Encoding(false)); CsvWriter csv = new CsvWriter(text);
                csv.WriteRow("DateTime", "FracSec", "Ord", "EventType", "EventSubType", "Category", "Area", "Node", "Unit", "Module", "ModuleDescription", "Attribute", "State", "EventLevel", "Desc1", "Desc2", "IsArchived");
                for (int i = 0; i < rows.Count; i++) { EventRecord e = rows[i]; csv.WriteRow(FormatDate(_c.Time.RawUtcToSource(e.DateTimeValue)), e.FracSec, e.Ord, e.EventType, e.EventSubType, e.Category, e.Area, e.Node, e.Unit, e.Module, e.ModuleDescription, e.Attribute, e.State, e.EventLevel, e.Desc1, e.Desc2, e.IsArchived); }
                text.Flush();
            };
            return response;
        }
        private static bool Has(IDictionary<string, string> values, string name) { return values.ContainsKey(name); }
        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture); }
        public static void ApplyPagingHeaders(HttpResponse response, EventPage page, SourceTimeConverter time)
        {
            if (response == null) throw new ArgumentNullException("response"); if (page == null) throw new ArgumentNullException("page"); if (time == null) throw new ArgumentNullException("time");
            response.Headers["X-DCS-Has-More"] = page.HasMore ? "true" : "false"; EventCursor cursor = page.NextCursor; if (cursor == null) return;
            response.Headers["X-DCS-Next-DateTime"] = FormatDate(time.RawUtcToSource(cursor.DateTimeValue)); response.Headers["X-DCS-Next-FracSec"] = cursor.FracSec.ToString(CultureInfo.InvariantCulture); response.Headers["X-DCS-Next-Ord"] = cursor.Ord.ToString(CultureInfo.InvariantCulture);
        }
    }
}
