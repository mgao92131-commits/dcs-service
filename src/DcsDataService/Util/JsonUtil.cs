using System.Web.Script.Serialization;

namespace DcsDataService.Util
{
    public static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { RecursionLimit = 16 };
        public static string Serialize(object value) { lock (Serializer) return Serializer.Serialize(value); }
    }
}
