namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorianStatus
    {
        public bool DllAvailable { get; set; }
        public bool Connected { get; set; }
        public string Server { get; set; }
        public int ServerState { get; set; }
        public string Message { get; set; }
    }
}
