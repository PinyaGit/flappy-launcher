using System;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Unified JSON serializer adapter supporting both .NET Framework 4.8 and .NET 8+.
    /// </summary>
    internal static class JsonAdapter
    {
#if NETFRAMEWORK
        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default(T);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return ser.Deserialize<T>(json);
        }

        public static string ToJson(object obj)
        {
            if (obj == null) return "{}";
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return ser.Serialize(obj);
        }
#else
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = false
        };

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public static string ToJson(object obj)
        {
            if (obj == null) return "{}";
            return JsonSerializer.Serialize(obj, Options);
        }
#endif
    }
}
