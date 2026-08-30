using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DcsDataService.Configuration
{
    public static class IniConfigLoader
    {
        public static ServiceConfig Load(string path)
        {
            if (!File.Exists(path)) throw new ConfigurationException("Configuration file not found: " + path);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int equals = line.IndexOf('=');
                if (equals <= 0) throw new ConfigurationException("Invalid INI line " + (i + 1).ToString(CultureInfo.InvariantCulture) + ".");
                values[section + "." + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            ServiceConfig c = new ServiceConfig(); c.ConfigPath = Path.GetFullPath(path);
            c.HistorianServer = Text(values, "Historian.Server", c.HistorianServer);
            c.HistorianConnectionTimeoutSeconds = Number(values, "Historian.ConnectionTimeoutSeconds", c.HistorianConnectionTimeoutSeconds);
            c.HistorianTestTag = Text(values, "Historian.TestTag", c.HistorianTestTag);
            c.EventsServer = Text(values, "Events.Server", c.EventsServer); c.EventsDatabase = Text(values, "Events.Database", c.EventsDatabase);
            c.EventsSchema = Text(values, "Events.Schema", c.EventsSchema); c.EventsTable = Text(values, "Events.Table", c.EventsTable);
            c.EventsCommandTimeoutSeconds = Number(values, "Events.CommandTimeoutSeconds", c.EventsCommandTimeoutSeconds);
            c.EventsStateCacheSeconds = Number(values, "Events.RuntimeStateCacheSeconds", c.EventsStateCacheSeconds);
            c.ApiPort = Number(values, "Api.Port", c.ApiPort);
            c.HistoryMaxConcurrent = Number(values, "Concurrency.HistoryMaxConcurrent", c.HistoryMaxConcurrent); c.EventMaxConcurrent = Number(values, "Concurrency.EventMaxConcurrent", c.EventMaxConcurrent); c.RequestQueueLimit = Number(values, "Concurrency.RequestQueueLimit", c.RequestQueueLimit);
            c.MaxEventRows = Number(values, "ApiLimits.MaxEventRows", c.MaxEventRows);
            c.MaxHistorySpanHours = Number(values, "ApiLimits.MaxHistorySpanHours", c.MaxHistorySpanHours);
            c.HistorianReadChunkSamples = Number(values, "Historian.ReadChunkSamples", c.HistorianReadChunkSamples); c.RequestTimeoutSeconds = Number(values, "ApiLimits.RequestTimeoutSeconds", c.RequestTimeoutSeconds);
            c.MaxSamplesPerHistoryRequest = Number(values, "ApiLimits.MaxSamplesPerHistoryRequest", c.MaxSamplesPerHistoryRequest);
            c.SourceTimeZone = Text(values, "Time.SourceTimeZone", c.SourceTimeZone); c.LogDirectory = Text(values, "Files.Logs", c.LogDirectory);
            c.Validate(); return c;
        }

        private static string Text(Dictionary<string, string> v, string k, string d) { string s; return v.TryGetValue(k, out s) ? s : d; }
        private static int Number(Dictionary<string, string> v, string k, int d) { string s; int n; if (!v.TryGetValue(k, out s)) return d; if (!Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) throw new ConfigurationException(k + " must be an integer."); return n; }
    }
}
