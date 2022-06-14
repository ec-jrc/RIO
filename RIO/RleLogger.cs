using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RIO
{
    /// <summary>
    /// Use this class to count the occurrences of data in a specified slice of time or with a mmaximum amount of data.
    /// In order to minimize the memory footprint of the collection of data, Run Length Encoding is used.
    /// </summary>
    /// <typeparam name="T">The nature of the data to be counted can be of any type, typically it is worth using it for strings.</typeparam>
    public class RleLogger<T>
    {
        class Chunk
        {
            public DateTime start = DateTime.UtcNow;
            public T data;
            public long count;
        }
        private LinkedList<Chunk> pieces = new LinkedList<Chunk>();
        private TimeSpan period = TimeSpan.Zero;
        private TimeSpan maxTime = TimeSpan.Zero;
        private int maxCount = 0;
        private Chunk current = null;

        /// <summary>
        /// Initializes an instance with a maximum of samples allowed. When the logger contains the amount of data specified, any new sample
        /// will evict the least recent one.
        /// </summary>
        /// <param name="count">Maximum amount of samples to be accounted.</param>
        public RleLogger(int count) { maxCount = count; }
        /// <summary>
        /// Initialize an instance with a maximum amount of time for the data to be kept. Passed that time, older data are evicted.
        /// </summary>
        /// <param name="span">Period of time between two adjacent samples in the logger.</param>
        /// <param name="persistence">Maximum age of the data kept by the logger.</param>
        public RleLogger(TimeSpan span, TimeSpan persistence) { period = span; maxTime = persistence; }

        /// <summary>
        /// Total number of samples the logger keeps.
        /// </summary>
        public long Count => pieces.ToArray().Select(c => c.count).Sum();
        /// <summary>
        /// Adds a new sample to the logger. RLE, if the sample is different from the previous sample added, will start to count the
        /// repetitions of this sample. Otherwise, it will just increment the counter of how many consecutive samples were recorded.
        /// </summary>
        /// <param name="data">The data to be added.</param>
        public void Add(T data)
        {
            if (current?.data.Equals(data) == true)
                current.count++;
            else
            {
                current = new Chunk() { data = data, count = 1 };
                pieces.AddLast(current);
            }

            if (maxCount > 0)
            {
                long totalCount = Count;
                while (totalCount > maxCount)
                {
                    Chunk c = pieces.First.Value;
                    if (--c.count <= 0)
                        pieces.RemoveFirst();
                    else if (period != TimeSpan.Zero)
                        c.start += period;
                    totalCount--;
                }
            }
            if (maxTime != TimeSpan.Zero)
            {
                DateTime start = pieces.First.Value.start;
                DateTime minStart = DateTime.UtcNow - maxTime;
                while (minStart > start)
                {
                    Chunk c = pieces.First.Value;
                    if (--c.count <= 0)
                        pieces.RemoveFirst();
                    else
                        c.start += period;
                    if (pieces.Count > 0)
                        start = pieces.First.Value.start;
                }
            }
        }
        /// <summary>
        /// A list of all the different samples stored in the logger.
        /// </summary>
        public IEnumerable<T> Samples => pieces.ToArray().Select<Chunk, T>(c => c.data).Distinct();

        /// <summary>
        /// Percentage of the presence of the single sample amongst the total.
        /// </summary>
        public IEnumerable<KeyValuePair<T, float>> Ratios
        {
            get
            {
                if (pieces.Count == 0) return new List<KeyValuePair<T, float>>();

                KeyValuePair<T, float>[] temp = Samples.Select(d => new KeyValuePair<T, float>(d, pieces.ToArray().Where(c => c.data.Equals(d)).Select(c => c.count).Sum())).ToArray();
                float count = temp.Sum(t => t.Value);
                return temp.Select(t => new KeyValuePair<T, float>(t.Key, t.Value / count));
            }
        }
    }
    /// <summary>
    /// This class is used to serialize correctly the <see cref="RleLogger{T}"/> instances: the output will contain the
    /// number of samples and the relative quantity of each normalized to 1.
    /// </summary>
    /// <typeparam name="T">The type of objects to collect.</typeparam>
    public class RleLoggerConverter<T> : Newtonsoft.Json.JsonConverter
    {
        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType"><see cref="Type"/> of the object.</param>
        /// <returns>true if this instance can convert the specified object type; otherwise, false.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(RleLogger<T>).Equals(objectType);
        }
        /// <summary>
        /// Reads the JSON representation of the object.
        /// !!!Not implemenyted yet!!!
        /// </summary>
        /// <param name="reader">The <see cref="JsonReader"/> to read from.</param>
        /// <param name="objectType">Type of the object.</param>
        /// <param name="existingValue">The existing value of object being read.</param>
        /// <param name="serializer">The calling serializer.</param>
        /// <returns>The object value deserialized.</returns>
        /// <exception cref="NotImplementedException"/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="JsonWriter"/> to write to.</param>
        /// <param name="value">The value.</param>
        /// <param name="serializer">The calling serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is RleLogger<T> data)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("Count");
                writer.WriteValue(data.Count);
                writer.WritePropertyName("Ratios");
                writer.WriteStartObject();
                Dictionary<T, float> ratios = data.Ratios.ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (T key in data.Ratios.Select(kv => kv.Key).ToArray())
                {
                    writer.WritePropertyName(key.ToString());
                    writer.WriteValue(ratios[key]);
                }
                writer.WriteEndObject();

                writer.WriteEndObject();
            }
        }
    }
}
