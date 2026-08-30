using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using DeltaVEventSync.Agent.Configuration;
using DeltaVEventSync.Agent.DeltaV;
using DeltaVEventSync.Agent.Models;

// Read-only reference exporter. DeltaVReader.cs and its domain models are
// compiled directly from the existing sibling DcsAgent deployment.
internal static class EventParityExport
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || (args[0] != "range" && args[0] != "after")) { Usage(); return 2; }
            Dictionary<string, string> options = Parse(args);
            AgentConfig config = new AgentConfig();
            config.SourceServer = Required(options, "--server");
            config.SourceDatabase = Required(options, "--database");
            config.SourceSchema = Required(options, "--schema");
            config.SourceTable = Required(options, "--table");
            config.CommandTimeoutSeconds = Number(options, "--timeout", 1, 600);
            int limit = Number(options, "--limit", 1, 5000);
            DeltaVReader reader = new DeltaVReader(config);
            List<EventRecord> records;
            if (args[0] == "range")
            {
                records = reader.ReadRange(Date(options, "--from"), Date(options, "--to"), null, limit);
            }
            else
            {
                SyncCursor cursor = new SyncCursor();
                cursor.DateTimeValue = Date(options, "--cursor-date");
                cursor.FracSec = Convert.ToInt16(Number(options, "--cursor-frac", Int16.MinValue, Int16.MaxValue), CultureInfo.InvariantCulture);
                cursor.Ord = Number(options, "--cursor-ord", Int32.MinValue, Int32.MaxValue);
                records = reader.ReadAfter(cursor, limit);
            }
            List<object> wire = new List<object>();
            for (int i = 0; i < records.Count; i++)
            {
                EventRecord e = records[i];
                wire.Add(new { dateTime = e.DateTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), fracSec = e.FracSec, ord = e.Ord, eventType = e.EventType, eventSubType = e.EventSubType, category = e.Category, area = e.Area, node = e.Node, unit = e.Unit, module = e.Module, moduleDescription = e.ModuleDescription, attribute = e.Attribute, state = e.State, eventLevel = e.EventLevel, desc1 = e.Desc1, desc2 = e.Desc2, isArchived = e.IsArchived });
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 67108864;
            string output = Required(options, "--out");
            File.WriteAllText(output, serializer.Serialize(new { records = wire }), new UTF8Encoding(false));
            Console.WriteLine("EventParityExport mode=" + args[0] + " rows=" + records.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Output=" + Path.GetFullPath(output));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("EVENT REFERENCE EXPORT FAILED: " + ex.Message); return 1; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--")) throw new ArgumentException("Invalid option list.");
            result[args[i]] = args[i + 1];
        }
        return result;
    }
    private static string Required(Dictionary<string, string> options, string name) { string value; if (!options.TryGetValue(name, out value) || String.IsNullOrEmpty(value)) throw new ArgumentException(name + " is required."); return value; }
    private static int Number(Dictionary<string, string> options, string name, int min, int max) { int value; if (!Int32.TryParse(Required(options, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max) throw new ArgumentException(name + " is outside its allowed range."); return value; }
    private static DateTime Date(Dictionary<string, string> options, string name) { DateTime value; if (!DateTime.TryParseExact(Required(options, name), new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fffffff" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) throw new ArgumentException(name + " must be a source-local DeltaV timestamp."); return value; }
    private static void Usage() { Console.WriteLine("EventParityExport range --server S --database D --schema dbo --table Journal --timeout 30 --from TIME --to TIME --limit N --out FILE"); Console.WriteLine("EventParityExport after --server S --database D --schema dbo --table Journal --timeout 30 --cursor-date TIME --cursor-frac N --cursor-ord N --limit N --out FILE"); }
}

