using System;

namespace DcsDataService.Configuration
{
    public sealed class ServiceConfig
    {
        public string ConfigPath;
        public string HistorianServer = "APP";
        public int HistorianConnectionTimeoutSeconds = 30;
        public string HistorianTestTag = "";
        public string EventsServer = ".";
        public string EventsDatabase = "EventJournal";
        public string EventsSchema = "dbo";
        public string EventsTable = "Journal";
        public int EventsCommandTimeoutSeconds = 30;
        public int EventsStateCacheSeconds = 30;
        public string ApiBind = "127.0.0.1";
        public int ApiPort = 18080;
        public string ApiKey = "CHANGE_ME";
        public int MaxTagsPerRequest = 50;
        public int MaxEventRows = 5000;
        public int MaxRequestBytes = 1048576;
        public int MaxHistorySpanHours = 24;
        public int MaxSamplesPerRead = 10000;
        public int MaxSamplesPerRequest = 50000;
        public int MaxResponseBytes = 8388608;
        public int RequestTimeoutSeconds = 60;
        public string SourceTimeZone = "China Standard Time";
        public string LogDirectory = "logs";

        public void Validate()
        {
            if (String.IsNullOrEmpty(HistorianServer)) throw new ConfigurationException("Historian.Server is required.");
            if (String.IsNullOrEmpty(EventsServer)) throw new ConfigurationException("Events.Server is required.");
            if (String.IsNullOrEmpty(EventsDatabase)) throw new ConfigurationException("Events.Database is required.");
            if (String.IsNullOrEmpty(EventsSchema) || String.IsNullOrEmpty(EventsTable)) throw new ConfigurationException("Events.Schema and Events.Table are required.");
            if (!String.Equals(ApiBind, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(ApiBind, "localhost", StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationException("Api.Bind must be 127.0.0.1 or localhost in v1.");
            Positive(ApiPort, "Api.Port"); Positive(HistorianConnectionTimeoutSeconds, "Historian.ConnectionTimeoutSeconds");
            Positive(EventsCommandTimeoutSeconds, "Events.CommandTimeoutSeconds"); Positive(EventsStateCacheSeconds, "Events.StateCacheSeconds"); Positive(MaxTagsPerRequest, "ApiLimits.MaxTagsPerRequest");
            Positive(MaxEventRows, "ApiLimits.MaxEventRows"); Positive(MaxRequestBytes, "ApiLimits.MaxRequestBytes");
            Positive(MaxHistorySpanHours, "ApiLimits.MaxHistorySpanHours"); Positive(MaxSamplesPerRead, "ApiLimits.MaxSamplesPerRead"); Positive(MaxSamplesPerRequest, "ApiLimits.MaxSamplesPerRequest"); Positive(MaxResponseBytes, "ApiLimits.MaxResponseBytes");
            Positive(RequestTimeoutSeconds, "ApiLimits.RequestTimeoutSeconds");
            if (MaxResponseBytes > 67108864) throw new ConfigurationException("ApiLimits.MaxResponseBytes cannot exceed 67108864.");
        }

        private static void Positive(int value, string name) { if (value <= 0) throw new ConfigurationException(name + " must be positive."); }
    }

    public sealed class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
    }
}
