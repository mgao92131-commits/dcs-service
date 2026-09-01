using System;
using System.Collections.Generic;
using DvAccess = DeltaV.Historian.DvCHDataAccess.DvCHDataAccess;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorianProbeResult
    {
        public HistorianStatus Status; public HistoryTagInfo Tag; public int Samples;
    }

    public sealed class HistorianApiProbe
    {
        private readonly HistorianProvider _provider;
        public HistorianApiProbe(HistorianProvider provider) { _provider = provider; }
        public static string CheckDll() { return typeof(DvAccess).Assembly.FullName; }
        public HistorianProbeResult Run(string testTag)
        {
            HistorianProbeResult result = new HistorianProbeResult(); result.Status = _provider.Probe();
            if (String.IsNullOrEmpty(testTag)) throw new HistorianException("Historian.TestTag must be configured for probe.");
            result.Tag = _provider.ResolveTags(new List<string> { testTag })[0];
            if (result.Tag.Status != HistoryTagStatus.HistoryTagOK) throw new HistorianException("Test tag is " + result.Tag.Status + ": " + testTag);
            int samples = 0;
            _provider.ReadRawStream(testTag, DateTime.Now.AddMinutes(-5), DateTime.Now, 1000, TimeSpan.FromMinutes(5), delegate(IList<HistorySample> batch) { samples += batch.Count; });
            result.Samples = samples; return result;
        }
    }
}
