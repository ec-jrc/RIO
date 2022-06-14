using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// <see cref="ITask"/>s can perform actions based on request by the user or by the <see cref="RuleEngine"/>.
    /// These actions must be defined in the related <see cref="IFeature"/> and invoked providing the <see cref="ITask"/>
    /// and a set of parameters.
    /// </summary>
    public abstract class Command
    {
        private static readonly Regex extract = new Regex(@"array\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly string[] booleans = new string[] { "on", "yes", "true", "1" };
        /// <summary>
        /// The type of <see cref="ITask"/> that can perform the action, i.e. the <see cref="IFeature"/> defining it.
        /// </summary>
        public string Target { get; protected set; }
        /// <summary>
        /// The name of this action, as referred in <see cref="Rule"/>s or in the requests from the users.
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// Use this method to invoke the action to perform on the <see cref="ITask"/>, using a <see cref="Manager"/> as the
        /// context and a set of parameters, and returning some result in a <see cref="Message"/>.
        /// </summary>
        /// <param name="instance">The <see cref="ITask"/> to refer to in order to perform the action: think of it as
        /// the <c>this</c> of a method.</param>
        /// <param name="manager">The <see cref="Manager"/> is the environment, offers the general <see cref="Settings"/>
        /// as well as executing <see cref="Command"/>s on other <see cref="ITask"/>s.</param>
        /// <param name="response">A <see cref="Message"/> to be used to report results and errors.</param>
        /// <param name="parameters">A name value dictionary containing the parameters to perform the action.
        /// Required parameters must be present; otherwise, the execution will throw.</param>
        /// <returns>A content object reporting some information.</returns>
        public object Execute(ITask instance, Manager manager, Message response, Dictionary<string, dynamic> parameters)
        {
            dynamic input = Parse(parameters);
            Dictionary<string, dynamic> command = new Dictionary<string, dynamic>();
            Extensions.AddRange(command, ((IDictionary<string, object>)input).Select(kv => new KeyValuePair<string, dynamic>(kv.Key, kv.Value)));
            command["target"] = instance?.Name ?? Manager.Id;
            command["action"] = Name;

            response.Parameters = new Dictionary<string, dynamic>
            {
                ["command"] = command
            };
            //Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (dynamic parameter in Parameters)
            {
                //if (parameters.ContainsKey(parameter.Name))
                //{
                //    //  Check Type
                //    values[parameter.Name] = parameters[parameter.Name];
                //}
                //else if (parameter.Required)
                if (!parameters.ContainsKey(parameter.Name) && parameter.Required)
                    throw new ArgumentException("Required parameter missing", parameter.Name);   // Mandatory parameters cannot be missing
            }
            if (parameters.ContainsKey("delay"))
            {
                object value = parameters["delay"];
                if (int.TryParse(value.ToString(), out int delay) && delay > 0)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            Thread.Sleep(delay);
                            Run(instance, manager, response, input);
                            response.Add("Execution Result", "error", "none");
                        }
                        catch (Exception ex)
                        {
                            response.Add("Execution Result", "error", ex.Message);
                        }
                        response.Add("Execution Result", "execution", string.Format("{0} delayed {1} ms on {2} executed", Name, delay, instance.Name));
                        Manager.OnNotify(instance.Name, response);
                    });
                    response.Add("Execution Result", "execution", string.Format("{0} delayed {1} ms on {2} scheduled", Name, delay, instance.Name));
                    return string.Format("{0} delayed {1} ms on {2}", Name, delay, instance.Name);
                }
            }
            return Run(instance, manager, response, input);
        }

        /// <summary>
        /// Implement this method to provide the business logic of your <see cref="Command"/>.
        /// </summary>
        /// <param name="instance">The <see cref="ITask"/> to refer to in order to perform the action: think of it as
        /// the <c>this</c> of a method.</param>
        /// <param name="manager">The <see cref="Manager"/> is the environment, offers the general <see cref="Settings"/>
        /// as well as executing <see cref="Command"/>s on other <see cref="ITask"/>s.</param>
        /// <param name="response">A <see cref="Message"/> to be used to report results and errors.</param>
        /// <param name="parameters">A <c>dynamic</c> object: its properties map the provided parameters.</param>
        /// <returns></returns>
        protected abstract object Run(ITask instance, Manager manager, Message response, dynamic parameters = null);

        /// <summary>
        /// Implement this with the list of <see cref="Parameter"/>s used by the <see cref="Command"/>.
        /// </summary>
        public abstract IEnumerable<Parameter> Parameters { get; }

        private dynamic Parse(Dictionary<string, dynamic> parameters)
        {
            ExpandoObject context = new ExpandoObject();
            IDictionary<string, object> expandoDic = context;

            foreach (Parameter parameter in Parameters)
            {
                if (parameter.Type.Equals("*"))
                {
                    Dictionary<string, string> freeParams = new Dictionary<string, string>();
                    foreach (string key in parameters.Keys)
                    {
                        if (Parameters.Any(p => p.Name.Equals(key))) continue;
                        expandoDic[key] = ParseValue(parameters[key]);
                        freeParams[key] = parameters[key].ToString();
                    }
                    expandoDic[parameter.Name] = freeParams;
                    continue;
                }
                if (parameters.ContainsKey(parameter.Name) && parameters[parameter.Name] != null)
                {
                    expandoDic[parameter.Name] = Parse(parameter, parameters[parameter.Name]);
                    continue;
                }

                if (parameter.Required)
                    throw new ArgumentException("Missing required parameter", parameter.Name);
                else
                    expandoDic[parameter.Name] = null;
            }
            return context;
        }

        private object ParseValue(dynamic value)
        {
            return value;
        }
        /// <summary>
        /// Parses an unknown object into the type according to the definition provided by <paramref name="parameter"/>
        /// </summary>
        /// <param name="parameter">Definition of the parameter</param>
        /// <param name="dynamic">The provided value</param>
        /// <returns>An object of the type required by <paramref name="parameter"/></returns>
        public static dynamic Parse(Parameter parameter, dynamic dynamic)
        {
            Match match = extract.Match(parameter.Type);
            if (match.Success)
            {
                string elementType = match.Groups[1].Value;
                string[] values = dynamic as string[];
                if (values == null && dynamic is object[] oa)
                    values = oa.Select(o => o.ToString()).ToArray();
                if (values == null && dynamic is Newtonsoft.Json.Linq.JArray array)
                    values = array.ToObject<string[]>();
                if (values == null && (dynamic.ToString().StartsWith("(") || dynamic.ToString().StartsWith("[")))
                {
                    string v = dynamic.ToString().Trim('(', ')', '[', ']', '\r', '\n');
                    List<string> vals = new List<string>();
                    values = v.SplitQuotedRE(new char[] { ' ', ',' }).Where(t => !"\r\n".Equals(t)).ToArray();
                }
                if ((values == null || values.Length == 0) && !string.IsNullOrWhiteSpace(dynamic.ToString()))
                    values = new string[] { dynamic.ToString() };
                switch (elementType)
                {
                    case "int":
                        return values.Select<string, Int64>(s => Int64.TryParse(s.ToString(), out long l) ? l : 0).ToArray();
                    case "bool":
                        return values.Select<string, bool>(s => { s.ToString().ToBoolExt(out bool b); return b; });
                    case "string":
                        return values;
                    case "real":
                        return values.Select<string, Double>(s => Double.TryParse(s.ToString(), out double d) ? d : 0).ToArray();
                    default:    //  comma separated types sequences will be used to describe complex types
                        throw new ArgumentException(string.Format("Unknown data type {0}", parameter.Type), parameter.Name);
                }
            }
            switch (parameter.Type)
            {
                case "int":
                    return Int64.TryParse(dynamic.ToString(), out long l) ? l : 0;
                case "bool":
                    {
                        string s = dynamic.ToString();
                        s.ToBoolExt(out bool b);
                        return b;
                    }
                case "string":
                    return dynamic.ToString();
                case "parameters":
                    Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
                    if (dynamic is Newtonsoft.Json.Linq.JArray jArray)
                    {
                        for (int i = 0; i < jArray.Count - 1; i += 2)
                        {
                            Newtonsoft.Json.Linq.JValue key = jArray[i] as Newtonsoft.Json.Linq.JValue;
                            Newtonsoft.Json.Linq.JValue value = jArray[i + 1] as Newtonsoft.Json.Linq.JValue;
                            values[key.Value.ToString()] = value.Value;
                        }
                    }
                    else
                        foreach (Newtonsoft.Json.Linq.JProperty token in ((Newtonsoft.Json.Linq.JObject)dynamic).Properties())
                            values[token.Name] = token.Value;
                    return values;
                case "real":
                    return Double.TryParse(dynamic.ToString(), out double d) ? d : 0;
                default:    //  comma separated types sequences will be used to describe complex types
                    throw new ArgumentException(string.Format("Unknown data type {0}", parameter.Type), parameter.Name);
            }
        }
    }
    /*
    public class Sequence : Command
    {
        List<Execution> children = new List<Execution>();
        public override IEnumerable<Parameter> Parameters { get { yield break; } }

        protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
        {
            List<Message> retValue = new List<Message>();
            foreach (Execution action in children)
            {
                string command = string.Format("{0}+{1}", action.Command.Name, action.Target);
                Manager.OnNotify("Scheduler", "Starting command {0}", command);
                Message results = Manager.Execute(action);
                results.Parameters["command"] = command;
                retValue.Add(results);
            }
            return retValue;
        }
    }
    */
}