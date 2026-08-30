using System;

namespace DcsDataService.DeltaV.Historian
{
    public sealed class HistoryQueryTooLargeException : Exception
    {
        public HistoryQueryTooLargeException(string message) : base(message) { }
    }

    public sealed class HistorySampleBudget
    {
        private readonly int _limit;
        private int _used;
        public HistorySampleBudget(int limit) { if (limit < 1) throw new ArgumentOutOfRangeException("limit"); _limit = limit; }
        public int Used { get { return _used; } }
        public void Add(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (count > _limit - _used) throw new HistoryQueryTooLargeException("History result exceeds MaxSamplesPerHistoryRequest=" + _limit + "; query stopped before more Historian segments were read.");
            _used += count;
        }
    }
}
