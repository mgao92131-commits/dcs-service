using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace DcsDataService.Util
{
    public static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 67108864, RecursionLimit = 32 };
        public static string Serialize(object value) { lock (Serializer) return Serializer.Serialize(value); }
        public static Dictionary<string, object> Object(string json) { try { lock (Serializer) { object value = Serializer.DeserializeObject(json); Dictionary<string, object> result = value as Dictionary<string, object>; if (result == null) throw new ArgumentException("JSON body must be an object."); return result; } } catch (ArgumentException) { throw; } catch (Exception ex) { throw new ArgumentException("Invalid JSON body: " + ex.Message); } }
        public static string RequiredString(Dictionary<string, object> o, string name) { object v; if (!o.TryGetValue(name, out v) || v == null || String.IsNullOrEmpty(Convert.ToString(v, CultureInfo.InvariantCulture))) throw new ArgumentException(name + " is required."); return Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static string OptionalString(Dictionary<string, object> o, string name) { object v; return o.TryGetValue(name, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null; }
        public static int Int(Dictionary<string, object> o, string name, int fallback) { object v; return o.TryGetValue(name, out v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback; }
        public static int RequiredInt(Dictionary<string, object> o, string name) { object v; if (!o.TryGetValue(name, out v) || v == null) throw new ArgumentException(name + " is required."); try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch (Exception) { throw new ArgumentException(name + " must be an integer."); } }
        public static DateTime Date(Dictionary<string, object> o, string name) { string s = RequiredString(o, name); DateTime value; if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)) throw new ArgumentException(name + " is not a valid source DateTime."); return DateTime.SpecifyKind(value, DateTimeKind.Unspecified); }
        public static IList<string> Strings(Dictionary<string, object> o, string name) { object value; if (!o.TryGetValue(name, out value)) throw new ArgumentException(name + " is required."); object[] items = value as object[]; if (items == null) throw new ArgumentException(name + " must be an array."); List<string> result = new List<string>(); for (int i = 0; i < items.Length; i++) { string s = Convert.ToString(items[i], CultureInfo.InvariantCulture); if (String.IsNullOrEmpty(s)) throw new ArgumentException(name + " contains an empty value."); result.Add(s); } return result; }
        public static Dictionary<string, object> OptionalObject(Dictionary<string, object> o, string name) { object v; if (!o.TryGetValue(name, out v) || v == null) return null; Dictionary<string, object> d = v as Dictionary<string, object>; if (d == null) throw new ArgumentException(name + " must be an object."); return d; }
    }
}
