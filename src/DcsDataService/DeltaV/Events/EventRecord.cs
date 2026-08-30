using System;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventRecord
    {
        public DateTime DateTimeValue { get; set; } public short FracSec { get; set; } public int Ord { get; set; }
        public string EventType { get; set; } public string EventSubType { get; set; } public string Category { get; set; }
        public string Area { get; set; } public string Node { get; set; } public string Unit { get; set; }
        public string Module { get; set; } public string ModuleDescription { get; set; } public string Attribute { get; set; }
        public string State { get; set; } public string EventLevel { get; set; } public string Desc1 { get; set; } public string Desc2 { get; set; }
        public short? IsArchived { get; set; }
        public EventCursor Cursor { get { return new EventCursor { DateTimeValue = DateTimeValue, FracSec = FracSec, Ord = Ord }; } }
    }
}
