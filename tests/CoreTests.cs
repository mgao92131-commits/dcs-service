using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using DcsDataService.Configuration;
using DcsDataService.Api;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

internal static class CoreTests
{
    private static int _count;
    public static int Main()
    {
        try { CursorUsesAllFields(); EventSourceFailsClosed(); EventCursorWindowIsValidated(); EventPagingHeadersAreComplete(); SourceTimeUsesBeijing(); HistoryNormalizationSortsAndDeduplicates(); HistoryBudgetStopsEarly(); CsvIsStandardsCompliant(); QueryStringIsParsed(); ConfigurationUsesV1Defaults(); ServerIsLoopbackOnly(); ConcurrencyGateLimitsEntry(); Console.WriteLine("CORE TESTS PASSED (" + _count + ")"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("CORE TEST FAILED: " + ex); return 1; }
    }
    private static void CursorUsesAllFields()
    {
        EventCursor a = new EventCursor { DateTimeValue = new DateTime(2026, 8, 30, 1, 2, 3), FracSec = 10, Ord = 1 };
        EventCursor b = new EventCursor { DateTimeValue = a.DateTimeValue, FracSec = 11, Ord = 0 }; EventCursor c = new EventCursor { DateTimeValue = a.DateTimeValue, FracSec = 10, Ord = 2 };
        Check(a.CompareTo(b) < 0, "FracSec must participate in cursor ordering."); Check(a.CompareTo(c) < 0, "Ord must participate in cursor ordering."); Check(a.ToString().EndsWith("|10|1"), "Cursor string must contain all fields.");
    }
    private static void HistoryNormalizationSortsAndDeduplicates()
    {
        DateTime t1 = new DateTime(2026, 8, 30, 1, 0, 0); DateTime t2 = t1.AddSeconds(1); HistorySample first = Sample(t1, 1.25, 7); HistorySample duplicate = Sample(t1, 1.25, 7); HistorySample later = Sample(t2, 2.5, 8);
        IList<HistorySample> rows = HistorianProvider.Normalize(new List<HistorySample> { later, duplicate, first }); Check(rows.Count == 2, "Exact split-boundary duplicates must be removed."); Check(rows[0].Timestamp == t1 && rows[1].Timestamp == t2, "Samples must be timestamp sorted.");
    }
    private static void EventSourceFailsClosed()
    {
        EventSourceUnsafeException error = null; try { EventProvider.EnsureSourceSafe(new EventSourceInfo { OverflowHasRows = true }); } catch (EventSourceUnsafeException ex) { error = ex; } Check(error != null && error.ErrorCode == "event_overflow", "EJOverflow rows must fail closed.");
        error = null; try { EventProvider.EnsureSourceSafe(new EventSourceInfo { IsFull = true }); } catch (EventSourceUnsafeException ex) { error = ex; } Check(error != null && error.ErrorCode == "event_journal_full", "IsFull must fail closed.");
    }
    private static void EventCursorWindowIsValidated()
    {
        EventCursor earliest = new EventCursor { DateTimeValue = new DateTime(2026, 8, 25), FracSec = 0, Ord = 1 }; EventCursor latest = new EventCursor { DateTimeValue = new DateTime(2026, 8, 30), FracSec = 0, Ord = 9 };
        EventCursorException error = null; try { EventProvider.ValidateCursorWindow(new EventCursor { DateTimeValue = new DateTime(2026, 8, 20) }, earliest, latest, "G", "G"); } catch (EventCursorException ex) { error = ex; } Check(error != null && error.ErrorCode == "event_cursor_expired", "Expired cursor must be rejected.");
        error = null; try { EventProvider.ValidateCursorWindow(latest, earliest, latest, "OLD", "NEW"); } catch (EventCursorException ex) { error = ex; } Check(error != null && error.ErrorCode == "source_changed", "Generation mismatch must be rejected.");
    }
    private static void EventPagingHeadersAreComplete()
    {
        HttpResponse response = new HttpResponse(); EventPage page = new EventPage { HasMore = true, NextCursor = new EventCursor { DateTimeValue = new DateTime(2026, 8, 30, 8, 0, 0), FracSec = 123, Ord = 456 } };
        DcsDataService.Api.Handlers.EventHandler.ApplyPagingHeaders(response, page, new SourceTimeConverter("China Standard Time"));
        Check(response.Headers["X-DCS-Has-More"] == "true", "Range and cursor responses must expose HasMore."); Check(response.Headers["X-DCS-Next-DateTime"] == "2026-08-30T16:00:00.000" && response.Headers["X-DCS-Next-FracSec"] == "123" && response.Headers["X-DCS-Next-Ord"] == "456", "Range and cursor responses must expose the provider NextCursor.");
    }
    private static void SourceTimeUsesBeijing()
    {
        SourceTimeConverter time = new SourceTimeConverter("China Standard Time"); DateTime rawUtc = new DateTime(2026, 8, 30, 2, 0, 0); DateTime source = time.RawUtcToSource(rawUtc);
        Check(source == new DateTime(2026, 8, 30, 10, 0, 0) && source.Kind == DateTimeKind.Unspecified, "Raw UTC must be returned as Beijing source-local time."); Check(time.SourceToRawUtc(source).Kind == DateTimeKind.Utc, "Source-local input must convert to UTC.");
    }
    private static void HistoryBudgetStopsEarly()
    {
        HistorySampleBudget budget = new HistorySampleBudget(5); budget.Add(3); bool rejected = false; try { budget.Add(3); } catch (HistoryQueryTooLargeException) { rejected = true; } Check(rejected && budget.Used == 3, "History budget must reject before accepting excess samples.");
    }
    private static void CsvIsStandardsCompliant()
    {
        CultureInfo before = Thread.CurrentThread.CurrentCulture; Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        try { StringWriter text = new StringWriter(); new CsvWriter(text).WriteRow("ABC,DEF", "He said \"TEST\"", "line1\r\nline2", null, "中文", 1.5); Check(text.ToString() == "\"ABC,DEF\",\"He said \"\"TEST\"\"\",\"line1\r\nline2\",,中文,1.5\r\n", "CSV escaping/null/Unicode/invariant number output is fixed."); }
        finally { Thread.CurrentThread.CurrentCulture = before; }
    }
    private static void QueryStringIsParsed()
    {
        IDictionary<string, string> values = QueryStringParser.Parse("tag=A%2FB%20C&limit=10"); Check(values["tag"] == "A/B C" && QueryStringParser.OptionalInt(values, "limit", 1) == 10, "GET query string must decode and parse values."); bool duplicate = false; try { QueryStringParser.Parse("tag=A&tag=B"); } catch (ArgumentException) { duplicate = true; } Check(duplicate, "Duplicate query parameters must be rejected.");
    }
    private static void ConfigurationUsesV1Defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "dcs-service-config-" + Guid.NewGuid().ToString("N") + ".ini");
        try { File.WriteAllText(path, "[Api]\r\nPort=18080\r\n"); ServiceConfig config = IniConfigLoader.Load(path); Check(config.HistoryMaxConcurrent == 2 && config.EventMaxConcurrent == 4 && config.RequestQueueLimit == 32, "V1 concurrency defaults must be 2/4/32."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void ServerIsLoopbackOnly() { Check(ApiServer.ListenAddress.Equals(IPAddress.Loopback), "Server listener must be hard-coded to IPAddress.Loopback."); }
    private static void ConcurrencyGateLimitsEntry()
    {
        ConcurrencyGate gate = new ConcurrencyGate(2); ManualResetEvent start = new ManualResetEvent(false); int active = 0; int maximum = 0; Thread[] threads = new Thread[10];
        for (int i = 0; i < threads.Length; i++) { threads[i] = new Thread(delegate() { start.WaitOne(); using (gate.Enter(5000)) { int now = Interlocked.Increment(ref active); lock (threads) if (now > maximum) maximum = now; Thread.Sleep(20); Interlocked.Decrement(ref active); } }); threads[i].Start(); }
        start.Set(); for (int i = 0; i < threads.Length; i++) threads[i].Join(); Check(maximum == 2, "Concurrency gate must admit at most two workers.");
    }
    private static HistorySample Sample(DateTime time, object value, int sequence) { return new HistorySample { Tag = "T", Timestamp = time, Value = value, DataType = "Float", DeltaVStatus = "Good", ArchiveStatus = "HistoryDataIsValid", SequenceNo = sequence }; }
    private static void Check(bool condition, string message) { _count++; if (!condition) throw new Exception(message); }
}
