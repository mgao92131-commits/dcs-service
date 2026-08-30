using System;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistorianException : Exception
    {
        public HistorianException(string message) : base(message) { }
        public HistorianException(string message, Exception inner) : base(message, inner) { }
    }
}
