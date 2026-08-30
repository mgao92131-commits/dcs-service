using System;

namespace DcsDataService.Util
{
    public sealed class SourceTimeConverter
    {
        private readonly TimeZoneInfo _sourceZone;

        public SourceTimeConverter(string sourceTimeZone)
        {
            if (String.IsNullOrEmpty(sourceTimeZone)) throw new ArgumentException("Source time zone is required.", "sourceTimeZone");
            _sourceZone = TimeZoneInfo.FindSystemTimeZoneById(sourceTimeZone);
        }

        public string SourceTimeZone { get { return _sourceZone.Id; } }

        public DateTime SourceToRawUtc(DateTime sourceTime)
        {
            DateTime unspecified = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
            if (_sourceZone.IsInvalidTime(unspecified)) throw new ArgumentException("Source DateTime falls in an invalid daylight-saving transition.");
            if (_sourceZone.IsAmbiguousTime(unspecified)) throw new ArgumentException("Source DateTime is ambiguous during a daylight-saving transition.");
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, _sourceZone);
        }

        public DateTime RawUtcToSource(DateTime rawUtc)
        {
            DateTime utc = DateTime.SpecifyKind(rawUtc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, _sourceZone), DateTimeKind.Unspecified);
        }
    }
}

