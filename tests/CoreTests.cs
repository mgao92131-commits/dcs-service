using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using DcsDataService.Api;
using DcsDataService.Api.Handlers;
using DcsDataService.Configuration;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;
using DcsDataService.Util;

internal static class CoreTests
{
    private static int _count;

    public static int Main()
    {
        try
        {
            CursorUsesAllFields(); EventSourceFailsClosed(); EventCursorWindowIsValidated();
            ChunkedWriteStreamFramesData(); ChunkedResponseCompletesOnlyOnSuccess();
            SourceTimeUsesBeijing(); HistoryNormalizationSortsAndDeduplicates();
            CsvIsStandardsCompliant(); QueryStringIsParsed(); RemovedLimitIsRejected();
            ConfigurationUsesStreamingDefaults(); DownloadNamesAreSafe();
            ServerIsLoopbackOnly(); ConcurrencyGateLimitsEntry();
            Console.WriteLine("CORE TESTS PASSED (" + _count + ")"); return 0;
        }
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
        IList<HistorySample> rows = HistorianProvider.Normalize(new List<HistorySample> { later, duplicate, first }); Check(rows.Count == 2, "Exact segment duplicates must be removed."); Check(rows[0].Timestamp == t1 && rows[1].Timestamp == t2, "Samples must be timestamp sorted.");
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

    private static void ChunkedWriteStreamFramesData()
    {
        MemoryStream raw = new MemoryStream(); ChunkedWriteStream stream = new ChunkedWriteStream(raw);
        byte[] first = Encoding.ASCII.GetBytes("abc"); byte[] second = Encoding.ASCII.GetBytes("defg"); stream.Write(first, 0, first.Length); stream.Write(second, 0, second.Length); stream.Complete();
        string expected = "3\r\nabc\r\n4\r\ndefg\r\n0\r\n\r\n"; Check(Encoding.ASCII.GetString(raw.ToArray()) == expected, "ChunkedWriteStream must frame every non-empty write and terminate with zero chunk.");
    }

    private static void ChunkedResponseCompletesOnlyOnSuccess()
    {
        HttpResponse success = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8", IsChunked = true };
        success.BodyWriter = delegate(Stream stream) { byte[] bytes = Encoding.UTF8.GetBytes("header\r\n"); stream.Write(bytes, 0, bytes.Length); };
        MemoryStream completeOutput = new MemoryStream(); success.WriteTo(completeOutput); string completeText = Encoding.ASCII.GetString(completeOutput.ToArray());
        Check(completeText.IndexOf("Transfer-Encoding: chunked\r\n", StringComparison.OrdinalIgnoreCase) >= 0, "Chunked response must advertise transfer encoding."); Check(completeText.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase) < 0, "Chunked response must not advertise Content-Length."); Check(completeText.EndsWith("0\r\n\r\n"), "Successful chunked response must have a terminating chunk.");

        TrackingDisposable resource = new TrackingDisposable();
        HttpResponse failed = new HttpResponse { StatusCode = 200, ContentType = "text/csv; charset=utf-8", IsChunked = true, BodyResource = resource };
        failed.BodyWriter = delegate(Stream stream) { byte[] bytes = Encoding.UTF8.GetBytes("partial\r\n"); stream.Write(bytes, 0, bytes.Length); throw new InvalidOperationException("synthetic stream failure"); };
        MemoryStream failedOutput = new MemoryStream(); bool threw = false; try { failed.WriteTo(failedOutput); } catch (InvalidOperationException) { threw = true; }
        string failedText = Encoding.ASCII.GetString(failedOutput.ToArray()); Check(threw && failed.IsStarted && resource.Disposed, "BodyWriter failures must propagate after response headers start and release the body resource."); Check(failedText.IndexOf("partial", StringComparison.Ordinal) >= 0 && !failedText.EndsWith("0\r\n\r\n"), "Failed chunked response must not have a terminating chunk.");
    }

    private static void SourceTimeUsesBeijing()
    {
        SourceTimeConverter time = new SourceTimeConverter("China Standard Time"); DateTime rawUtc = new DateTime(2026, 8, 30, 2, 0, 0); DateTime source = time.RawUtcToSource(rawUtc);
        Check(source == new DateTime(2026, 8, 30, 10, 0, 0) && source.Kind == DateTimeKind.Unspecified, "Raw UTC must be returned as Beijing source-local time."); Check(time.SourceToRawUtc(source).Kind == DateTimeKind.Utc, "Source-local input must convert to UTC.");
    }

