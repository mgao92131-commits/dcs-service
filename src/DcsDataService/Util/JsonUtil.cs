using System.Web.Script.Serialization;

namespace DcsDataService.Util
{
    public static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { RecursionLimit = 16, MaxJsonLength = 8388608 };
        public static T Deserialize<T>(string value) { lock (Serializer) return Serializer.Deserialize<T>(value); }
        public static string Serialize(object value) { lock (Serializer) return Serializer.Serialize(value); }
    }
}
