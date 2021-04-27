using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;

#nullable enable

namespace CdkShared
{
    public static class Utils
    {
        // Parses JSON text into Dictionary or Array that looks like JSON to CDK "JSON" functions.
        // CDK JSON methods expect JS/TS "JSON", which is already an object, so need to do this boilerplate here.
        public static ICollection? JsonToMap(string json)
            => JObject.Parse(json)?.Root.ToCollection();

        // Returns either Dictionary<string, object>, or an Array.
        // Converts Newtonsoft object model into Dictionary<string, object> or object[].
        private static ICollection ToCollection(this JToken jTree)
        {
            switch(jTree.Type)
            {
                case JTokenType.Object:
                {
                    var map = new Dictionary<string, object?>();

                    foreach(JProperty jProp in jTree)
                        jProp.Value.ProcessJsonValue(result => map.Add(jProp.Name, result));
                    
                    return map;
                }
                case JTokenType.Array:
                {
                    var list = new List<object?>();

                    foreach(JToken jArrayItem in jTree)
                        jArrayItem.ProcessJsonValue(result => list.Add(result));
                    
                    return list.ToArray();
                }
                default:
                    throw new Exception($"Can't propcess token of \"{jTree.Type}\" type.");
            }
        }
        
        private static void ProcessJsonValue(this JToken jVal, Action<object> resultHandler)
        {
            switch(jVal.Type)
            {
                case JTokenType.Object:
                case JTokenType.Array:
                    resultHandler(jVal.ToCollection());
                    break;
                default:
                    resultHandler(jVal.ToObject<object>());
                    break;
            }
        }

        public static string ReadContentFromUrl(string url)
        {
            using var webClient = new WebClient();
            return webClient.DownloadString(url);
        }

        public static IEnumerable<T> ToEnumerable<T>(this T item)
        {
            if(item != null)
                yield return item;
        }

        public static string? BlankToNull(this string str) => string.IsNullOrWhiteSpace(str) ? null : str;

        public static bool IsNullOrBlank(this string str) => string.IsNullOrWhiteSpace(str);
    }
}
