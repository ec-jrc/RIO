using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RIO;
using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using stj = System.Text.Json;
using Test = System.Collections.Generic.Dictionary<string, RIO.Message[]>;

namespace TAD
{
    internal static class Helpers
    {
        public static ExpandoObject ToExpando(IDictionary<string, JToken> dictionary)
        {
            ExpandoObject expando = new ExpandoObject();
            var expandoDic = (IDictionary<string, object>)expando;

            // go through the items in the dictionary and copy over the key value pairs)
            foreach (KeyValuePair<string, JToken> kvp in dictionary)
            {
                // if the value can also be turned into an ExpandoObject, then do it!
                if (kvp.Value.HasValues)
                {
                    var expandoValue = ToExpando(kvp.Value as IDictionary<string, JToken>);
                    expandoDic.Add(kvp.Key.ToString(), expandoValue);
                }
                else if (kvp.Value is ICollection collection)
                {
                    // iterate through the collection and convert any strin-object dictionaries
                    // along the way into expando objects
                    var itemList = new List<object>();
                    foreach (var item in collection)
                    {
                        if (item is IDictionary<string, object>)
                        {
                            var expandoItem = ((IDictionary)item).ToExpando();
                            itemList.Add(expandoItem);
                        }
                        else
                        {
                            itemList.Add(item);
                        }
                    }

                    expandoDic.Add(kvp.Key.ToString(), itemList);
                }
                else
                {
                    expandoDic[kvp.Key] = kvp.Value.ToString();
                }
            }

            return expando;
        }
        internal static Test TestsLoader()
        {
            Test value = new Test();
            try
            {
                string filename = "Test.json";
                using var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename));
                Newtonsoft.Json.JsonSerializer deserializer = Newtonsoft.Json.JsonSerializer.Create();
                //deserializer.Converters.Add(new ExpandoObjectConverter());
                using TextReader reader = new StreamReader(stream);
                using JsonReader r = new JsonTextReader(reader);
                value = deserializer.Deserialize(r, typeof(Test)) as Test;
            }
            catch { }
            return value;
        }

    }
    internal struct AlertNotification
    {
        public string Id;
        public DateTime Time;
    }
    public class JArrayConverter : stj.Serialization.JsonConverter<JArray>
    {
        public override JArray Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, JArray value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (JToken jToken in value)
            {
                switch (jToken)
                {
                    case JValue jValue:
                        stj.JsonSerializer.Serialize(writer, jValue.Value, options);
                        break;
                    case JObject jObject:
                        stj.JsonSerializer.Serialize(writer, jObject.ToString(), options);
                        break;
                    default:
                        stj.JsonSerializer.Serialize(writer, jToken.ToString(), options);
                        break;
                }
            }
            writer.WriteEndArray();
        }
    }

    public class TimeSpanConverter : stj.Serialization.JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString(), CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
        }
    }
}