    private static void CsvIsStandardsCompliant()
    {
        CultureInfo before = Thread.CurrentThread.CurrentCulture; Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        try { StringWriter text = new StringWriter(); new CsvWriter(text).WriteRow("ABC,DEF", "He said \"TEST\"", "line1\r\nline2", null, "涓枃", 1.5); Check(text.ToString() == "\"ABC,DEF\",\"He said \"\"TEST\"\"\",\"line1\r\nline2\",,涓枃,1.5\r\n", "CSV escaping/null/Unicode/invariant number output is fixed."); }
        finally { Thread.CurrentThread.CurrentCulture = before; }
    }

    private static void QueryStringIsParsed()
    {
        IDictionary<string, string> values = QueryStringParser.Parse("tag=A%2FB%20C"); Check(values["tag"] == "A/B C", "GET query string must decode values."); bool duplicate = false; try { QueryStringParser.Parse("tag=A&tag=B"); } catch (ArgumentException) { duplicate = true; } Check(duplicate, "Duplicate query parameters must be rejected.");
    }

    private static void RemovedLimitIsRejected()
    {
        DcsDataService.Api.Handlers.EventHandler handler = new DcsDataService.Api.Handlers.EventHandler(new HandlerContext()); bool rejected = false;
        try { handler.Handle(new HttpRequest { Method = "GET", Path = "/api/v1/events", QueryString = "from=2026-08-30T08%3A00%3A00&to=2026-08-30T09%3A00%3A00&limit=1" }); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Event limit must be rejected instead of being silently treated as pagination.");
    }

    private static void ConfigurationUsesStreamingDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "dcs-service-config-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[Api]\r\nPort=18080\r\n[Historian]\r\nStreamWindowMinutes=60\r\n[Timeout]\r\nProviderSlotWaitSeconds=60\r\nSocketReadSeconds=60\r\nSocketWriteSeconds=120\r\n");
            ServiceConfig config = IniConfigLoader.Load(path); Check(config.StreamWindowMinutes == 60 && config.ProviderSlotWaitSeconds == 60 && config.SocketWriteSeconds == 120, "Streaming timeout/window defaults must be loaded."); Check(config.HistoryMaxConcurrent == 2 && config.EventMaxConcurrent == 4 && config.RequestQueueLimit == 32, "Concurrency defaults must be 2/4/32.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void DownloadNamesAreSafe()
    {
        string name = DownloadFileName.History("A/B:C?D", new DateTime(2026, 8, 30), new DateTime(2026, 8, 31));
        Check(name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0 && name.EndsWith(".csv"), "Download file names must replace Windows-invalid characters.");
    }

    private static void ServerIsLoopbackOnly() { Check(ApiServer.ListenAddress.Equals(IPAddress.Loopback), "Server listener must be hard-coded to IPAddress.Loopback."); }

    private static void ConcurrencyGateLimitsEntry()
    {
        ConcurrencyGate gate = new ConcurrencyGate(2); ManualResetEvent start = new ManualResetEvent(false); int active = 0; int maximum = 0; Thread[] threads = new Thread[10];
        for (int i = 0; i < threads.Length; i++) { threads[i] = new Thread(delegate() { start.WaitOne(); using (gate.Enter(5000)) { int now = Interlocked.Increment(ref active); lock (threads) if (now > maximum) maximum = now; Thread.Sleep(20); Interlocked.Decrement(ref active); } }); threads[i].Start(); }
        start.Set(); for (int i = 0; i < threads.Length; i++) threads[i].Join(); Check(maximum == 2, "Concurrency gate must admit at most two workers.");
    }

    private static HistorySample Sample(DateTime time, object value, int sequence) { return new HistorySample { Tag = "T", Timestamp = time, Value = value, DataType = "Float", DeltaVStatus = "Good", ArchiveStatus = "HistoryDataIsValid", SequenceNo = sequence }; }
    private sealed class TrackingDisposable : IDisposable { public bool Disposed; public void Dispose() { Disposed = true; } }
    private static void Check(bool condition, string message) { _count++; if (!condition) throw new Exception(message); }
}
