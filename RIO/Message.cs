using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RIO
{
    /// <summary>
    /// The RIO components use this class to exchange messages, usually through a REDIS channel.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// Gets or sets the type of the <see cref="Message"/>. Known types are:
        /// <list type="bullet">
        /// <item>shutdown</item>
        /// <item>status</item>
        /// <item>scheduler</item>
        /// <item>Execution result</item>
        /// <item>config</item>
        /// <item>enable</item>
        /// <item>disable</item>
        /// <item>start</item>
        /// <item>stop</item>
        /// <item>list</item>
        /// <item>exec</item>
        /// <item>ruleset</item>
        /// </list>
        /// </summary>
        /// <value>
        /// The <see cref="Message"/> type.
        /// </value>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the source.
        /// </summary>
        /// <value>
        /// A loose reference to the originator of the <see cref="Message"/>.
        /// </value>
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the parameters dictionary.
        /// </summary>
        /// <value>
        /// The parameters as a set of key-values.
        /// </value>
        public Dictionary<string, dynamic> Parameters { get; set; }

        /// <summary>
        /// Returns true if the message is valid, e.g. the text it comes from was succesfully parsed. Defaults to <c>false</c>.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is valid; otherwise, <c>false</c>.
        /// </value>
        [JsonIgnore]
        public bool IsValid { get; internal set; } = true;
        /// <summary>
        /// If present, it is used to relate the message with other messages having all the same identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Parses a json text to extract a <see cref="RIO.Message"/>.
        /// </summary>
        /// <param name="text">The json text.</param>
        /// <returns>The parsed <see cref="RIO.Message"/> with the <see cref="RIO.Message.IsValid"/> property set accordingly to the success of the parsing.</returns>
        public static Message ParseJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new Message() { IsValid = false };

            try
            {
                Message r = JsonConvert.DeserializeObject<Message>(text);
                r.IsValid = true;
                if (r.Parameters == null)
                    r.Parameters = new Dictionary<string, object>();
                return r;
            }
            catch (Exception ex)
            {
                Message message = new Message() { IsValid = false, Parameters = new Dictionary<string, object>() };
                message.Type = "ERROR";
                message.Parameters = new Dictionary<string, object>() { { "text", text }, { "error", ex.Message } };
                return message;
            }
        }

        /// <summary>
        /// Sets the specified key in the <see cref="RIO.Message.Parameters"/> dictionary with an array of strings.
        /// </summary>
        /// <param name="name">The key.</param>
        /// <param name="values">The values array.</param>
        public void Set(string name, string[] values)
        {
            if (Parameters == null)
                Parameters = new Dictionary<string, dynamic>();

            Parameters[name] = values;
        }

        /// <summary>
        /// Sets the specified key in the <see cref="RIO.Message.Parameters"/> dictionary with a single string value.
        /// </summary>
        /// <param name="name">The key.</param>
        /// <param name="value">The value.</param>
        public void Set(string name, string value)
        {
            if (Parameters == null)
                Parameters = new Dictionary<string, dynamic>();

            Parameters[name] = value;
        }

        /// <summary>
        /// Concatenates the values using a dot (.) and adds the specified key in the <see cref="RIO.Message.Parameters"/> dictionary.
        /// if already present as a single value, it is overwritten; in case of an array, it is appended.
        /// </summary>
        /// <param name="name">The key.</param>
        /// <param name="values">The values.</param>
        public void Add(string name, params string[] values)
        {
            if (Parameters == null)
                Parameters = new Dictionary<string, dynamic>();

            if (Parameters.ContainsKey(name))
            {
                object o = Parameters[name];
                if (o.GetType().IsArray)
                {
                    object[] a = o as object[];
                    int idx = a.Length;
                    Array.Resize(ref a, idx + values.Length);
                    foreach (string s in values)
                        a[idx++] = s;
                    //a[a.Length - 1] = string.Join(".", values);
                    Parameters[name] = a;
                }
                else
                {
                    List<string> v = new List<string>();
                    v.Add(o.ToString());
                    v.AddRange(values);
                    Parameters[name] = v.ToArray();
                }
            }
            else
            {
                Parameters[name] = values;
            }
        }

        /// <summary>
        /// Returns a Slack markdown presentation of the <see cref="RIO.Message"/>.
        /// </summary>
        /// <returns>A string containing the markdown</returns>
        public string ToMarkDown()
        {
            return IsValid
                ? string.Format("*Type*: _{0}_, *Source*: _{1}_\n*Parameters*:\n{2}", Type ?? "unset", Source ?? "unknown",
                    Parameters != null
                    ? string.Join("\n", Parameters?.Select(kv => string.Format("_{0}_ = {1}", kv.Key, ((object)kv.Value).ToMarkDown())))
                    : string.Empty)
                : "Invalid";
        }

        /// <summary>
        /// Custom to string conversion.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return IsValid
                ? string.Format("Type: {0}, Source: {1}, Parameters: {2}", Type ?? "unset", Source ?? "unknown",
                    Parameters != null
                    ? string.Join(";", Parameters?.Select(kv => string.Format("{0}={1}", kv.Key, ((object)kv.Value).ToText())))
                    : string.Empty)
                : "Invalid";
        }
        /// <summary>
        /// Performs a deep copy of the object.
        /// </summary>
        /// <returns>A valid <see cref="Message"/> containing the same information of the original one, using references
        /// to the same <see cref="Parameters"/> values.</returns>
        public Message Clone()
        {
            Message clone = new Message()
            {
                IsValid = true,
                Id = Id,
                Source = Source,
                Type = Type,
                Parameters = new Dictionary<string, dynamic>()
            };

            clone.Parameters.AddRange(Parameters.Select(kv => new KeyValuePair<string, dynamic>(kv.Key, kv.Value)));

            return clone;
        }
    }
}