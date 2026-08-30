using System;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventSourceInfo
    {
        public string SourceNode { get; set; } public DateTime CreateTime { get; set; } public string Generation { get; set; }
        public long EstimatedRows { get; set; } public bool OverflowHasRows { get; set; } public bool IsFull { get; set; }
    }
}
