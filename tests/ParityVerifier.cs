using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal static class ParityVerifier
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3 || (args[0] != "history" && args[0] != "event")) { Console.WriteLine("Usage:\n  ParityVerifier history legacy.csv service-response.json TAG\n  ParityVerifier event legacy-batch.json service-response.json"); return 2; }
            if (args[0] == "history") { if (args.Length != 4) return 2; CompareHistory(args[1], args[2], args[3]); }
            else { if (args.Length != 3) return 2; CompareEvents(args[1], args[2]); }
            Console.WriteLine("PARITY PASSED"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("PARITY FAILED: " + ex.Message); return 1; }
    }

    private static void CompareHistory(string csvPath, string jsonPath, string tag)
    {
        List<List<string>> oldRows = new List<List<string>>(); string[] lines = File.ReadAllLines(csvPath);
        for (int i = 0; i < lines.Length; i++) if (lines[i].Length > 0 && lines[i][0] != '#' && !lines[i].StartsWith("Timestamp,")) oldRows.Add(Csv(lines[i]));
        Dictionary<string, object> root = Json(jsonPath); Dictionary<string, object> data = Dict(root, "data"); string sourceTimeZone = Text(data, "sourceTimeZone"); if (sourceTimeZone.Length == 0) throw new Exception("Missing sourceTimeZone."); Dictionary<string, object> samples = Dict(data, "samples"); object[] newRows = Array(samples, tag);
        Equal(oldRows.Count, newRows.Length, "history sample count");
        for (int i = 0; i < oldRows.Count; i++)
        {
            Dictionary<string, object> row = (Dictionary<string, object>)newRows[i]; Equal(SourceDateKey(oldRows[i][0], sourceTimeZone), DateKey(Text(row, "timestamp")), "history timestamp row " + i); Equal(oldRows[i][1], Invariant(row["value"]), "history value row " + i); Equal(oldRows[i][2], Text(row, "dataType"), "history dataType row " + i);
        }
        PrintEdges("History", oldRows.Count, oldRows.Count == 0 ? "(empty)" : SourceDateText(oldRows[0][0], sourceTimeZone), oldRows.Count == 0 ? "(empty)" : SourceDateText(oldRows[oldRows.Count - 1][0], sourceTimeZone));
    }

    private static void CompareEvents(string oldPath, string newPath)
    {
        Dictionary<string, object> oldRoot = Json(oldPath); object[] oldRows = Array(oldRoot, "records"); Dictionary<string, object> newRoot = Json(newPath); Dictionary<string, object> data = Dict(newRoot, "data"); string sourceTimeZone = Text(data, "sourceTimeZone"); if (sourceTimeZone.Length == 0) throw new Exception("Missing sourceTimeZone."); object[] newRows = Array(data, "events"); Equal(oldRows.Length, newRows.Length, "event row count");
        string[] fields = { "fracSec", "ord", "eventType", "eventSubType", "category", "area", "node", "unit", "module", "moduleDescription", "attribute", "state", "eventLevel", "desc1", "desc2", "isArchived" };
        for (int i = 0; i < oldRows.Length; i++)
        {
            Dictionary<string, object> a = (Dictionary<string, object>)oldRows[i]; Dictionary<string, object> b = (Dictionary<string, object>)newRows[i]; Equal(SourceDateKey(Text(a, "dateTime"), sourceTimeZone), DateKey(Text(b, "timestamp")), "event timestamp row " + i);
            for (int f = 0; f < fields.Length; f++) Equal(Value(a, fields[f]), Value(b, fields[f]), "event " + fields[f] + " row " + i);
        }
        string first = oldRows.Length == 0 ? "(empty)" : SourceCursor((Dictionary<string, object>)oldRows[0], sourceTimeZone); string last = oldRows.Length == 0 ? "(empty)" : SourceCursor((Dictionary<string, object>)oldRows[oldRows.Length - 1], sourceTimeZone); PrintEdges("Event", oldRows.Length, first, last);
    }

    private static string Cursor(Dictionary<string, object> row) { return Text(row, "dateTime") + "|" + Value(row, "fracSec") + "|" + Value(row, "ord"); }
    private static string SourceCursor(Dictionary<string, object> row, string zone) { return SourceDateText(Text(row, "dateTime"), zone) + "|" + Value(row, "fracSec") + "|" + Value(row, "ord"); }
    private static Dictionary<string, object> Json(string path) { JavaScriptSerializer s = new JavaScriptSerializer { MaxJsonLength = 67108864, RecursionLimit = 10000 }; Dictionary<string, object> value = s.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; if (value == null) throw new Exception(path + " is not a JSON object."); return value; }
    private static Dictionary<string, object> Dict(Dictionary<string, object> o, string key) { object v; if (!o.TryGetValue(key, out v) || !(v is Dictionary<string, object>)) throw new Exception("Missing object: " + key); return (Dictionary<string, object>)v; }
    private static object[] Array(Dictionary<string, object> o, string key) { object v; if (!o.TryGetValue(key, out v) || !(v is object[])) throw new Exception("Missing array: " + key); return (object[])v; }
    private static string Text(Dictionary<string, object> o, string key) { object v; return o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
    private static string Value(Dictionary<string, object> o, string key) { object v; return o.TryGetValue(key, out v) ? Invariant(v) : ""; }
    private static string Invariant(object value) { if (value == null) return ""; IFormattable f = value as IFormattable; return f == null ? value.ToString() : f.ToString(null, CultureInfo.InvariantCulture); }
    private static string DateKey(string value) { DateTime parsed; if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new Exception("Invalid timestamp: " + value); return parsed.Ticks.ToString(CultureInfo.InvariantCulture); }
    private static string SourceDateKey(string rawUtc, string zone) { return DateKey(SourceDateText(rawUtc, zone)); }
    private static string SourceDateText(string rawUtc, string zone) { DateTime parsed; if (!DateTime.TryParse(rawUtc, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new Exception("Invalid raw timestamp: " + rawUtc); DateTime source = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(zone)); return source.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture); }
    private static void Equal(object expected, object actual, string name) { if (!Object.Equals(expected, actual)) throw new Exception(name + " differs: old=[" + expected + "] new=[" + actual + "]"); }
    private static void PrintEdges(string name, int count, string first, string last) { Console.WriteLine(name + " count=" + count.ToString(CultureInfo.InvariantCulture)); Console.WriteLine(name + " first=" + first); Console.WriteLine(name + " last=" + last); }
    private static List<string> Csv(string line) { List<string> fields = new List<string>(); StringBuilder current = new StringBuilder(); bool quoted = false; for (int i = 0; i < line.Length; i++) { char c = line[i]; if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Length = 0; } else current.Append(c); } fields.Add(current.ToString()); return fields; }
}
