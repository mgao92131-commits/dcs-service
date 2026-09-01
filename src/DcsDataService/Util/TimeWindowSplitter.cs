using System;

namespace DcsDataService.Util
{
    public static class TimeWindowSplitter
    {
        public static void ForEach(DateTime from, DateTime to, TimeSpan size, Action<DateTime, DateTime> action)
        {
            if (to <= from) throw new ArgumentException("Window end must be after window start.");
            if (size <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("size");
            if (action == null) throw new ArgumentNullException("action");

            DateTime windowStart = from;
            while (windowStart < to)
            {
                TimeSpan remaining = to.Subtract(windowStart);
                DateTime windowEnd = remaining <= size ? to : windowStart.Add(size);
                action(windowStart, windowEnd);
                windowStart = windowEnd;
            }
        }
    }
}
