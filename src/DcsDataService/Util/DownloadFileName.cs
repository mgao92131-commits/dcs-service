using System;
using System.Globalization;

namespace DcsDataService.Util
{
    public static class DownloadFileName
    {
        public static string History(string tag, DateTime from, DateTime to)
        {
            return "history_" + Sanitize(tag) + "_" + SanitizeDate(from) + "_" + SanitizeDate(to) + ".csv";
        }

        public static string Events(DateTime from, DateTime to)
        {
            return "events_" + SanitizeDate(from) + "_" + SanitizeDate(to) + ".csv";
        }

        public static string Sanitize(string value)
        {
            if (String.IsNullOrEmpty(value)) return "download";
            char[] invalid = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            string result = value;
            for (int i = 0; i < invalid.Length; i++) result = result.Replace(invalid[i], '_');
            for (int i = 0; i < result.Length; i++) if (Char.IsControl(result[i])) result = result.Replace(result[i], '_');
            return result.Trim().Length == 0 ? "download" : result.Trim();
        }

        private static string SanitizeDate(DateTime value) { return Sanitize(value.ToString("yyyy-MM-ddTHH-mm-ss.fffffff", CultureInfo.InvariantCulture)); }
    }
}
