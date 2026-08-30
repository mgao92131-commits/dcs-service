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
            if (args.Length != 4 || (args[0] != "history" && args[0] != "event")) { Console.WriteLine("Usage:\n  ParityVerifier history legacy.csv service.csv SOURCE_TIME_ZONE\n  ParityVerifier event legacy.json service.csv SOURCE_TIME_ZONE"); return 2; }
            if (args[0] == "history") CompareHistory(args[1], args[2], args[3]); else CompareEvents(args[1], args[2], args[3]);
            Console.WriteLine("PARITY PASSED"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("PARITY FAILED: " + ex.Message); return 1; }
    }

    private static void CompareHistory(string oldPath, string newPath, string zone)
    {
        List<List<string>> oldRows = new List<List<string>>(); string[] lines = File.ReadAllLines(oldPath);
        for (int i = 0; i < lines.Length; i++) if (lines[i].Length > 0 && lines[i][0] != '#' && !lines[i].StartsWith("Timestamp,")) oldRows.Add(ParseLine(lines[i]));
        List<List<string>> service = ReadCsv(newPath); RequireHeader(service, new string[] { "Timestamp", "Value", "DataType" }); service.RemoveAt(0); Equal(oldRows.Count, service.Count, "history sample count");
        for (int i = 0; i < oldRows.Count; i++) { Equal(SourceDateKey(oldRows[i][0], zone), DateKey(service[i][0]), "history timestamp row " + i); Equal(oldRows[i][1], service[i][1], "history value row " + i); Equal(oldRows[i][2], service[i][2], "history dataType row " + i); }
        PrintEdges("History", oldRows.Count, oldRows.Count == 0 ? "(empty)" : SourceDateText(oldRows[0][0], zone), oldRows.Count == 0 ? "(empty)" : SourceDateText(oldRows[oldRows.Count - 1][0], zone));
    }

    private static void CompareEvents(string oldPath, string newPath, string zone)
    {
        Dictionary<string, object> oldRoot = Json(oldPath); object[] oldRows = Array(oldRoot, "records"); List<List<string>> service = ReadCsv(newPath);
        string[] headers = { "DateTime", "FracSec", "Ord", "EventType", "EventSubType", "Category", "Area", "Node", "Unit", "Module", "ModuleDescription", "Attribute", "State", "EventLevel", "Desc1", "Desc2", "IsArchived" }; RequireHeader(service, headers); service.RemoveAt(0); Equal(oldRows.Length, service.Count, "event row count");
        string[] oldFields = { "fracSec", "ord", "eventType", "eventSubType", "category", "area", "node", "unit", "module", "moduleDescription", "attribute", "state", "eventLevel", "desc1", "desc2", "isArchived" };
        for (int i = 0; i < oldRows.Length; i++) { Dictionary<string, object> old = (Dictionary<string, object>)oldRows[i]; Equal(SourceDateKey(Text(old, "dateTime"), zone), DateKey(service[i][0]), "event timestamp row " + i); for (int f = 0; f < oldFields.Length; f++) Equal(Value(old, oldFields[f]), service[i][f + 1], "event " + oldFields[f] + " row " + i); }
        string first = oldRows.Length == 0 ? "(empty)" : SourceCursor((Dictionary<string, object>)oldRows[0], zone); string last = oldRows.Length == 0 ? "(empty)" : SourceCursor((Dictionary<string, object>)oldRows[oldRows.Length - 1], zone); PrintEdges("Event", oldRows.Length, first, last);
    }

    private static List<List<string>> ReadCsv(string path)
    {
        List<List<string>> rows = new List<List<string>>(); using (TextReader reader = new StreamReader(path, Encoding.UTF8, true))
        {
            List<string> row = new List<string>(); StringBuilder field = new StringBuilder(); bool quoted = false; int value;
            while ((value = reader.Read()) >= 0) { char c = (char)value; if (quoted) { if (c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else quoted = false; } else field.Append(c); }
                else if (c == '"' && field.Length == 0) quoted = true; else if (c == ',') { row.Add(field.ToString()); field.Length = 0; } else if (c == '\r' || c == '\n') { if (c == '\r' && reader.Peek() == '\n') reader.Read(); row.Add(field.ToString()); field.Length = 0; rows.Add(row); row = new List<string>(); } else field.Append(c); }
            if (quoted) throw new Exception("CSV ends inside a quoted field: " + path); if (field.Length != 0 || row.Count != 0) { row.Add(field.ToString()); rows.Add(row); }
        } return rows;
    }
    private static void RequireHeader(List<List<string>> rows, string[] expected) { if (rows.Count == 0 || rows[0].Count < expected.Length) throw new Exception("CSV header is missing."); for (int i = 0; i < expected.Length; i++) Equal(expected[i], rows[0][i], "CSV header " + i); }
    private static List<string> ParseLine(string line) { List<string> fields = new List<string>(); StringBuilder current = new StringBuilder(); bool quoted = false; for (int i = 0; i < line.Length; i++) { char c = line[i]; if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Length = 0; } else current.Append(c); } fields.Add(current.ToString()); return fields; }
    private static Dictionary<string, object> Json(string path) { JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 67108864, RecursionLimit = 10000 }; Dictionary<string, object> value = serializer.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; if (value == null) throw new Exception(path + " is not a JSON object."); return value; }
    private static object[] Array(Dictionary<string, object> value, string key) { object found; if (!value.TryGetValue(key, out found) || !(found is object[])) throw new Exception("Missing array: " + key); return (object[])found; }
    private static string Text(Dictionary<string, object> value, string key) { object found; return value.TryGetValue(key, out found) && found != null ? Convert.ToString(found, CultureInfo.InvariantCulture) : ""; }
    private static string Value(Dictionary<string, object> value, string key) { object found; if (!value.TryGetValue(key, out found) || found == null) return ""; IFormattable f = found as IFormattable; return f == null ? found.ToString() : f.ToString(null, CultureInfo.InvariantCulture); }
    private static string DateKey(string value) { DateTime parsed; if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new Exception("Invalid timestamp: " + value); return parsed.Ticks.ToString(CultureInfo.InvariantCulture); }
    private static string SourceDateKey(string rawUtc, string zone) { return DateKey(SourceDateText(rawUtc, zone)); }
    private static string SourceDateText(string rawUtc, string zone) { DateTime parsed; if (!DateTime.TryParse(rawUtc, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new Exception("Invalid raw timestamp: " + rawUtc); DateTime source = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(zone)); return source.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture); }
    private static string SourceCursor(Dictionary<string, object> row, string zone) { return SourceDateText(Text(row, "dateTime"), zone) + "|" + Value(row, "fracSec") + "|" + Value(row, "ord"); }
    private static void Equal(object expected, object actual, string name) { if (!Object.Equals(expected, actual)) throw new Exception(name + " differs: old=[" + expected + "] new=[" + actual + "]"); }
    private static void PrintEdges(string name, int count, string first, string last) { Console.WriteLine(name + " count=" + count.ToString(CultureInfo.InvariantCulture)); Console.WriteLine(name + " first=" + first); Console.WriteLine(name + " last=" + last); }
}
