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
        public int ApiPort = 18080;
        public int HistoryMaxConcurrent = 2;
        public int EventMaxConcurrent = 4;
        public int RequestQueueLimit = 32;
        public int MaxEventRows = 5000;
        public int MaxHistorySpanHours = 24;
        public int HistorianReadChunkSamples = 10000;
        public int MaxSamplesPerHistoryRequest = 50000;
        public int RequestTimeoutSeconds = 60;
        public string SourceTimeZone = "China Standard Time";
        public string LogDirectory = "logs";

        public void Validate()
        {
            if (String.IsNullOrEmpty(HistorianServer)) throw new ConfigurationException("Historian.Server is required.");
            if (String.IsNullOrEmpty(EventsServer)) throw new ConfigurationException("Events.Server is required.");
            if (String.IsNullOrEmpty(EventsDatabase)) throw new ConfigurationException("Events.Database is required.");
            if (String.IsNullOrEmpty(EventsSchema) || String.IsNullOrEmpty(EventsTable)) throw new ConfigurationException("Events.Schema and Events.Table are required.");
            Positive(ApiPort, "Api.Port"); Positive(HistorianConnectionTimeoutSeconds, "Historian.ConnectionTimeoutSeconds");
            Positive(EventsCommandTimeoutSeconds, "Events.CommandTimeoutSeconds"); Positive(EventsStateCacheSeconds, "Events.RuntimeStateCacheSeconds");
            Positive(HistoryMaxConcurrent, "Concurrency.HistoryMaxConcurrent"); Positive(EventMaxConcurrent, "Concurrency.EventMaxConcurrent"); Positive(RequestQueueLimit, "Concurrency.RequestQueueLimit");
            Positive(MaxEventRows, "ApiLimits.MaxEventRows");
            Positive(MaxHistorySpanHours, "ApiLimits.MaxHistorySpanHours"); Positive(HistorianReadChunkSamples, "Historian.ReadChunkSamples"); Positive(MaxSamplesPerHistoryRequest, "ApiLimits.MaxSamplesPerHistoryRequest");
            Positive(RequestTimeoutSeconds, "ApiLimits.RequestTimeoutSeconds");
        }

        private static void Positive(int value, string name) { if (value <= 0) throw new ConfigurationException(name + " must be positive."); }
    }

    public sealed class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
    }
}
