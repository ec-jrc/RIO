using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RIO
{
    /// <summary>
    /// Collection of utility functions, mainly convertions.
    /// </summary>
    public static class Extensions
    {
        private static readonly string[] booleans = new string[] { "on", "yes", "true", "1" };

        /// <summary>
        /// Provides a text representation of an object suitable to be sent on a text interface (slack, local, ...)
        /// </summary>
        /// <param name="o">Generic object</param>
        /// <param name="safe">This flag requires that string containing spaces will be quoted by the " character.</param>
        /// <returns>A string cotaining a humnan readable presentation of any object.</returns>
        public static string ToText(this object o, bool safe = false)
        {
            switch (o)
            {
                case null:
                    return string.Empty;
                case DateTime dt:
                    return safe ? string.Format("{0}{1}{0}", '"', dt.ToString()) : dt.ToString();
                case int ival:
                    return ival.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case long lval:
                    return lval.ToString();
                case float fval:
                    return fval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case double dval:
                    return dval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case decimal nval:
                    return nval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case bool bval:
                    return bval.ToString();
                case string s:
                    if (safe && s?.Contains(" ") == true)
                        return string.Format("{0}{1}{0}", '"', s);
                    else
                        return s;
                case JValue jValue:
                    return jValue.Value.ToText(safe);
                case IDictionary dict:
                    {
                        char sep = '{';
                        StringBuilder sb = new StringBuilder();
                        foreach (object key in dict.Keys)
                        {
                            object value = dict[key];
                            sb.Append(sep);
                            sb.Append(' ');
                            sb.Append(key.ToText(true));
                            sb.Append(':');
                            sb.Append(value.ToText(true));
                            sep = ',';
                        }

                        sb.Append(' ');
                        sb.Append('}');
                        return sb.ToString();
                    }
                case IEnumerable collection:
                    {
                        char sep = '[';
                        StringBuilder sb = new StringBuilder();
                        foreach (object item in collection)
                        {
                            sb.Append(sep);
                            sb.Append(' ');
                            sb.Append(item.ToText(true));
                            sep = ',';
                        }

                        sb.Append(' ');
                        sb.Append(']');
                        return sb.ToString();
                    }
                default:
                    {
                        char sep = '{';
                        StringBuilder sb = new StringBuilder();
                        if (o.GetType().IsClass)
                        {
                            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(o))
                            {
                                object value = property.GetValue(o);
                                sb.Append(sep);
                                sb.Append(property.Name);
                                sb.Append(':');
                                sb.Append(value.ToText(true));
                                sep = ',';
                            }
                            foreach (FieldInfo field in o.GetType().GetFields())
                            {
                                object value = field.GetValue(o);
                                sb.Append(sep);
                                sb.Append(field.Name);
                                sb.Append(':');
                                sb.Append(value.ToText(true));
                                sep = ',';
                            }
                        }
                        sb.Append('}');
                        return sb.ToString();
                    }
            }
        }

        private static readonly char ESC = (char)27;
        /// <summary>
        /// Converts a markdown formatted text to a text enriched by control characters handled by terminal interfaces.
        /// </summary>
        /// <param name="text">A markdown formatted text</param>
        /// <returns>A terminal control formatted text</returns>
        public static string MdToTerminal(this string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);

            bool boldToggle = false, /*italicToggle = false,  codeToggle = false,
                previousAsterisk = false,*/ previousUnderscore = false;

            foreach (char c in text)
            {
                switch (c)
                {
                    case '_':
                        {
                            if (previousUnderscore)
                            {
                                if (boldToggle)
                                {
                                    boldToggle = false;
                                    sb.Append(ESC);
                                    sb.Append('[');
                                    sb.Append('0');
                                    sb.Append('m');
                                }
                                else
                                {
                                    boldToggle = true;
                                    sb.Append(ESC);
                                    sb.Append('[');
                                    sb.Append('1');
                                    sb.Append('m');
                                }
                                previousUnderscore = false;
                            }
                            else
                                previousUnderscore = true;
                        }
                        break;
                    default:
                        {
                            if (previousUnderscore)
                            {
                                previousUnderscore = false;
                                sb.Append('_');
                                //if (italicToggle)
                                //{
                                //    italicToggle = false;
                                //    sb.Append(ESC);
                                //    sb.Append('[');
                                //    sb.Append('0');
                                //    sb.Append('m');
                                //}
                                //else
                                //{
                                //    italicToggle = true;
                                //    sb.Append(ESC);
                                //    sb.Append('[');
                                //    sb.Append('1');
                                //    sb.Append('m');
                                //}
                            }
                            sb.Append(c);
                            if (c == '\n')
                                sb.Append('\r');
                        }
                        break;
                }
            }

            sb.Append(ESC);
            sb.Append('[');
            sb.Append('0');
            sb.Append('m');
            return sb.ToString();
        }
        /// <summary>
        /// Extension method to format all objects in markdown format.
        /// </summary>
        /// <param name="o">An object to be serialized</param>
        /// <param name="indent">Initial indentation as a sequence of white spaces.</param>
        /// <returns>A markdown text.</returns>
        public static string ToMarkDown(this object o, string indent = "")
        {
            switch (o)
            {
                case null:
                    return string.Empty;
                case DateTime dt:
                    return dt.ToString("dd/MM/yyyy hh:mm:ss");
                case TimeSpan ts:
                    return ts.ToString(@"hh\:mm\:ss\.fff");
                case int ival:
                    return ival.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case long lval:
                    return lval.ToString();
                case float fval:
                    return fval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case double dval:
                    return dval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case decimal nval:
                    return nval.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case bool bval:
                    return bval.ToString();
                case string s:
                    return s;
                case IFeature feature:
                    {
                        string sep = "\n";
                        indent = string.Format("  {0}", indent);
                        StringBuilder sb = new StringBuilder();
                        sb.Append(sep); sb.Append(indent); sb.Append("* __Name__: "); sb.Append(feature.Name);
                        sb.Append(sep); sb.Append(indent); sb.Append("* __Version__: "); sb.Append(feature.Version);
                        sb.Append(sep); sb.Append(indent); sb.Append("* __Configuration__: [");
                        foreach (Property property in feature.Configuration)
                        {
                            sb.Append(sep); sb.Append("  "); sb.Append(indent);
                            sb.Append(property.Type);
                            sb.Append(' ');
                            sb.Append(property.Name);
                            sb.Append(" = ");
                            if (string.IsNullOrEmpty(property.Default))
                                sb.Append("null");
                            else
                                sb.Append(property.Default);
                        }
                        sb.Append(sep); sb.Append(indent); sb.Append(']');
                        if (feature.Commands.Any())
                        {
                            sb.Append(sep); sb.Append(indent); sb.Append("* __Commands__: ");
                            foreach (Command command in feature.Commands)
                            {
                                sb.Append(sep); sb.Append("  "); sb.Append(indent);
                                sb.Append(command.Target);
                                sb.Append('.');
                                sb.Append(command.Name);
                                if (command.Parameters.Any())
                                {
                                    char subSep = '(';
                                    foreach (Parameter parameter in command.Parameters)
                                    {
                                        sb.Append(subSep);
                                        sb.Append(sep); sb.Append("  "); sb.Append(indent);
                                        sb.Append(parameter.Type);
                                        sb.Append(' ');
                                        sb.Append(parameter.Name);
                                        if (parameter.Required)
                                            sb.Append(" mandatory");
                                        if (!string.IsNullOrWhiteSpace(parameter.Domain))
                                        {
                                            sb.Append(' ');
                                            sb.Append(parameter.Domain);
                                        }
                                        subSep = ',';
                                    }
                                    sb.Append(sep); sb.Append(indent); sb.Append(')');
                                }
                            }
                        }

                        return sb.ToString();
                    }
                case Execution execution:
                    {
                        string sep = "\n";
                        indent = string.Format("  {0}", indent);
                        StringBuilder sb = new StringBuilder();
                        sb.Append(sep); sb.Append(indent); sb.Append("* __Target__: "); sb.Append(execution.Target);
                        sb.Append(sep); sb.Append(indent); sb.Append("* __Command__: "); sb.Append(execution.Command.Name);
                        if (execution.Parameters.Count > 0)
                        {
                            sb.Append(sep); sb.Append(indent); sb.Append("* __Parameters__: ");
                            foreach (string key in execution.Parameters.Keys)
                            {
                                object value = execution.Parameters[key];
                                sb.Append(sep);
                                sb.Append(indent);
                                sb.Append("* __");
                                sb.Append(key.ToText());
                                sb.Append("__: ");
                                sb.Append(value.ToMarkDown(indent));
                            }
                        }

                        return sb.ToString();
                    }
                case IDictionary dict:
                    {
                        string sep = "\n";
                        indent = string.Format("  {0}", indent);
                        StringBuilder sb = new StringBuilder();
                        foreach (object key in dict.Keys)
                        {
                            object value = dict[key];
                            sb.Append(sep);
                            sb.Append(indent);
                            sb.Append("* __");
                            sb.Append(key.ToText());
                            sb.Append("__: ");
                            sb.Append(value.ToMarkDown(indent));
                        }

                        return sb.ToString();
                    }
                case JObject jObject:
                    {
                        string sep = "\n";
                        indent = string.Format("  {0}", indent);
                        StringBuilder sb = new StringBuilder();
                        foreach (JToken jToken in jObject.Children())
                        {
                            sb.Append(sep);
                            sb.Append(jToken.ToMarkDown(indent));
                        }

                        return sb.ToString();
                    }
                case JProperty jProperty:
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(indent);
                        sb.Append("* __");
                        sb.Append(jProperty.Name);
                        sb.Append("__: ");
                        sb.Append(jProperty.Value.ToMarkDown(indent));
                        return sb.ToString();
                    }
                case JToken jToken:
                    {
                        if (jToken is JArray)
                            return (jToken.Select(jt => jt)/*.Select(jt => (JObject)jt/*.Value<string>())*/.ToMarkDown(string.Format("  {0}", indent)));
                        return jToken.Value<string>();
                    }
                case Array array:
                    {
                        string sep = "\n";
                        StringBuilder sb = new StringBuilder();
                        sb.Append('[');
                        foreach (object item in array)
                        {
                            sb.Append(sep);
                            sb.Append(indent);
                            sb.Append("* ");
                            sb.Append(item.ToMarkDown(indent));
                        }

                        sb.Append(sep);
                        sb.Append(indent);
                        sb.Append(']');
                        return sb.ToString();
                    }
                case IEnumerable<dynamic> collection:
                    {
                        string sep = "\n";
                        StringBuilder sb = new StringBuilder();
                        sb.Append('[');
                        foreach (object item in collection)
                        {
                            sb.Append(sep);
                            sb.Append(indent);
                            sb.Append("* ");
                            sb.Append(item.ToMarkDown(indent));
                        }

                        sb.Append(sep);
                        sb.Append(indent);
                        sb.Append(']');
                        return sb.ToString();
                    }
                default:
                    {
                        string sep = "\n";
                        StringBuilder sb = new StringBuilder();
                        indent = string.Format("  {0}", indent);
                        if (o.GetType().IsClass)
                        {
                            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(o))
                            {
                                object value = property.GetValue(o);
                                sb.Append(sep);
                                sb.Append(indent);
                                sb.Append("* __");
                                sb.Append(property.Name);
                                sb.Append("__: ");
                                sb.Append(value.ToMarkDown(indent));
                            }
                            foreach (FieldInfo field in o.GetType().GetFields())
                            {
                                object value = field.GetValue(o);
                                sb.Append(sep);
                                sb.Append(indent);
                                sb.Append("* __");
                                sb.Append(field.Name);
                                sb.Append("__: ");
                                sb.Append(value.ToMarkDown(indent));
                            }
                        }
                        else
                            sb.Append(o.ToString());
                        return sb.ToString();
                    }
            }
        }
        /// <summary>
        /// Extension method that splits a string using chars from an array, but preserving substrings surrounded by the quoting charachter.
        /// </summary>
        /// <param name="text">The text to be separated.</param>
        /// <param name="splitChars">All characters that are used to separate the string parts.</param>
        /// <param name="quote">This character is used in the input text to quote the parts of the string that must
        /// be preserved and not splitted.</param>
        /// <param name="options">With this <see cref="StringSplitOptions"/> it is possible to include the empty strings from the split, it defaults to
        /// <see cref="StringSplitOptions.RemoveEmptyEntries"/>.</param>
        /// <returns>An enumeration of strings, that are the parts of the input text.</returns>
        public static IEnumerable<string> SplitQuotedRE(this string text, char[] splitChars, char quote = '"', StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries)
        {
            string escaped = string.Format("{0}{0}", quote), quoted = string.Format("{0}", quote);
            string rex = string.Format("(?:^|{0})*({1}(?:[^{1}]+|{1}{1})*{1}|[^{0}]*)", new string(splitChars), quote);
            Regex split = new Regex(rex, RegexOptions.Compiled);
            foreach (Match match in split.Matches(text))
            {
                string curr = match.Groups[0].Value;
                if (0 != curr.Length || options == StringSplitOptions.None)
                    yield return curr.TrimStart(splitChars).Trim(quote).Replace(escaped, quoted);
            }
            yield break;
        }
        /*public static IEnumerable<string> SplitQuoted(this string text, char[] splitChars, char quote = '"')
        {
            string[] parts = text.Split(splitChars);
            string escaped = string.Format("{0}{0}", quote), quoted = string.Format("{0}", quote);
            StringBuilder sb = null;
            bool quoting = false;
            foreach (string part in parts)
            {
                if (part[0] != quote)
                {
                    if (quoting)
                    {
                        sb.Append(splitChars[0]);
                        if (part[^1] == quote)
                        {
                            sb.Append(part.Substring(0, part.Length - 1));
                            yield return sb.ToString().Replace(escaped, quoted);
                            quoting = false;
                        }
                        else
                            sb.Append(part);
                    }
                    else
                    {
                        yield return part;
                        continue;
                    }
                }
                else
                {
                    if (part[^1] == quote)
                    {
                        yield return part.Substring(0, part.Length - 1).Replace(escaped, quoted);
                    }
                    else
                    {
                        sb = new StringBuilder(part.Substring(1));
                        quoting = true;
                    }
                }
            }
        }*/
        //public static ContainerConfiguration WithAssembliesInPath(this ContainerConfiguration configuration, string path, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        //{
        //    return WithAssembliesInPath(configuration, path, null, searchOption);
        //}

        //public static ContainerConfiguration WithAssembliesInPath(this ContainerConfiguration configuration, string path, AttributedModelProvider conventions, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        //{
        //    List<Assembly> assemblies = new List<Assembly>();
        //    foreach (string name in Directory
        //        .GetFiles(path, "TAD*.dll", searchOption).Union(Directory
        //        .GetFiles(path, "JRC*.dll", searchOption)).Union(Directory
        //        .GetFiles(path, "RIO*.dll", searchOption))   // HACK: Core creates a lot of not managed dll to be avoided
        //        )
        //        try
        //        {
        //            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(name);
        //            assemblies.Add(assembly);
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine("Unable to load assembly {0}, because {1}", name, ex.InnerException?.Message ?? ex.Message);
        //        }

        //    configuration = configuration.WithAssemblies(assemblies, conventions);

        //    return configuration;
        //}
        /// <summary>
        /// Converts a <see cref="Dictionary{TKey, TValue}"/> into an <see cref="ExpandoObject"/>, which will have
        /// its key-value contents as properties.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
        /// <param name="values">A <see cref="Dictionary{TKey, TValue}"/> containing the properties for the object.</param>
        /// <returns>An <see cref="ExpandoObject"/> with the properties filled by <paramref name="values"/>.</returns>
        public static ExpandoObject ToExpando<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            var expando = new System.Dynamic.ExpandoObject();
            var expandoDic = (IDictionary<string, object>)expando;
            AddRange<string, object>(expandoDic, values.Select(kv => new KeyValuePair<string, object>(kv.Key.ToString(), kv.Value)));

            return expando;
        }
        /// <summary>
        /// Extension method that turns a dictionary of string and object to an ExpandoObject
        /// </summary>
        public static ExpandoObject ToExpando(this IDictionary dictionary)
        {
            var expando = new System.Dynamic.ExpandoObject();
            var expandoDic = (IDictionary<string, object>)expando;

            // go through the items in the dictionary and copy over the key value pairs)
            foreach (DictionaryEntry kvp in dictionary)
            {
                // if the value can also be turned into an ExpandoObject, then do it!
                if (kvp.Value is IDictionary dic)
                {
                    var expandoValue = dic.ToExpando();
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
                else if (kvp.Value is DictionaryEntry entry)
                {
                    expandoDic[kvp.Key.ToString()] = entry.Value;
                }
                else
                {
                    expandoDic[kvp.ToString()] = kvp.Value.ToString();
                }
            }

            return expando;
        }
        /// <summary>
        /// Extension method to check if an <see cref="ExpandoObject"/> contains the specified property.
        /// </summary>
        /// <param name="expando">The <see cref="ExpandoObject"/> to be checked.</param>
        /// <param name="key">The name of the requested property.</param>
        /// <returns>true, if the property is present, false otherwise.</returns>
        public static bool ContainsKey(this ExpandoObject expando, string key)
        {
            return ((IDictionary<string, object>)expando).ContainsKey(key);
        }
        /// <summary>
        /// Provides a copy of a <see cref="Dictionary{TKey, TValue}"/> with the addition of other key-value
        /// entries.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys.</typeparam>
        /// <typeparam name="TValue">The type of the values.</typeparam>
        /// <param name="dictionary">Source dictionary.</param>
        /// <param name="entries">Additional entries.</param>
        /// <returns>A copy of <paramref name="dictionary"/> added with <paramref name="entries"/>.</returns>
        public static Dictionary<TKey, TValue> Copy<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, IEnumerable<KeyValuePair<TKey, TValue>> entries = null)
        {
            if (dictionary == null) return null;
            Dictionary<TKey, TValue> retValue = new Dictionary<TKey, TValue>();
            retValue.AddRange(dictionary);
            retValue.AddRange(entries);

            return retValue;
        }
        /// <summary>
        /// Provides a copy of a <see cref="Dictionary{TKey, TValue}"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys.</typeparam>
        /// <typeparam name="TValue">The type of the values.</typeparam>
        /// <param name="dictionary">Source dictionary.</param>
        /// <returns>A copy of <paramref name="dictionary"/>.</returns>
        public static Dictionary<TKey, TValue> Copy<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null) return null;
            Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>();
            result.AddRange(dictionary);

            return result;
        }
        /// <summary>
        /// Extension method to add a set of key-value entries to a <see cref="Dictionary{TKey, TValue}"/>
        /// </summary>
        /// <typeparam name="TKey">The type of the keys of the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values of the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary the <paramref name="entries"/> will be added to.</param>
        /// <param name="entries">The entries to be added to the <paramref name="dictionary"/>.</param>
        public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, IEnumerable<KeyValuePair<TKey, TValue>> entries)
        {
            if (entries == null) return;
            foreach (KeyValuePair<TKey, TValue> entry in entries)
            {
                dictionary[entry.Key] = entry.Value;
            }
        }
        /// <summary>
        /// Extension method to extract from an <see cref="ExpandoObject"/> the property called <paramref name="key"/>.
        /// </summary>
        /// <param name="expando">The <see cref="ExpandoObject"/> to query.</param>
        /// <param name="key">The name of the requested property.</param>
        /// <returns>The value associated with the requested property.</returns>
        public static object Get(this ExpandoObject expando, string key)
        {
            return ((IDictionary<string, object>)expando)[key];
        }
        /// <summary>
        /// Extension method to set the property called <paramref name="key"/> of an <see cref="ExpandoObject"/>.
        /// </summary>
        /// <param name="expando">The <see cref="ExpandoObject"/> to modify.</param>
        /// <param name="key">The name of the property.</param>
        /// <param name="value">The value to assign to the property.</param>
        public static void Set(this ExpandoObject expando, string key, object value)
        {
            ((IDictionary<string, object>)expando)[key] = value;
        }
        /// <summary>
        /// Extension method to convert inot a decimal every object that may be converted into a text describing a number.
        /// </summary>
        /// <param name="obin">A text representing a number.</param>
        /// <returns>The converted numeric value.</returns>
        public static decimal ToDecimal(this object obin)
        {
            string StringSeparator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            if (StringSeparator == ",")
                return Convert.ToDecimal(obin.ToString().Replace(".", ","));
            else
                return Convert.ToDecimal(obin);
        }

        /*
        public static int GetSize(this RedisResponse response)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Status:
                    return response.AsStatus().Length;
                case ResponseType.Error:
                    return response.AsError().Length;
                case ResponseType.Integer:
                    return sizeof(int);
                case ResponseType.Bulk:
                    {
                        Bulk bulk = response.AsBulk();
                        return (bulk.IsNull) ? 0 : bulk.Length;
                    }
                case ResponseType.MultiBulk:
                    {
                        MultiBulk bulk = response.AsMultiBulk();
                        return (bulk.IsNull) ? 0 : bulk.Length;
                    }
            }
            return 0;
        }*/
        /// <summary>
        /// Extension method to parse a text into a <see cref="DateTime"/> value.
        /// </summary>
        /// <param name="s">The text representing a time reference.</param>
        /// <returns>The string <paramref name="s"/> parsed into a <see cref="DateTime"/> value.</returns>
        public static DateTime ToDateTime(this string s)
        {
            if (s.ToDateTime(out DateTime result))
                return result;
            return DateTime.MinValue;
        }
        /// <summary>
        /// Extension method to parse a text into a <see cref="DateTime"/> value.
        /// </summary>
        /// <param name="s">The text representing a time reference.</param>
        /// <param name="result">The string <paramref name="s"/> parsed into a <see cref="DateTime"/> value.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToDateTime(this string s, out DateTime result)
        {
            return DateTime.TryParse(s, out result);
        }
        /// <summary>
        /// Extension method to parse a text into a <see cref="DateTime"/> value after fixing the date and time
        /// parts separators.
        /// </summary>
        /// <param name="s">The text representing a time reference.</param>
        /// <param name="result">The string <paramref name="s"/> parsed into a <see cref="DateTime"/> value.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToDateTag(this string s, out DateTime result)
        {
            result = new DateTime(0);
            if (s.Length != 14)
                return false;
            s = s.Substring(0, 4) + "/" + s.Substring(4, 2) + "/" + s.Substring(6, 2) +
                " " + s.Substring(8, 2) + ":" + s.Substring(10, 2) + ":" + s.Substring(12, 2);
            return s.ToDateTime(out result);
        }
        /// <summary>
        /// Converts the string representation of a number to its 32-bit signed integer equivalent.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="result">
        /// When this method returns, contains the 32-bit signed integer value equivalent
        /// of the number contained in s, if the conversion succeeded, or zero if the conversion
        /// failed. The conversion fails if the s parameter is null or <see cref="System.String.Empty"/>,
        /// is not of the correct format, or represents a number less than <see cref="System.Int32.MinValue"/>
        /// or greater than <see cref="System.Int32.MaxValue"/>. This parameter is passed uninitialized;
        /// any value originally supplied in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToInt(this string s, out int result)
        {
            return int.TryParse(s, out result);
        }
        /// <summary>
        /// Converts the string representation of a number to its 64-bit signed integer equivalent.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="result">
        /// When this method returns, contains the 64-bit signed integer value equivalent
        /// of the number contained in s, if the conversion succeeded, or zero if the conversion
        /// failed. The conversion fails if the s parameter is null or <see cref="System.String.Empty"/>,
        /// is not of the correct format, or represents a number less than <see cref="System.Int64.MinValue"/>
        /// or greater than <see cref="System.Int64.MaxValue"/>. This parameter is passed uninitialized;
        /// any value originally supplied in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToLong(this string s, out long result)
        {
            return long.TryParse(s, out result);
        }
        /// <summary>
        /// Extension method to parse a boolean value, strictly limited to true and false.
        /// </summary>
        /// <param name="s">A text describing a boolean value.</param>
        /// <param name="result">A boolean value as described in the text.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        /// <seealso cref="ToBoolExt(string, out bool)"/>
        public static bool ToBool(this string s, out bool result)
        {
            return bool.TryParse(s, out result);
        }
        /// <summary>
        /// Extension method to recognize as boolean true all text case-insensitive equals to on, yes, true or 1.
        /// every other text will be converted to false.
        /// </summary>
        /// <param name="s">A text describing a boolean value.</param>
        /// <param name="result">A boolean false, if the text is not in the list of the true values.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        /// <seealso cref="ToBool(string, out bool)"/>
        public static bool ToBoolExt(this string s, out bool result)
        {
            result = booleans.FirstOrDefault(b => b.Equals(s.ToLower())) != null;
            return true;
        }
        /// <summary>
        /// Converts the string representation of a number to its single-precision floating-point number equivalent.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="result">
        /// When this method returns, contains a single-precision floating-point number equivalent
        /// of the numeric value or symbol contained in s, if the conversion succeeded, or
        /// zero if the conversion failed. The conversion fails if the s parameter is null
        /// or <see cref="System.String.Empty"/>, represents a
        /// number less than System.SByte.MinValue or greater than System.SByte.MaxValue.
        /// This parameter is passed uninitialized; any value originally supplied
        /// in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToFloat(this string s, out float result)
        {
            return float.TryParse(s, out result);
        }
        /// <summary>
        /// Converts the string representation of a number in a specified style and culture-specific
        /// format to its single-precision floating-point number equivalent. A return value
        /// indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="style">
        /// A bitwise combination of System.Globalization.NumberStyles values that indicates
        /// the permitted format of s. A typical value to specify is <see cref="System.Globalization.NumberStyles.Float"/>
        /// combined with <see cref="System.Globalization.NumberStyles.AllowThousands"/>.
        /// </param>
        /// <param name="provider">An <see cref="System.IFormatProvider"/> that supplies culture-specific formatting information about s.</param>
        /// <param name="result">
        /// When this method returns, contains a single-precision floating-point number equivalent
        /// of the numeric value or symbol contained in s, if the conversion succeeded, or
        /// zero if the conversion failed. The conversion fails if the s parameter is null
        /// or <see cref="System.String.Empty"/>, is not in a format compliant with style, represents a
        /// number less than <see cref="System.SByte.MinValue"/> or greater than <see cref="System.SByte.MaxValue"/>,
        /// or if style is not a valid combination of <see cref="System.Globalization.NumberStyles"/> enumerated
        /// constants. This parameter is passed uninitialized; any value originally supplied
        /// in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToFloat(this string s, NumberStyles style, IFormatProvider provider, out float result)
        {
            return float.TryParse(s, style, provider, out result);
        }
        /// <summary>
        /// Converts the string representation of a number to its double-precision floating-point number equivalent.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="result">
        /// When this method returns, contains a double-precision floating-point number equivalent
        /// of the numeric value or symbol contained in s, if the conversion succeeded, or
        /// zero if the conversion failed. The conversion fails if the s parameter is null
        /// or <see cref="System.String.Empty"/>, represents a
        /// number less than <see cref="System.SByte.MinValue"/> or greater than <see cref="System.SByte.MaxValue"/>.
        /// This parameter is passed uninitialized; any value originally supplied
        /// in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToDouble(this string s, out double result)
        {
            return Double.TryParse(s, out result);
        }
        /// <summary>
        /// Converts a string containing a coordinate as DMS into decimal format.
        /// </summary>
        /// <param name="s">A DMS formatted coordinate.</param>
        /// <param name="result">
        /// When this method returns, contains a double-precision floating-point where the integer part is equals to
        /// the degree and the decimal part to the combination of minutes and seconds.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToCoordinate(this string s, out double result)
        {
            if (s.IndexOf('°') < 0)
            {
                return Double.TryParse(s, out result);
            }
            else
            {
                result = 0;
                string[] parts = s.Split('°');
                if (parts[0].ToInt(out int intPart))
                {
                    string frac = (parts[1] + "00000").Substring(0, 5);
                    if (frac.Substring(0, 2).ToInt(out int mins) && frac.Substring(2).ToInt(out int dsecs))
                    {
                        result = intPart + mins / 60F + dsecs / 36000F;
                        return true;
                    }
                }
                return false;
            }
        }
        /// <summary>
        /// Converts the string representation of a number in a specified style and culture-specific
        /// format to its double-precision floating-point number equivalent. A return value
        /// indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">A string containing a number to convert.</param>
        /// <param name="style">
        /// A bitwise combination of System.Globalization.NumberStyles values that indicates
        /// the permitted format of s. A typical value to specify is <see cref="System.Globalization.NumberStyles.Float"/>
        /// combined with <see cref="System.Globalization.NumberStyles.AllowThousands"/>.
        /// </param>
        /// <param name="provider">An <see cref="System.IFormatProvider"/> that supplies culture-specific formatting information about s.</param>
        /// <param name="result">
        /// When this method returns, contains a double-precision floating-point number equivalent
        /// of the numeric value or symbol contained in s, if the conversion succeeded, or
        /// zero if the conversion failed. The conversion fails if the s parameter is null
        /// or <see cref="System.String.Empty"/>, is not in a format compliant with style, represents a
        /// number less than <see cref="System.SByte.MinValue"/> or greater than <see cref="System.SByte.MaxValue"/>,
        /// or if style is not a valid combination of <see cref="System.Globalization.NumberStyles"/> enumerated
        /// constants. This parameter is passed uninitialized; any value originally supplied
        /// in result will be overwritten.
        /// </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool ToDouble(this string s, NumberStyles style, IFormatProvider provider, out double result)
        {
            return Double.TryParse(s, style, provider, out result);
        }
        /// <summary>
        /// Try to parse a string to recognize a Day<see cref="DayOfWeek"/> of the <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
        /// in long or short (3 letters) form.
        /// </summary>
        /// <param name="s">The name of the day.</param>
        /// <param name="ignoreCase">This flag requires the parsing to be case-insensitive, when true.</param>
        /// <param name="day">The variable where to store the result.</param>
        /// <returns>true if the parsing was successful, false otherwise</returns>
        public static bool TryToDayOfWeek(this string s, bool ignoreCase, out DayOfWeek day)
        {
            if (Enum.TryParse<DayOfWeek>(s, true, out day))
                return true;
            for (DayOfWeek d = DayOfWeek.Sunday; d < DayOfWeek.Saturday; d++)
            {
                if (s.Equals(d.ToString().Substring(0, 3),
                    ignoreCase ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture))
                {
                    day = d;
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Enumerate all the properties as dynamic objects with Name and Value of each property as their properties.
        /// </summary>
        /// <param name="o">A generic object, whose properties will be enumerated.</param>
        /// <returns>An enumeration of objects with two properties: Name, a string, and Value, an object.</returns>
        public static IEnumerable<dynamic> AllProperties(this object o)
        {
            switch (o)
            {
                case ExpandoObject expo:
                    {
                        var expandoDic = (IDictionary<string, object>)expo;
                        foreach (string Name in expandoDic.Keys)
                        {
                            yield return new
                            { Name, Value = expandoDic[Name] };
                        }
                    }
                    break;
                case JObject jObj:
                    {
                        foreach (JProperty token in jObj.Children())
                        {
                            yield return new { token.Name, token.Value };
                        }
                    }
                    break;
                default:
                    {
                        var properties = o.GetType().GetProperties();

                        foreach (var prop in properties)
                        {
                            yield return new
                            { prop.Name, Value = prop.GetValue(o, null) };
                        }
                    }
                    break;
            }
        }
        /// <summary>
        /// Returns a copy of a string where, according to a substitution map, all occurrences of thekeys are replaced
        /// with the values. The process is repeated as long as there is at least one substitution to perform.
        /// </summary>
        /// <param name="template"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static string Substitute(this string template, dynamic parameters)
        {
            int count = 1;
            while (count > 0)
            {
                count = 0;
                IDictionary<string, object> map = parameters as IDictionary<string, object>;
                foreach (string key in map.Keys)
                {
                    string s = template.Replace($"${key}",
                                                map[key]?.ToString());
                    if (!s.Equals(template)) count++;
                    template = s;
                }
            }
            return template;
        }
    }
}