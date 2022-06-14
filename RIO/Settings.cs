using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Parameters = System.Collections.Generic.Dictionary<string, dynamic>;

namespace RIO
{
    /// <summary>
    /// This class is used to maintain the configuration of the RIO: it includes a section for all module and a generic
    /// one about the device itself. Extending <see cref="DataModel"/>, it implements <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    public class Settings : DataModel
    {
        string id = "RIO-Uninitialized_device";
        string queueEndPoint = string.Empty;
        string queueCredentials = string.Empty;
        string webAccess = "http://webcritech.jrc.ec.europa.eu/TAD_server/api/Data/PostAsync";
        string webProxy = null;
        /// <summary>
        /// The identifier of the device, to be used in all communication. It will be exposed also as <see cref="Manager.Id"/>
        /// </summary>
        public string Id { get => id; set => Set<string>(ref id, value); }
        /// <summary>
        /// The list of <see cref="Feature"/>s that configures the modules availabe in the device.
        /// </summary>
        public List<Feature> Features { get; set; } = new List<Feature>();
        /// <summary>
        /// The connection string to a REDIS instance, used to connect to a pubsub service.
        /// </summary>
        public string Queue { get => queueEndPoint; set => queueEndPoint = value; }
        /// <summary>
        /// The password to be supplied in the REDIS connection string defined in <see cref="Queue"/>.
        /// </summary>
        public string QueueCredentials { get => queueCredentials; set => queueCredentials = value; }
        /// <summary>
        /// URL of the endpoint to post telemetry data to.
        /// </summary>
        public string WebAccess { get => webAccess; set => webAccess = value; }
        /// <summary>
        /// Enables or disables the local management interactive interface available by connecting with telnet
        /// to port 4005.
        /// </summary>
        public bool LocalManagement { get; set; } = true;
        /// <summary>
        /// If true, the debug information are sent through the Management Channel in a 
        /// <see cref="Message"/> with Type set to "debug".
        /// </summary>
        public bool EnableRemoteDebug { get; set; }

        /// <summary>
        /// Enables or disables the Slack reporting channel reached by the token defined in <see cref="SlackToken"/>.
        /// </summary>
        public bool EnableSlack { get; set; }
        /// <summary>
        /// The tooken to access a Slack channel to report to.
        /// <seealso cref="EnableSlack"/>
        /// </summary>
        public string SlackToken { get; set; }
        /// <summary>
        /// Define here the URL of the proxy to use for WWW navigation, including the credentials, if needed.
        /// </summary>
        public string WebProxy { get => webProxy; set => webProxy = value; }
        /// <summary>
        /// This propriety is used to store stistically the position where the device is installed in the format
        /// Latitude,Longitude
        /// </summary>
        public string Location { get; set; }
        /// <summary>
        /// With this method users of the Settings class can raise the <see cref="INotifyPropertyChanged"/> related event
        /// </summary>
        /// <param name="propertyName">The name of the property modified.</param>
        public void OnChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }

