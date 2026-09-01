using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DcsDataService.DeltaV.Events;
using DcsDataService.Util;

namespace DcsDataService.Api.Handlers
{
    public sealed class EventHandler : IApiHandler
    {
        private readonly HandlerContext _c;
        public EventHandler(HandlerContext c) { _c = c; }

        public HttpResponse Handle(HttpRequest request)
        {
            IDictionary<string, string> query = QueryStringParser.Parse(request.QueryString);
            if (query.ContainsKey("limit")) throw new ArgumentException("limit is no longer supported; specify a complete time range instead.");

            bool hasFrom = Has(query, "from"); bool hasTo = Has(query, "to");
            bool hasCursor = Has(query, "afterTime") || Has(query, "afterFracSec") || Has(query, "afterOrd") || Has(query, "sourceGeneration");
            if (hasFrom && hasCursor) throw new ArgumentException("Specify exactly one mode: from/to or afterTime/afterFracSec/afterOrd/sourceGeneration/to.");
            if (!hasFrom && !hasCursor) throw new ArgumentException("Specify exactly one mode: from/to or afterTime/afterFracSec/afterOrd/sourceGeneration/to.");

            DateTime sourceFrom; DateTime sourceTo; DateTime rawFrom; DateTime rawTo; EventCursor cursor = null; string requestedGeneration = null;
            if (hasFrom)
            {
                if (!hasTo) throw new ArgumentException("to is required for range mode.");
                sourceFrom = QueryStringParser.RequiredDate(query, "from"); sourceTo = QueryStringParser.RequiredDate(query, "to");
                if (sourceTo <= sourceFrom) throw new ArgumentException("to must be after from.");
                rawFrom = _c.Time.SourceToRawUtc(sourceFrom); rawTo = _c.Time.SourceToRawUtc(sourceTo);
            }
            else
            {
                if (!hasTo) throw new ArgumentException("to is required for cursor mode.");
                sourceTo = QueryStringParser.RequiredDate(query, "to");
                int frac = QueryStringParser.RequiredInt(query, "afterFracSec"); if (frac < Int16.MinValue || frac > Int16.MaxValue) throw new ArgumentException("afterFracSec must be inside SmallInt range.");
                int ord = QueryStringParser.RequiredInt(query, "afterOrd"); sourceFrom = QueryStringParser.RequiredDate(query, "afterTime"); requestedGeneration = QueryStringParser.Required(query, "sourceGeneration");
                cursor = new EventCursor { DateTimeValue = _c.Time.SourceToRawUtc(sourceFrom), FracSec = Convert.ToInt16(frac), Ord = ord }; rawTo = _c.Time.SourceToRawUtc(sourceTo); rawFrom = cursor.DateTimeValue;
            }

            EventProvider.EventStream prepared = null; IDisposable gate = null; string generation; string fileName;
            try
            {
                gate = _c.EventGate.Enter(_c.Config.ProviderSlotWaitSeconds * 1000);
                if (hasFrom)
                {
                    prepared = _c.Events.PrepareRangeStream(rawFrom, rawTo); generation = prepared.SourceGeneration; fileName = DownloadFileName.Events(sourceFrom, sourceTo);
                }
                else
                {
                    prepared = _c.Events.PrepareAfterStream(cursor, rawTo, requestedGeneration); generation = prepared.SourceGeneration; fileName = DownloadFileName.Events(sourceFrom, sourceTo);
                }

                _c.Log.Info("Event stream start from=" + FormatDate(sourceFrom) + " to=" + FormatDate(sourceTo) + " generation=" + generation);
                EventProvider.EventStream streamForResponse = prepared;
                HttpResponse response = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8", IsChunked = true };
                response.Headers["X-DCS-Source-TimeZone"] = _c.Config.SourceTimeZone;
                response.Headers["X-DCS-Source-Generation"] = generation;
                response.Headers["X-DCS-To"] = FormatDate(sourceTo);
                response.Headers["Content-Disposition"] = "attachment; filename=\"" + fileName + "\"";
                response.BodyResource = new DisposablePair(prepared, gate);
                prepared = null; gate = null;
                response.BodyWriter = delegate(Stream stream)
                {
                    Stopwatch clock = Stopwatch.StartNew(); long rows = 0;
                    StreamWriter text = new StreamWriter(stream, new UTF8Encoding(false)); CsvWriter csv = new CsvWriter(text);
                    csv.WriteRow("DateTime", "FracSec", "Ord", "EventType", "EventSubType", "Category", "Area", "Node", "Unit", "Module", "ModuleDescription", "Attribute", "State", "EventLevel", "Desc1", "Desc2", "IsArchived"); text.Flush();
                    try
                    {
                        streamForResponse.Stream(delegate(EventRecord record)
                        {
                            csv.WriteRow(FormatDate(_c.Time.RawUtcToSource(record.DateTimeValue)), record.FracSec, record.Ord, record.EventType, record.EventSubType, record.Category, record.Area, record.Node, record.Unit, record.Module, record.ModuleDescription, record.Attribute, record.State, record.EventLevel, record.Desc1, record.Desc2, record.IsArchived);
                            rows++; if (rows % 1000 == 0) text.Flush();
                        });
                        text.Flush(); clock.Stop();
                        _c.Log.Info("Event stream complete durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " rows=" + rows.ToString(CultureInfo.InvariantCulture) + " generation=" + generation);
                    }
                    catch (Exception ex)
                    {
                        clock.Stop(); _c.Log.Error("Event stream aborted durationMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " rows=" + rows.ToString(CultureInfo.InvariantCulture) + " generation=" + generation, ex); throw;
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

        private static bool Has(IDictionary<string, string> values, string name) { return values.ContainsKey(name); }
        private static string FormatDate(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture); }
    }
}
