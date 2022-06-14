using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RIO
{
    /// <summary>
    /// Use this class to collect decimal samples for a given amount of time. Samples occurring in the same time slot
    /// as defined by <see cref="Granularity"/> will be aggregated (added) together.
    /// </summary>
    public class TimeLogger
    {
        private readonly TimeSpan span;
        private readonly TimeSpan granularity;
        private DateTime current = DateTime.MinValue;

        private readonly int size;
        private readonly decimal[] buffer;
        private int start = 0, end = 0;

        /// <summary>
        /// This is the amount of time the logger will collect the samples. Samples older than this will pop out of the
        /// collection.
        /// </summary>
        public TimeSpan Span => span;
        /// <summary>
        /// The amount of time for which the samples are kept together.
        /// </summary>
        public TimeSpan Granularity => granularity;
        /// <summary>
        /// The size of the collection, given by the ratio <see cref="Span"/> / <see cref="Granularity"/>.
        /// </summary>
        public int Size => size;
        /// <summary>
        /// The number of samples collected, including empty slots.
        /// </summary>
        public int Count => (start == 0) ? end + 1 : size;
        /// <summary>
        /// Sum of all the samples.
        /// </summary>
        public decimal Total
        {
            get
            {
                decimal sum = 0;
                for (int i = 0; i < ((start == 0) ? end : size); i++)
                    sum += buffer[i];
                return sum;
                //Index i1 = 0, i2 = (start == 0) ? end + 1 : size - 1;
                //return buffer[i1..i2].Sum();
            }
        }
        /// <summary>
        /// Average of all the samples, including empty slots.
        /// </summary>
        public decimal Average
        {
            get
            {
                decimal avg = 0;
                for (int i = 0; i < ((start == 0) ? start : size); i++)
                    avg += (buffer[i] - avg) / (i + 1);
                return avg;
                //Index i1 = 0, i2 = (start == 0) ? end + 1 : size - 1;
                //return buffer[i1..i2].Average();
            }
        }
        /// <summary>
        /// Creates a new instance of a collection able to store decimal samples in time slots during a period.
        /// Samples occurring in the same slot will be aggregated (added); slots during which no data occurred will
        /// contain 0.
        /// </summary>
        /// <param name="span">Amount of the time to observe: samples older than this will pop out of the collection.</param>
        /// <param name="granularity">Time length of a slot: samples occurring in the same slot will be aggregated (added).</param>
        public TimeLogger(TimeSpan span, TimeSpan granularity)
        {
            this.span = span;
            this.granularity = granularity;
            size = (int)Math.Abs(Math.Round(span.TotalSeconds / granularity.TotalSeconds, MidpointRounding.AwayFromZero));
            buffer = new decimal[size];
        }
        /// <summary>
        /// Add a new sample to the collection. If a data is already present, this will be added.
        /// Empty slots will be added in case more than <see cref="Granularity"/> was passed from the last addition.
        /// </summary>
        /// <param name="data"></param>
        public void Add(decimal data)
        {
            DateTime timeReference = DateTime.UtcNow;
            if (current == DateTime.MinValue) current = timeReference;

            while (timeReference - current > granularity)
            {
                Push(0);
                current += granularity;
            }
            buffer[end] += data;
        }
        private void Push(decimal data)
        {
            buffer[end] = data;
            end = (end + 1) % size;
            if (end == start) start = (start + 1) % size;
        }
        /// <summary>
        /// For all the slot in the collection returns a <see cref="Tuple"/> containing the time reference and the value.
        /// </summary>
        /// <returns>Enumeration of <see cref="Tuple{T1, T2}"/> containing a <see cref="DateTime"/> and a <see cref="decimal"/></returns>
        public IEnumerable<Tuple<DateTime, decimal>> Data()
        {
            DateTime timeReference = current - span;
            for (int idx = 0; idx < size; idx++)
            {
                yield return new Tuple<DateTime, decimal>(timeReference, buffer[(start + idx) % size]);
                timeReference += granularity;
            }
        }
    }

    /// <summary>
    /// Used to json serialize a <see cref="TimeLogger"/>
    /// </summary>
    public class TimeLoggerConverter : JsonConverter
    {
        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>true if this instance can convert the specified object type; otherwise, false.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(TimeLogger).Equals(objectType);
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
            if (value is TimeLogger data)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("Count");
                writer.WriteValue(data.Count);
                writer.WritePropertyName("Total");
                writer.WriteValue(data.Total);
                writer.WritePropertyName("Interval");
                writer.WriteValue(data.Span);
                writer.WritePropertyName("Average");
                writer.WriteValue(data.Average);

                writer.WriteEndObject();
            }
        }
    }
}
