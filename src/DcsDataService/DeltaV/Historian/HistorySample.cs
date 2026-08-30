using System;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorySample
    {
        public string Tag { get; set; }
        public DateTime Timestamp { get; set; }
        public object Value { get; set; }
        public string DataType { get; set; }
        public string DeltaVStatus { get; set; }
        public string ArchiveStatus { get; set; }
        public int SequenceNo { get; set; }
        public bool IsHistoryHole { get; set; }
        public bool IsCRHole { get; set; }
        public bool IsManuallyDeleted { get; set; }
        public bool IsManuallyInserted { get; set; }
    }
}
