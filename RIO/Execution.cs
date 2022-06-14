using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace RIO
{
    /// <summary>
    /// This class contains all the information to execute a command: the <see cref="Command"/> itself, a named reference to
    /// an object or a type to object onto which the <see cref="Command"/> is to be executed, and the parameters for the
    /// execution.
    /// </summary>
    [DebuggerDisplay("{Target}+{Command.Name}")]
    public class Execution
    {
        /// <summary>
        /// The name of an <see cref="ITask"/> or of an <see cref="IFeature"/> to perform the <see cref="Command"/> onto:
        /// in case of an <see cref="IFeature"/>, on all <see cref="ITask"/>s configured for the <see cref="IFeature"/>
        /// will be invoked the <see cref="Command"/>.
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The <see cref="Command"/> to be executed. In case of Json serialization, only the name is used.
        /// </summary>
        [JsonConverter(typeof(JsonJustPropertyConverter), "Name")]
        public Command Command { get; set; }

        /// <summary>
        /// A set of named objects to be passed to the <see cref="Command"/> for its execution.
        /// </summary>
        public Dictionary<string, dynamic> Parameters { get; set; }
    }

    /// <summary>
    /// A Json serialization attribute to use the value of a specified property (no default) to represent an object.
    /// </summary>
    public class JsonJustPropertyConverter : JsonConverter
    {
        readonly string propertyName;

        /// <summary>
        /// The only constructor requires the name of the property to be used during the Json serialization.
        /// </summary>
        /// <param name="property">The name of the property.</param>
        public JsonJustPropertyConverter(string property)
        {
            this.propertyName = property;
        }

        /// <summary>
        /// Formally, a JsonJustPropertyConverter can convert all objects, as long as the provided property name refers to
        /// an existing property.
        /// </summary>
        /// <param name="objectType">The <see cref="Type"/> of the object that will be serialized.</param>
        /// <returns></returns>
        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        /// <summary>
        /// This converter cannot revert to the original object from the value of one of its properties.
        /// </summary>
        public override bool CanRead
        {
            get { return false; }
        }

        /// <summary>
        /// Since it is not possible to obtain the original object from just the value of one of its property,
        /// this method is not implemented.
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
        /// 
        /// </summary>
        /// <param name="writer">The <see cref="JsonWriter"/> to write to.</param>
        /// <param name="value">The objecct to be serialized.</param>
        /// <param name="serializer">The calling serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Type type = value.GetType();
            FieldInfo field = type.GetField(propertyName);
            if (field != null)
            {
                writer.WriteValue(field.GetValue(value).ToString());
                return;
            }
            PropertyInfo property = type.GetProperty(propertyName);
            if (property != null)
            {
                writer.WriteValue(property.GetValue(value).ToString());
                return;
            }
            serializer.Serialize(writer, value);
        }
    }
}
