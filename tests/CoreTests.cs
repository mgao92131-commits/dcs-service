using System;
using System.Collections.Generic;
using System.IO;
using DcsDataService.Configuration;
using DcsDataService.DeltaV.Events;
using DcsDataService.DeltaV.Historian;

internal static class CoreTests
{
    private static int _count;
    public static int Main()
    {
        try { CursorUsesAllFields(); HistoryNormalizationSortsAndDeduplicates(); ConfigurationRejectsNonLoopback(); Console.WriteLine("CORE TESTS PASSED (" + _count + ")"); return 0; }
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
    private static HistorySample Sample(DateTime time, object value, int sequence) { return new HistorySample { Tag = "T", Timestamp = time, Value = value, DataType = "Float", DeltaVStatus = "Good", ArchiveStatus = "HistoryDataIsValid", SequenceNo = sequence }; }
    private static void ConfigurationRejectsNonLoopback()
    {
        string path = Path.Combine(Path.GetTempPath(), "dcs-service-config-" + Guid.NewGuid().ToString("N") + ".ini");
        try { File.WriteAllText(path, "[Api]\r\nBind=0.0.0.0\r\n"); bool rejected = false; try { IniConfigLoader.Load(path); } catch (ConfigurationException) { rejected = true; } Check(rejected, "V1 configuration must reject non-loopback binds."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void Check(bool condition, string message) { _count++; if (!condition) throw new Exception(message); }
}