    /// <summary>
    /// Through this class, it is possible to define the configuration of a module: its properties will
    /// be used to configure the <see cref="ITask"/> that will run the software for the related functionality.
    /// </summary>
    [System.Diagnostics.DebuggerDisplay("{Type}:{Id}, {Enabled?\"Enabled\":\"Disabled\"}")]
    public class Feature
    {
        /// <summary>
        /// If false, the configuration is preserved and managed, but not used to instanciate an <see cref="ITask"/>.
        /// If true, the RIO will try to set-up and run the related <see cref="ITask"/>.
        /// </summary>
        public bool Enabled { get; set; }
        /// <summary>
        /// The Id is used to address the specific feature and the related <see cref="ITask"/> for internal RIO commands.
        /// It allows having more Features of the same type configured at the same moment, e.g. if two sensors of the same
        /// type are attached to the device, two different features with different parameters can be configured to manage
        /// both.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// The name of the module to be used to activate this <see cref="Feature"/>, according to the
        /// <see cref="IFeature.Name"/> declared in the module.
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// The set of the parameters to be used to configure the <see cref="ITask"/>, that will perform
        /// the activities for this feature.
        /// </summary>
        public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();
        /// <summary>
        /// An useful information to understand which version of the module is running.
        /// </summary>
        public string Version { get; internal set; }
        /// <summary>
        /// Use this function to retrieve a setting as a string from the <see cref="Properties"/>.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <returns>The value of the property converted to string.
        /// If not found, a <see cref="String.Empty"/>.
        /// </returns>
        public string GetString(string name) { return Properties.ContainsKey(name) ? Properties[name].ToString() : string.Empty; }
        /// <summary>
        /// Use this function to retrieve a setting as a string from the <see cref="Properties"/> and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a 
        /// string; if not found, <see cref="string.Empty"/>.</param>
        /// <returns>The value of the property converted to string.
        /// If not found, a <see cref="string.Empty"/>.
        /// </returns>
        public bool GetString(string name, out string result)
        {
            if (Properties.ContainsKey(name))
            {
                result = Properties[name].ToString();
                return true;
            }
            result = string.Empty;
            return false;
        }
        /// <summary>
        /// Use this function to retrieve a setting as a string from the <see cref="Properties"/> and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a 
        /// string; if not found, defaultValue.</param>
        /// <param name="defaultValue">The value to be assigned to result, if the setting is not available.</param>
        /// <returns>The value of the property converted to string.
        /// If not found, it will contain the defaultValue.
        /// </returns>
        public void GetString(string name, out string result, string defaultValue)
        {
            if (Properties.ContainsKey(name))
                result = Properties[name].ToString();
            else
                result = defaultValue;
        }
        /// <summary>
        /// Use this function to retrieve a setting as a string array from the <see cref="Properties"/>.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <returns>
        /// If it is a string array, it is returned as-is.
        /// If it is a string in the form "[item,item,...]", it is parsed and returned as string array.
        /// In any other case, an empty array of string will be returned.
        /// </returns>
        public string[] GetStringArray(string name)
        {
            if (!Properties.ContainsKey(name)) return Array.Empty<string>();
            string value = Properties[name].ToString();
            if (value.StartsWith("["))
                return value.TrimStart('[').TrimEnd(']').SplitQuotedRE(new char[] { ',' }).ToArray();

            return (Properties[name] as string[]) ?? Array.Empty<string>();
        }
        private dynamic ParseParameter(dynamic obj)
        {
            switch (obj)
            {
                case JArray jArray when jArray.Count == 1:
                    return ParseParameter(jArray[0].Value<string>());
                case string value:
                    {
                        //string value = Properties[name].ToString();
                        if (value.StartsWith("("))
                        {
                            string[] values = value.TrimStart('(').TrimEnd(')').SplitQuotedRE(new char[] { ',' }).ToArray();
                            Parameters retValue = new Parameters();
                            retValue.AddRange<string, dynamic>(values.Select<string, KeyValuePair<string, dynamic>>(s =>
                            {
                                string[] parts = s.Trim().Split(":".ToCharArray(), 2);
                                return parts.Length == 2 ? new KeyValuePair<string, dynamic>(parts[0].Trim(), parts[1]) : new KeyValuePair<string, dynamic>(parts[0].Trim(), string.Empty);
                            }));
                            return retValue;
                        }
                        else
                            return value;
                    }
                case IDictionary dictionary:
                    {
                        Parameters subValue = new Parameters();
                        foreach (dynamic item in dictionary)
                        {
                            subValue.Add(item.Key, ParseParameter(item.Value));
                        }
                        return subValue;
                    }
                case IEnumerable collection:
                    {
                        Parameters subValue = new Parameters();
                        foreach (dynamic item in collection)
                        {
                            subValue.Add(item.Name, ParseParameter(item.Value.Value));
                        }
                        return subValue;
                    }
                default:
                    return obj.ToString();
            }
        }
        /// <summary>
        /// Use this function to retrieve a setting as an array of Dictionaries, used to store complex configurations.
        /// <see cref="Parameters"/> is defined as <![CDATA[System.Collections.Generic.Dictionary<string, dynamic>]]>.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <returns>A collection of Dictionaries, each representing a complex configuration.</returns>
        public Parameters[] GetParametersArray(string name)
        {
            if (!Properties.ContainsKey(name)) return Array.Empty<Parameters>();

            if (Properties[name] is string value)
            {
                List<Parameters> retValue = new List<Parameters>();
                string[] values = (value.StartsWith("((")) ?
                    values = value.Substring(1, value.Length - 2).SplitQuotedRE(new char[] { '(', ',', ')' }).ToArray() :
                    values = new string[] { string.Format("{0} : {1}", name, value) };
                foreach (string dic in values)
                {
                    Parameters subValue = new Parameters();
                    subValue.AddRange<string, dynamic>(values.Select<string, KeyValuePair<string, dynamic>>(s =>
                    {
                        string[] parts = s.Trim().Split(":".ToCharArray(), 2);
                        return parts.Length == 2 ? new KeyValuePair<string, dynamic>(parts[0].Trim(), parts[1]) : new KeyValuePair<string, dynamic>(parts[0].Trim(), string.Empty);
                    }));
                    retValue.Add(subValue);
                }
                return retValue.ToArray();
            }
            if (Properties[name] is IEnumerable collection)
            {
                List<Parameters> retValue = new List<Parameters>();
                foreach (dynamic item in collection)
                    retValue.Add(ParseParameter(item));

                return retValue.ToArray();
            }
            else
            {
                ;
            }

            return Array.Empty<Parameters>();
        }
        /// <summary>
        /// Use this function to retrieve a setting as a Dictionary, used to store complex configurations.
        /// <see cref="Parameters"/> is defined as <![CDATA[System.Collections.Generic.Dictionary<string, dynamic>]]>.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <returns>A Dictionary that maps labels to polymorphic values.</returns>
        public Parameters GetParameters(string name)
        {
            Parameters retValue = new Parameters();
            if (!Properties.ContainsKey(name)) return retValue;
            else return ParseParameter(Properties[name]);
        }
        /// <summary>
        /// The function tries to extract a setting, using it as an integer value and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as an
        /// integer; if not found, 0.</param>
        /// <returns>true, if the setting is found and correctly parsed; false, otherwise.</returns>
        public bool GetInt(string name, out int result)
        {
            if (Properties.ContainsKey(name))
                return int.TryParse(Properties[name].ToString(), out result);
            result = 0;
            return false;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as an integer value and
        /// put it into a supplied variable. If not available, the default value is assigned.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as an
        /// integer; if not found, defaultValue.</param>
        /// <param name="defaultValue">The value to be assigned to result, if the setting is not available.</param>
        public void GetInt(string name, out int result, int defaultValue)
        {
            if (Properties.ContainsKey(name))
                if (int.TryParse(Properties[name].ToString(), out result))
                    return;
            result = defaultValue;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as an integer. If not available, the default value is returned.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="defaultValue">The value to be returned, if the setting is not available.</param>
        /// <returns>The value of the setting as an
        /// integer; if not found, defaultValue.</returns>
        public int GetInt(string name, int defaultValue)
        {
            if (Properties.ContainsKey(name))
                if (int.TryParse(Properties[name].ToString(), out int result))
                    return result;
            return defaultValue;
        }
        /// <summary>
        /// Use this function to retrieve a setting as an array of integers from the <see cref="Properties"/>.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <returns>
        /// In not found, an empty array of Integers will be returned.
        /// If it is a string array, each element is parsed to integer, or -1, if not converted.
        /// If it is a string in the form "[item,item,...]", it is parsed and returned as an array of Integers.
        /// </returns>
        public int[] GetIntArray(string name)
        {
            if (!Properties.ContainsKey(name)) return Array.Empty<int>();
            string value = Properties[name].ToString();
            string[] values;
            if (value.StartsWith("["))
                values = value.TrimStart('[').TrimEnd(']').SplitQuotedRE(new char[] { ',' }).ToArray();
            else values = (Properties[name] as string[]) ?? Array.Empty<string>();

            return values.Select<string, int>(s => int.TryParse(s, out int v) ? v : -1).ToArray();
        }
        /// <summary>
        /// The function tries to extract a setting, using it as a boolean value and
        /// put it into a supplied variable. Every string matching case-insensitive with one of the values
        /// "on", "yes", "true" or "1" will evaluate to true. Any other text, to false.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a
        /// boolean; false, if not found.</param>
        /// <returns>true, if the setting is found and parsed; false, if not available.</returns>
        public bool GetBoolean(string name, out bool result)
        {
            return Properties.ContainsKey(name)
                ? Properties[name].ToString().ToBoolExt(out result)
                : result = false;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as a boolean value and
        /// put it into a supplied variable. If not found, a default value will be used.
        /// Every string matching case-insensitive with one of the values
        /// "on", "yes", "true" or "1" will evaluate to true. Any other text, to false.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a
        /// boolean; defaultValue, if not found.</param>
        /// <param name="defaultValue">The value to be assigned to result, if the setting is not available.</param>
        public void GetBoolean(string name, out bool result, bool defaultValue)
        {
            if (Properties.ContainsKey(name))
                if (Properties[name].ToString().ToBoolExt(out result))
                    return;
            result = defaultValue;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as a float value and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a
        /// float; if not found, 0.</param>
        /// <returns>true, if the setting is found and correctly parsed; false, otherwise.</returns>
        public bool GetFloat(string name, out float result)
        {
            if (Properties.ContainsKey(name))
                return float.TryParse(Properties[name].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.DefaultThreadCurrentCulture, out result);
            result = 0;
            return false;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as a float value and
        /// put it into a supplied variable, using a default value, if not found.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a
        /// float; if not found, defaultValue.</param>
        /// <param name="defaultValue">The value to be returned, if the setting is not available.</param>
        public void GetFloat(string name, out float result, float defaultValue)
        {
            if (Properties.ContainsKey(name))
                if (float.TryParse(Properties[name].ToString(), out result))
                    return;
            result = defaultValue;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as an integer value in hexadecimal format and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as a
        /// float; if not found, 0.</param>
        /// <returns>true, if the setting is found and correctly parsed; false, otherwise.</returns>
        public bool GetHex(string name, out int result)
        {
            if (Properties.ContainsKey(name))
                return int.TryParse(Properties[name].ToString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out result);
            result = 0;
            return false;
        }
        /// <summary>
        /// The function tries to extract a setting, using it as an integer value in hexadecimal format and
        /// put it into a supplied variable.
        /// </summary>
        /// <param name="name">The name of the requested setting.</param>
        /// <param name="result">When this method returns, it contains the value of the setting as an
        /// integer; if not found, defaultValue.</param>
        /// <param name="defaultValue">The value to be assigned to result, if the setting is not available.</param>
        public void GetHex(string name, out int result, int defaultValue)
        {
            if (Properties.ContainsKey(name))
                if (int.TryParse(Properties[name].ToString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out result))
                    return;
            result = defaultValue;
        }
    }

    /// <summary>
    /// Custom serializer for <see cref="Dictionary{TKey, TValue}"/>. Instead of default serialization,
    /// it serializes the dictionary between parenthesis, in the form of key:value,key:value,...
    /// In case the value contains commas, it is surrounded by quotes.
    /// </summary>
    /// <inheritdoc/>
    public class FeatureDictionaryConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(Dictionary<string, string>).Equals(objectType);
        }
        /// <summary>
        /// It is not required, since the text is deserialized by the <see cref="Settings"/> class itself.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Writes the <see cref="Dictionary{TKey, TValue}"/>. Instead of default serialization,
        /// it serializes the dictionary between parenthesis, in the form of key:value,key:value,...
        /// In case the value contains commas, it is surrounded by quotes.
        /// </summary>
        /// <param name="writer">The <see cref="JsonWriter"/> supplied by the serialization framework.</param>
        /// <param name="value">A Dictionary&lt;string, object&gt;</param>
        /// <param name="serializer">The <see cref="JsonSerializer"/> performing the serialization.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Dictionary<string, object> parameters)
            {
                String sep = "( ";
                StringBuilder sb = new StringBuilder();
                foreach (string key in parameters.Keys)
                {
                    sb.Append(sep);
                    sb.Append(key);
                    sb.Append(" : ");
                    string s = parameters[key].ToString();
                    if (s.Contains(','))
                    {
                        sb.Append("\"\"");
                        sb.Append(s);
                        sb.Append("\"\"");
                    }
                    else
                        sb.Append(s);
                    sep = ", ";
                }
                sb.Append(')');

                writer.WriteValue(sb.ToString());
            }
        }
    }
}
