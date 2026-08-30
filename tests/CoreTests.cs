using System;
using System.Collections.Generic;
using System.IO;
using DcsDataService.Configuration;
using DcsDataService.Api;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;

internal static class CoreTests
{
    private static int _count;
    public static int Main()
    {
        try { CursorUsesAllFields(); EventSourceFailsClosed(); EventCursorWindowIsValidated(); HistoryNormalizationSortsAndDeduplicates(); HistoryBudgetStopsEarly(); ResponseByteLimitUsesUtf8(); ConfigurationRejectsNonLoopback(); Console.WriteLine("CORE TESTS PASSED (" + _count + ")"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("CORE TEST FAILED: " + ex); return 1; }
    }
    private static void CursorUsesAllFields()
    {
        EventCursor a = new EventCursor { DateTimeValue = new DateTime(2026, 8, 30, 1, 2, 3), FracSec = 10, Ord = 1 };
        EventCursor b = new EventCursor { DateTimeValue = a.DateTimeValue, FracSec = 11, Ord = 0 };
        EventCursor c = new EventCursor { DateTimeValue = a.DateTimeValue, FracSec = 10, Ord = 2 };
        Check(a.CompareTo(b) < 0, "FracSec must participate in cursor ordering."); Check(a.CompareTo(c) < 0, "Ord must participate in cursor ordering."); Check(a.ToString().EndsWith("|10|1"), "Cursor string must contain all fields.");
    }
    private static void HistoryNormalizationSortsAndDeduplicates()
    {
        DateTime t1 = new DateTime(2026, 8, 30, 1, 0, 0); DateTime t2 = t1.AddSeconds(1);
        HistorySample first = Sample(t1, 1.25, 7); HistorySample duplicate = Sample(t1, 1.25, 7); HistorySample later = Sample(t2, 2.5, 8);
        IList<HistorySample> rows = HistorianProvider.Normalize(new List<HistorySample> { later, duplicate, first });
        Check(rows.Count == 2, "Exact split-boundary duplicates must be removed."); Check(rows[0].Timestamp == t1 && rows[1].Timestamp == t2, "Samples must be timestamp sorted."); Check(rows[0].Value is double, "Numeric sample value must remain typed.");
    }
    private static void EventSourceFailsClosed()
    {
        EventSourceUnsafeException error = null; try { EventProvider.EnsureSourceSafe(new EventSourceInfo { OverflowHasRows = true }); } catch (EventSourceUnsafeException ex) { error = ex; }
        Check(error != null && error.ErrorCode == "event_overflow", "EJOverflow rows must fail closed.");
        error = null; try { EventProvider.EnsureSourceSafe(new EventSourceInfo { IsFull = true }); } catch (EventSourceUnsafeException ex) { error = ex; }
        Check(error != null && error.ErrorCode == "event_journal_full", "IsFull must fail closed."); EventProvider.EnsureSourceSafe(new EventSourceInfo()); Check(true, "Safe source must pass.");
    }
    private static void EventCursorWindowIsValidated()
    {
        EventCursor earliest = new EventCursor { DateTimeValue = new DateTime(2026, 8, 25), FracSec = 0, Ord = 1 }; EventCursor latest = new EventCursor { DateTimeValue = new DateTime(2026, 8, 30), FracSec = 0, Ord = 9 };
        EventCursorException error = null; try { EventProvider.ValidateCursorWindow(new EventCursor { DateTimeValue = new DateTime(2026, 8, 20) }, earliest, latest, "G", "G"); } catch (EventCursorException ex) { error = ex; } Check(error != null && error.ErrorCode == "cursor_expired", "Expired cursor must be rejected.");
        error = null; try { EventProvider.ValidateCursorWindow(latest, earliest, latest, "OLD", "NEW"); } catch (EventCursorException ex) { error = ex; } Check(error != null && error.ErrorCode == "source_changed", "Generation mismatch must be rejected.");
        error = null; try { EventProvider.ValidateCursorWindow(new EventCursor { DateTimeValue = new DateTime(2026, 9, 1) }, earliest, latest, "G", "G"); } catch (EventCursorException ex) { error = ex; } Check(error != null && error.ErrorCode == "cursor_ahead", "Cursor ahead of source must be rejected.");
    }
    private static void HistoryBudgetStopsEarly()
    {
        HistorySampleBudget budget = new HistorySampleBudget(5); budget.Add(3); Check(budget.Used == 3, "History budget must accumulate samples."); bool rejected = false; try { budget.Add(3); } catch (HistoryQueryTooLargeException) { rejected = true; } Check(rejected && budget.Used == 3, "History budget must reject before accepting excess samples.");
    }
    private static void ResponseByteLimitUsesUtf8()
    {
        bool rejected = false; try { ApiServer.EnsureResponseSize("中", 2); } catch (ResponseTooLargeException) { rejected = true; } Check(rejected, "Response limit must count UTF-8 bytes."); ApiServer.EnsureResponseSize("中", 3); Check(true, "Exact UTF-8 response limit must pass.");
    }
    private static HistorySample Sample(DateTime time, object value, int sequence) { return new HistorySample { Tag = "T", Timestamp = time, Value = value, DataType = "Float", DeltaVStatus = "Good", ArchiveStatus = "HistoryDataIsValid", SequenceNo = sequence }; }
    private static void ConfigurationRejectsNonLoopback()
    {
        string path = Path.Combine(Path.GetTempPath(), "dcs-service-config-" + Guid.NewGuid().ToString("N") + ".ini");
        try { File.WriteAllText(path, "[Api]\r\nBind=0.0.0.0\r\n"); bool rejected = false; try { IniConfigLoader.Load(path); } catch (ConfigurationException) { rejected = true; } Check(rejected, "V1 configuration must reject non-loopback binds."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void Check(bool condition, string message) { _count++; if (!condition) throw new Exception(message); }
}
