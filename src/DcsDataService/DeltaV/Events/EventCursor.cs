using System;
using System.Globalization;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventCursor : IComparable<EventCursor>
    {
        public DateTime DateTimeValue { get; set; }
        public short FracSec { get; set; }
        public int Ord { get; set; }
        public int CompareTo(EventCursor other) { if (other == null) return 1; int v = DateTimeValue.CompareTo(other.DateTimeValue); if (v != 0) return v; v = FracSec.CompareTo(other.FracSec); return v != 0 ? v : Ord.CompareTo(other.Ord); }
        public override string ToString() { return DateTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "|" + FracSec.ToString(CultureInfo.InvariantCulture) + "|" + Ord.ToString(CultureInfo.InvariantCulture); }
    }
}
