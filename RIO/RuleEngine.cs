using JRC.CAP;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RIO
{
    /// <summary>
    /// This class uses a set of <see cref="Rule"/>s to decide if it is necessary to perform a set of actions, i.e.
    /// list of <see cref="Execution"/>s.
    /// </summary>
    public class RuleEngine
    {
        private readonly static string SystemRuleId = "07130A7BB12443E9BDA67AB13DBAF4CE";
        private Rule SystemRule;
        string DeviceId { get; set; }
        /// <summary>
        /// The set of variables that will be available to the <see cref="Rule.Expression"/>
        /// </summary>
        [JsonIgnore()]
        public Dictionary<string, object> Knowledge = new Dictionary<string, object>();
        [JsonIgnore()]
        private readonly Dictionary<string, DateTime> aging = new Dictionary<string, DateTime>();
        /// <summary>
        /// The collection of <see cref="Rule"/>s and the related actions.
        /// </summary>
        public List<Rule> Ruleset { get; } = new List<Rule>();
        /// <summary>
        /// It is the table of preset list of activities to be performed: multiple <see cref="Rule"/>s of the <see cref="Ruleset"/>
        /// may link to the same list of <see cref="Execution"/>s
        /// </summary>
        public Dictionary<string, List<Execution>> Actions { get; } = new Dictionary<string, List<Execution>>();
        /// <summary>
        /// A list of sources: only the sources in this list will require to evaluate the <see cref="Ruleset"/>.
        /// </summary>
        public List<string> Devices { get; } = new List<string>();
        /// <summary>
        /// A table to convert the names of the information sources in a format accepted by a <see cref="Rule.Expression"/>.
        /// </summary>
        public Dictionary<string, string> Translations { get; } = new Dictionary<string, string>();
        internal List<Execution> Process(Message request)
        {
            List<Execution> actions = new List<Execution>();
            lock (this)
            {
                string source = request.Source;
                if (!Devices.Contains(source))
                    return actions;

                Set(source, request.Parameters);
                foreach (Rule rule in Ruleset)
                    try
                    {
                        if (rule.Condition(Knowledge) == true)
                            foreach (Execution action in rule.Actions)
                            {   // we need to pass a clone of the Execution, to avoid messing with other requests
                                Execution exec = new Execution() { Command = action.Command, Target = action.Target, Parameters = new Dictionary<string, dynamic>() };
                                exec.Parameters.AddRange<string, dynamic>(request.Parameters);
                                exec.Parameters.AddRange<string, dynamic>(action.Parameters);
                                actions.Add(exec);
                            }
                    }
                    catch (Exception ex)
                    {
                        Manager.OnNotify("error", "Execution error for Rule {0}, {1}", rule, ex);
                    }
            }

            return actions;
        }

        internal (List<Rule> passed, List<Rule> paused, List<Rule> failed, List<Execution> actions) Process()
        {
            List<Execution> actions = new List<Execution>();
            List<Rule> passed = new List<Rule>(), paused = new List<Rule>(), failed = new List<Rule>();
            foreach (Rule rule in Ruleset.ToArray().AsParallel())
            {
                try
                {
                    Dictionary<string, object> data = Knowledge.Copy();
                    CleanAged(rule.TimeTrigger, data);
                    bool? evaluation = rule.Condition(data);
                    if (evaluation == true)
                    {
                        foreach (Execution action in rule.Actions)
                        {   // we need to pass a clone of the Execution, to avoid messing with other requests
                            Execution exec = new Execution() { Command = action.Command, Target = action.Target, Parameters = new Dictionary<string, dynamic>() };
                            exec.Parameters.AddRange<string, dynamic>(action.Parameters);
                            lock (action)
                                actions.Add(exec);
                        }
                        passed.Add(rule);
                    }
                    else if (evaluation == false)
                        failed.Add(rule);
                    else
                        paused.Add(rule);
                }
                catch (Exception)
                {
                    //failed.Add(rule);
                }
            }
            if (SystemRule != null)
            {
                Dictionary<string, object> data = Knowledge.Copy();
                CleanAged(SystemRule.TimeTrigger, data);
                try
                {
                    if (SystemRule.Condition(data) == true)
                        foreach (Execution action in SystemRule.Actions)
                        {   // we need to pass a clone of the Execution, to avoid messing with other requests
                            Execution exec = new Execution() { Command = action.Command, Target = action.Target, Parameters = new Dictionary<string, dynamic>() };
                            exec.Parameters.AddRange<string, dynamic>(action.Parameters);
                            lock (action)
                                actions.Add(exec);
                        }
                }
                catch (Exception ex)
                {
                    Manager.OnNotify("error", "SystemRule evaluation: {0}", ex.Message);
                }
            }
            return (passed, paused, failed, actions);
        }

        internal (List<Rule> passed, List<Rule> paused, List<Rule> failed, List<Execution> actions) Process(alert alert)
        {
            List<Execution> actions = new List<Execution>();
            List<Rule> passed = new List<Rule>(), paused = new List<Rule>(), failed = new List<Rule>();
            Manager.OnNotify("", string.Format("Processing alert {0}", alert.identifier));

            lock (this)
            {
                string sender = alert.sender;
                if (Devices.Contains(sender) || Manager.Id.Equals(sender))
                {
                    foreach (alertInfo info in alert.info)
                    {
                        Dictionary<string, object> parameters = Update(alert, sender, info);
                        foreach (Rule rule in Ruleset)
                            try
                            {
                                Dictionary<string, object> data = Knowledge.Copy();
                                CleanAged(rule.TimeTrigger, data);
                                bool? evaluation = rule.Condition(data);
                                if (evaluation == true)
                                {
                                    foreach (Execution action in rule.Actions)
                                    {   // we need to pass a clone of the Execution, to avoid messing with other requests
                                        Execution exec = new Execution() { Command = action.Command, Target = action.Target, Parameters = new Dictionary<string, dynamic>() };
                                        exec.Parameters.AddRange<string, dynamic>(parameters);
                                        exec.Parameters.AddRange<string, dynamic>(action.Parameters);
                                        actions.Add(exec);
                                    }
                                    passed.Add(rule);
                                }
                                else if (evaluation == false)
                                    failed.Add(rule);
                                else
                                    paused.Add(rule);
                            }
                            catch (Exception ex)
                            {
                                Manager.OnNotify("error", "alert evaluation: {0}", ex.Message);
                            }
                    }
                    //else foreach (alertInfo info in alert.info)
                    //    {
                    //        if (alert.InAddresses(DeviceId) || info.InArea(Location))
                    //        {
                    //            Dictionary<string, object> parameters = new Dictionary<string, object>();
                    //            parameters.AddRange(info.parameter.Select<alertInfoParameter, KeyValuePair<string, object>>(p =>
                    //            {
                    //                if (double.TryParse(p.value, out double doubleVal))
                    //                    return new KeyValuePair<string, object>(p.valueName, doubleVal);

                    //                return new KeyValuePair<string, object>(p.valueName, p.value);
                    //            }));
                    //            Set(sender, parameters);
                    //            foreach (Rule rule in Ruleset)
                    //                try
                    //                {
                    //                    if (rule.Condition(Knowledge))
                    //                        actions.AddRange(rule.Actions);
                    //                }
                    //                catch (Exception ex) { }
                    //        }
                    //    }
                }
            }

            return (passed, paused, failed, actions);
        }

        internal Dictionary<string, object> Update(alert alert, string sender, alertInfo info)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            Compose(parameters, alert);
            parameters.Add("eventType", info.@event);

            parameters.AddRange(info.parameter?.Select(p =>
            {
                if (double.TryParse(p.value, out double doubleVal))
                    return new KeyValuePair<string, object>(p.valueName, doubleVal);

                return new KeyValuePair<string, object>(p.valueName, p.value);
            }));
            parameters["Language"] = string.IsNullOrWhiteSpace(info.language) ? "en-US" : info.language;

            Set(sender, parameters);
            Set(alert.source, parameters);
            return parameters;
        }

        private static void Compose(Dictionary<string, object> parameters, alert alert)
        {
            if (alert.source != null)
                parameters.Add("source", alert.source);
            parameters.Add("status", (int)alert.status);
            parameters.Add("msgType", (int)alert.msgType);
            if (alert.code != null)
                parameters.Add("codes", string.Join(",", alert.code));
            parameters.Add("addresses", alert.addresses ?? string.Empty);
        }
        /// <summary>
        /// Updates <see cref="Knowledge"/> with the information stored in <paramref name="parameters"/> and related to
        /// the <paramref name="source"/>, e.g. another RIO device.
        /// </summary>
        /// <param name="source">The object the infromation refers to.</param>
        /// <param name="parameters">A key-value set of information</param>
        public void Set(string source, Dictionary<string, object> parameters)
        {
            TimeSpan maxTimeTrigger = Ruleset.Max(r => r.TimeTrigger);
            CleanAged(maxTimeTrigger, Knowledge, true);

            if (!Devices.Contains(source) && !Manager.Id.Equals(source))
                return;

            if (Translations.ContainsKey(source))
                source = Translations[source];

            lock (this)
            {
                foreach (string key in parameters.Keys.ToArray())
                {
                    if (double.TryParse(parameters[key].ToString(), out double d))
                        parameters[key] = d;
                    if (key.EndsWith("AlertLevel", StringComparison.InvariantCultureIgnoreCase))
                        parameters[source] = Knowledge[source] = d;
                    //{
                    //    if (Knowledge.ContainsKey(source))
                    //    {
                    //        if (int.TryParse(Knowledge[source]?.ToString(), out int previous))
                    //        {
                    //            parameters[source] = Knowledge[source] = Knowledge["ALERT_LEVEL"] = Math.Max(previous, d);
                    //        }
                    //    }
                    //    else
                    //        parameters[source] = Knowledge[source] = d;
                    //}
                    string name = string.Format("{0}_{1}", source, key);
                    Knowledge[name] = parameters[key];
                }
                aging[source] = DateTime.UtcNow;
            }
        }

        private void CleanAged(TimeSpan timeSpan, Dictionary<string, object> data, bool clean = false)
        {
            DateTime present = DateTime.UtcNow;
            if (timeSpan < TimeSpan.MaxValue)
                lock (this)
                {
                    foreach (string device in aging.Keys.ToArray())
                        if (present - aging[device] > timeSpan)
                        {
                            if (clean)
                                aging.Remove(device);
                            foreach (string key in data.Keys.Where(s => s.StartsWith(device)).ToArray())
                                data.Remove(key);
                        }
                }
        }
        /// <summary>
        /// Use this to create a new <see cref="RuleEngine"/> and configure it with the content of the file in
        /// <paramref name="path"/>.
        /// </summary>
        /// <param name="path">File path to initialize the <see cref="RuleEngine"/>. If not existing, empty or invalid,
        /// it is overwritten by an empty configuration.</param>
        /// <returns>A configured <see cref="RuleEngine"/></returns>
        public static RuleEngine LoadConfiguration(string path)
        {
            try
            {
                RuleEngine re = new RuleEngine();
                if (!File.Exists(path))
                {
                    Manager.OnNotify("error", string.Format("Missing {0}", path));
                    File.WriteAllText(path, JsonConvert.SerializeObject(re, Formatting.Indented));
                }
                else
                {
                    string config = File.ReadAllText(path);
                    JObject obj = JsonConvert.DeserializeObject(config) as JObject;

                    re.Devices.AddRange(obj[nameof(re.Devices)].Children().Select<JToken, string>(j => j.Value<string>()));
                    if (!re.Devices.Contains("RIO.MGMT"))
                        re.Devices.Add("RIO.MGMT");

                    re.Translations.AddRange<string, string>(obj[nameof(re.Translations)].Children<JProperty>()
                        .Select<JProperty, KeyValuePair<string, string>>(j => new KeyValuePair<string, string>(j.Name, j.Value.Value<string>())));
                    re.Translations["RIO.MGMT"] = "RIO_MGMT";

                    foreach (JProperty jProperty in obj[nameof(re.Actions)])
                    {
                        string preset = jProperty.Name;
                        List<Execution> actions = new List<Execution>();
                        foreach (JToken item in jProperty.Value)
                        {
                            string target = item["Target"].Value<string>(),
                                definingTask = Manager.FindFeature(target)?.Id;
                            string commandName = item["Command"].Value<string>();
                            Dictionary<string, dynamic> parameters = new Dictionary<string, dynamic>();
                            parameters.AddRange<string, dynamic>(item["Parameters"].Children<JProperty>()
                        .Select<JProperty, KeyValuePair<string, dynamic>>(j =>
                        {
                            dynamic value;
                            if (j.Value is JArray ja) value = ja.Select(t => t.Value<string>()).ToArray();
                            else value = j.Value.Value<string>();
                            return new KeyValuePair<string, dynamic>(j.Name, value);
                        }));

                            if (Manager.FindCommand(definingTask, commandName, out Command cmd))
                                actions.Add(new Execution() { Target = target, Command = cmd, Parameters = parameters });
                        }
                        if (actions.Count > 0)
                            re.Actions[preset] = actions;
                    }
                    foreach (JToken token in obj[nameof(Ruleset)])
                        try
                        {
                            DateTime dt = DateTime.UtcNow;
                            TimeSpan timeTrigger = new TimeSpan();

                            string id = token["Id"].Value<string>();
                            string tt = token["TimeTrigger"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(tt)) timeTrigger = TimeSpan.Parse(tt);

                            string expression = token["Expression"].Value<string>();
                            string preset = token["Actions"].Value<string>();

                            if (re.Actions.TryGetValue(preset, out List<Execution> actions))
                            {
                                Rule rule = new Rule()
                                {
                                    Id = id,
                                    Expression = expression,
                                    TimeTrigger = timeTrigger,
                                    Actions = actions,
                                    ActionList = preset
                                };
                                re.Ruleset.Add(rule);
                            }
                        }
                        catch
                        { // wrong Rule ignored
                        }
                }
                {
                    List<Execution> executions = new List<Execution>();
                    if (Manager.FindCommand("TadDisplay", "display", out Command cmd))
                        executions.Add(new Execution() { Command = cmd, Target = "TadDisplay", Parameters = new Dictionary<string, dynamic>() });
                    if (Manager.FindCommand("TadDisplay", "siren", out cmd))
                        executions.Add(new Execution() { Command = cmd, Target = "TadDisplay", Parameters = new Dictionary<string, dynamic>() });
                    if (Manager.FindCommand("TadDisplay", "speaker", out cmd))
                        executions.Add(new Execution() { Command = cmd, Target = "TadDisplay", Parameters = new Dictionary<string, dynamic>() });

                    if (executions.Count > 0)
                    {
                        re.SystemRule = new Rule
                        {
                            Id = SystemRuleId,
                            Expression = "RIO_MGMT_command = \"setPage\" AND RIO_MGMT_addresses.Contains( ID )",
                            Actions = executions.ToArray(),
                            TimeTrigger = TimeSpan.FromMinutes(10)
                        };
                    }
                }

                re.Knowledge["ID"] = re.DeviceId = Manager.Id;
                return re;
            }
            catch (Exception ex)
            {
                Manager.OnNotify("error", ex.Message);
                RuleEngine re = new RuleEngine();
                File.WriteAllText(path, JsonConvert.SerializeObject(re, Formatting.Indented));
                return re;
            }
        }

        internal void SaveConfiguration(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        internal void Clear() { Ruleset.Clear(); }

        internal static string FindFeature(string target, Settings settings)
        {
            Feature feature = null;
            if ((feature = settings.Features.FirstOrDefault(f => f.Id.Equals(target))) != null)
                return feature.Type;
            if ((feature = settings.Features.FirstOrDefault(f => f.Type.Equals(target))) != null)
                return target;
            return target;
        }

        internal bool Execute(string command, Message response, dynamic parameters = null)
        {
            if (Actions.ContainsKey(command))
            {
                List<Execution> actions = Actions[command];
                foreach (Execution action in actions)
                {
                    Dictionary<string, dynamic> arguments = new Dictionary<string, dynamic>();
                    arguments.AddRange(action.Parameters);
                    if (parameters != null)
                        Extensions.AddRange(arguments, parameters);
                    Execution exec = new Execution()
                    {
                        Command = action.Command,
                        Target = action.Target,
                        Parameters = arguments
                    };
                    Message m = Manager.Execute(exec);
                    response.Parameters.AddRange(m.Parameters);
                    return true;
                }
                return false;
            }

            return false;
        }
    }
}