using System;
using System.Collections.Generic;

namespace DcsRealtime
{
    public static class DeltaVInstallation
    {
        public const string BinDirectory = @"C:\DeltaV\bin";
    }
    public sealed class ReadError
    {
        public string code { get; set; }
        public string message { get; set; }
        public string stage { get; set; }
        public int? nativeStatus { get; set; }
        public string nativeStatusName { get; set; }
    }
    public sealed class ReadItem
    {
        public string tag { get; set; }
        public bool ok { get; set; }
        public object value { get; set; }
        public string valueText { get; set; }
        public string valueType { get; set; }
        public int? nativeDataType { get; set; }
        public string nativeTypeName { get; set; }
        public string observedAt { get; set; }
        public string sourceTimeZone { get; set; }
        public string sourceTimestamp { get; set; }
        public object quality { get; set; }
        public ReadError error { get; set; }
    }
    public sealed class ReadBatch
    {
        public int protocolVersion { get; set; }
        public bool ok { get; set; }
        public bool complete { get; set; }
        public int total { get; set; }
        public int successCount { get; set; }
        public int failureCount { get; set; }
        public ReadItem[] items { get; set; }
        public ReadError error { get; set; }
    }
    public static class ReadValidation
    {
        public const int ProtocolVersion = 1;
        public const int HardMaximumTags = 64;
        public const int MaximumTagLength = 512;
        public static void Tags(string[] tags, int maximum)
        {
            if (tags == null || tags.Length == 0 || tags.Length > maximum || tags.Length > HardMaximumTags)
                throw new ArgumentException("tags must contain 1 to " + Math.Min(maximum, HardMaximumTags) + " complete parameter paths.");
            foreach (string tag in tags)
            {
                if (String.IsNullOrEmpty(tag) || tag.Length > MaximumTagLength || tag != tag.Trim()) throw new ArgumentException("Invalid parameter path length or surrounding whitespace.");
                foreach (char c in tag) if (Char.IsControl(c)) throw new ArgumentException("Parameter paths cannot contain control characters.");
                int slash = tag.LastIndexOf('/'), dot = tag.LastIndexOf('.');
                if (slash < 1 || dot <= slash + 1 || dot == tag.Length - 1 || tag.IndexOf("//", StringComparison.Ordinal) >= 0)
                    throw new ArgumentException("Use a full native parameter path such as MODULE/AI1/PV.CV.");
            }
        }
        public static ReadBatch Completed(ReadItem[] items)
        {
            int successes = 0; foreach (ReadItem item in items) if (item.ok) successes++;
            return new ReadBatch { protocolVersion = ProtocolVersion, complete = true, ok = successes == items.Length, total = items.Length, successCount = successes, failureCount = items.Length - successes, items = items };
        }
    }
}
