namespace DcsDataService.DeltaV.Historian
{
    public enum HistoryTagStatus { HistoryTagOK = 1, HistoryTagUnknown = 2, HistoryTagAmbiguous = 3, Error = -1 }

    public sealed class HistoryTagInfo
    {
        public string Tag { get; set; }
        public int Handle { get; set; }
        public HistoryTagStatus Status { get; set; }
        public string DataType { get; set; }
    }
}
