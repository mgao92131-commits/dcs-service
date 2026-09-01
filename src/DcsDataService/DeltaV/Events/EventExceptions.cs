using System;

namespace DcsDataService.DeltaV.Events
{
    public sealed class EventSourceUnsafeException : Exception
    {
        public string ErrorCode { get; private set; }
        public EventSourceUnsafeException(string code, string message) : base(message) { ErrorCode = code; }
    }

    public sealed class EventCursorException : Exception
    {
        public string ErrorCode { get; private set; }
        public EventCursorException(string code, string message) : base(message) { ErrorCode = code; }
    }
}
