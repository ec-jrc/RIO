using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RIO
{
    /// <summary>
    /// Use this class to log for the amount of time the time intervals between subsequent events.
    /// Every occurrence of an event, call <see cref="Pulse"/> to add a data. First invocation will be used as the initial time.
    /// The logger will hold the data not older than <see cref="Timespan"/>.
    /// In order to exploit it, use <see cref="Min"/>, <see cref="Max"/> and <see cref="Average"/>.
    /// </summary>
    public class DelayLogger
    {
        DateTime lastPulse = DateTime.MinValue;
        TimeSpan timespan;
        Dictionary<DateTime, TimeSpan> records = new Dictionary<DateTime, TimeSpan>();

        /// <summary>
        /// Build a new logger, that will hold data for the specified amount of time.
        /// </summary>
        /// <param name="timeWindow">The amount of time we want to collect information about.</param>
        public DelayLogger(TimeSpan timeWindow)
        {
            timespan = timeWindow;
        }

        /// <summary>
        /// The amount of time we want to collect information about.
        /// </summary>
        public TimeSpan Timespan { get => timespan; set => timespan = value; }
        /// <summary>
        /// Add a data: the amount of time past from the last invocation of this method.
        /// First invocation will define the initial time.
        /// All data older than <see cref="Timespan"/> will be deleted.
        /// </summary>
        public void Pulse()
        {
            DateTime newPulse = DateTime.UtcNow;

            if (lastPulse == DateTime.MinValue)
            {
                lastPulse = newPulse;
                return;
            }
            DateTime min = newPulse - timespan;
            lock (this)
            {
                records[newPulse] = newPulse - lastPulse;
                foreach (DateTime date in records.Keys.Where(k => k < min).ToArray())
                    records.Remove(date);
            }
            lastPulse = newPulse;
        }
        /// <summary>
        /// The minimum delay recorded in the present time window, <see cref="Timespan"/>
        /// </summary>
        /// <returns></returns>
        public TimeSpan Min
        {
            get
            {
                lock (this)
                    return records.Count() > 0 ? records.Values.Min() : TimeSpan.FromSeconds(0);
            }
        }
        /// <summary>
        /// The maximum delay recorded in the present time window, <see cref="Timespan"/>
        /// </summary>
        /// <returns></returns>
        public TimeSpan Max
        {
            get
            {
                lock (this)
                    return records.Count() > 0 ? records.Values.Max() : TimeSpan.FromSeconds(0);
            }
        }
        /// <summary>
        /// The average delay recorded in the present time window, <see cref="Timespan"/>
        /// </summary>
        /// <returns></returns>
        public TimeSpan Average
        {
            get
            {
                lock (this)
                    return records.Count() > 0 ? TimeSpan.FromTicks((long)records.Values.Select(ts => ts.Ticks).Average()) : TimeSpan.FromSeconds(0);
            }
        }
        /// <summary>
        /// Number of samples accumulated in the collection.
        /// </summary>
        public IEnumerable<double> Counts
        {
            get
            {
                int total, accumulated = 0;

                TimeSpan time = TimeSpan.FromSeconds(1);
                TimeSpan[] timespans;
                lock (this)
                    timespans = records.Values.ToArray();
                List<double> retValue = new List<double>();
                if (timespans.Length > 0)
                {
                    while (accumulated < timespans.Length)
                    {
                        total = timespans.ToArray().Where(v => v < time).Count();
                        retValue.Add(100.0 * (total - accumulated) / timespans.Length );
                        accumulated = total;
                        time += TimeSpan.FromSeconds(1);
                    }
                }
                return retValue.ToArray();
            }
        }
    }

    /// <summary>
    /// JSON converter for class <see cref="DelayLogger"/>.
    /// This class will write a JSON object containing as properties <see cref="DelayLogger.Timespan"/>, <see cref="DelayLogger.Min"/>, <see cref="DelayLogger.Max"/> and <see cref="DelayLogger.Average"/>
    /// </summary>
    public class DelayLoggerConverter : Newtonsoft.Json.JsonConverter
    {
        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>true if this instance can convert the specified object type; otherwise, false.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(DelayLogger).Equals(objectType);
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="objectType"></param>
        /// <param name="existingValue"></param>
        /// <param name="serializer"></param>
        /// <returns></returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="JsonWriter"/> to write to.</param>
        /// <param name="value">The value to be srialized.</param>
        /// <param name="serializer">The calling serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is DelayLogger data)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("Timespan");
                writer.WriteValue(data.Timespan);
                writer.WritePropertyName("Delays");
                writer.WriteStartObject();
                writer.WritePropertyName("Minimum");
                writer.WriteValue(data.Min);
                writer.WritePropertyName("Average");
                writer.WriteValue(data.Average);
                writer.WritePropertyName("Maximum");
                writer.WriteValue(data.Max);
                writer.WriteEndObject();

                writer.WritePropertyName("Counts");
                writer.WriteStartArray();
                foreach (double count in data.Counts)
                    writer.WriteValue(count);
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
        }
    }
}
