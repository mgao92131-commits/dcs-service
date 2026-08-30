using System;
using System.Collections.Generic;
using System.Globalization;

namespace DcsDataService.Api
{
    public static class QueryStringParser
    {
        public static Dictionary<string, string> Parse(string query)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrEmpty(query)) return result;
            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i].Length == 0) continue;
                int equals = pairs[i].IndexOf('=');
                string key = Decode(equals < 0 ? pairs[i] : pairs[i].Substring(0, equals));
                string value = Decode(equals < 0 ? "" : pairs[i].Substring(equals + 1));
                if (key.Length == 0) throw new ArgumentException("Query parameter name cannot be empty.");
                if (result.ContainsKey(key)) throw new ArgumentException("Duplicate query parameter: " + key + ".");
                result.Add(key, value);
            }
            return result;
        }

        public static string Required(IDictionary<string, string> values, string name)
        {
            string value; if (!values.TryGetValue(name, out value) || String.IsNullOrEmpty(value)) throw new ArgumentException(name + " is required."); return value;
        }

        public static DateTime RequiredDate(IDictionary<string, string> values, string name)
        {
            string value = Required(values, name); DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) throw new ArgumentException(name + " must be an ISO date/time.");
            if (parsed.Kind != DateTimeKind.Unspecified) throw new ArgumentException(name + " must be a source-local time without Z or an offset.");
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        public static int OptionalInt(IDictionary<string, string> values, string name, int defaultValue)
        {
            string value; int parsed; if (!values.TryGetValue(name, out value)) return defaultValue;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException(name + " must be an integer."); return parsed;
        }

        public static int RequiredInt(IDictionary<string, string> values, string name)
        {
            string value = Required(values, name); int parsed; if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException(name + " must be an integer."); return parsed;
        }

        private static string Decode(string value)
        {
            try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
            catch (UriFormatException) { throw new ArgumentException("Query string contains invalid percent encoding."); }
        }
    }
}
