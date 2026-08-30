using System.Collections.Generic;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventPage
    {
        public readonly List<EventRecord> Records = new List<EventRecord>();
        public EventCursor NextCursor { get; set; }
        public bool HasMore { get; set; }
        public string SourceGeneration { get; set; }
        public EventCursor EarliestCursor { get; set; }
        public EventCursor LatestCursor { get; set; }
    }

    public sealed class EventSourceUnsafeException : System.Exception
    {
        public string ErrorCode { get; private set; }
        public EventSourceUnsafeException(string code, string message) : base(message) { ErrorCode = code; }
    }

    public sealed class EventCursorException : System.Exception
    {
        public string ErrorCode { get; private set; }
        public EventCursorException(string code, string message) : base(message) { ErrorCode = code; }
    }
}
