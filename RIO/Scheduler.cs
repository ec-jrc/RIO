using JRC.CAP;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// It manages the cron-like scheduled tasks, usually defined in crontab.json
    /// </summary>
    public class Scheduler : IEnumerable<string>
    {
        private readonly RuleEngine CrontabEngine = new RuleEngine();
        private readonly RuleEngine UntilFalseEngine = new RuleEngine();
        private readonly RuleEngine UntilTrueEngine = new RuleEngine();
        Timer Timer;
        readonly string path = "crontab.json";
        readonly Dictionary<string, Execution> actions = new Dictionary<string, Execution>();
        readonly List<string> crontab = new List<string>();

        /// <summary>
        /// The list of actions the scheduler may perform when requested or scheduled.
        /// </summary>
        public IEnumerable<string> Commands => actions.Keys;

        /// <summary>
        /// The list of rules defining the schedule described in text form.
        /// </summary>
        public IEnumerable<string> Rules
        {
            get
            {
                foreach (Rule rule in CrontabEngine.Ruleset)
                    yield return rule.Expression;
            }
        }

        /// <summary>
        /// Initializes the <see cref="Scheduler"/> from the default path <code>crontab.json</code>,
        /// using the provided <see cref="Settings"/>.
        /// </summary>
        /// <param name="settings">The configuration of the RIO.</param>
        public void Initialize(Settings settings)
        {
            actions.Clear();
            crontab.Clear();
            CrontabEngine.Clear();

            if (!File.Exists(path))
            {
                var cron = new
                {
                    schedules = Array.Empty<string>(),
                    commands = new Dictionary<string, Execution>()
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(cron));
            }
            string config = File.ReadAllText(path);
            JObject obj = null;
            try
            {
                obj = JsonConvert.DeserializeObject(config) as JObject;
            }
            catch (Exception)
            {
                Manager.OnNotify("error", $"Invalid schedule file {path}");
                return;
            }
            foreach (JProperty jToken in obj["commands"].Children())
            {   // Create the executions for the scheduling rules
                string name = jToken.Name, target = jToken.Value["Target"].Value<string>(),
                    definingTask = RuleEngine.FindFeature(target, settings);
                string commandName = jToken.Value["Command"].Value<string>();
                Dictionary<string, dynamic> parameters = new Dictionary<string, dynamic>();
                parameters.AddRange<string, dynamic>(jToken.Value["Parameters"].Children<JProperty>()
            .Select<JProperty, KeyValuePair<string, dynamic>>(j => new KeyValuePair<string, dynamic>(j.Name, j.Value)));

                if (Manager.FindCommand(definingTask, commandName, out Command cmd))
                    actions[name] = new Execution() { Target = target, Command = cmd, Parameters = parameters };
            }

            foreach (JToken token in obj["schedules"].Children())
            {   // Parse the scheduling rules
                string schedule = token.Value<string>();
                crontab.Add(schedule);
                if (!schedule.StartsWith("#"))
                {
                    try
                    {
                        (Rule rule, string command) = CronParser.Parse(schedule);

                        if (actions.ContainsKey(command))
                        {
                            List<Execution> commands = new List<Execution>
                            {
                                actions[command]
                            };
                            rule.Actions = commands;
                        }
                        else throw new Exception($"Command {command} not found");
                        CrontabEngine.Ruleset.Add(rule);
                    }
                    catch (Exception ex)
                    {
                        Manager.OnNotify("error", $"Invalid schedule {schedule}: {ex.Message}");
                    }
                }
            }
        }
        internal void Start()
        {
            Timer = new Timer(SchedulerManager, null, 1000 + DateTime.Now.Millisecond, 1000);
        }

        internal void Stop()
        {
            Timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void SchedulerManager(object state)
        {
            lock (CrontabEngine)
            {
                var variables = Manager.Variables.Select(p =>
                      double.TryParse(p.Value.ToString(), out double doubleVal) ?
                          new KeyValuePair<string, object>(p.Key, doubleVal) :
                          new KeyValuePair<string, object>(p.Key, p.Value)
                ).ToArray();
                CrontabEngine.Knowledge.AddRange(variables);
                UntilFalseEngine.Knowledge.AddRange(variables);
                UntilTrueEngine.Knowledge.AddRange(variables);
            }
            List<Execution> actions = new List<Execution>();
            var process = CrontabEngine.Process();
            actions.AddRange(process.actions);
            process = UntilFalseEngine.Process();
            foreach (Rule rule in process.failed)
                UntilFalseEngine.Ruleset.Remove(rule);
            actions.AddRange(process.actions);
            process = UntilTrueEngine.Process();
            foreach (Rule rule in process.passed)
                UntilTrueEngine.Ruleset.Remove(rule);
            actions.AddRange(process.actions);

            foreach (Execution action in actions)
            {
                string command = string.Format("{0}+{1}", action.Command.Name, action.Target);
                Manager.OnNotify("Scheduler", "Starting command {0}", command);
                Message results = Manager.Execute(action);

                Manager.OnNotify("Scheduler", results);
            }
        }

        /// <summary>
        /// The method provides the <see cref="IEnumerator"/> functionality to list all schedules.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<string> GetEnumerator()
        {
            return crontab.GetEnumerator();
        }

        /// <summary>
        /// The method provides the <see cref="IEnumerator"/> functionality to list all schedules.
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return crontab.GetEnumerator();
        }

        internal bool Execute(string command, Message response, dynamic parameters = null)
        {
            if (actions.ContainsKey(command))
            {
                Execution action = actions[command];
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

        internal void Update(alert alert)
        {
            if (CrontabEngine.Ruleset.Count > 0)
                foreach (alertInfo info in alert.info)
                    CrontabEngine.Update(alert, alert.source, info);
        }

        internal string Reload()
        {
            try
            {
                Initialize(Manager.Instance.Settings);
                return string.Format("New schedule installed: {0} rules", CrontabEngine.Ruleset.Count);
            }
            catch (Exception ex)
            {
                return string.Format("Unable to install the new schedule: {0}", ex.Message);
            }
        }

        internal void UntilFalse(List<Rule> rules)
        {
            UntilFalseEngine.Ruleset.AddRange(rules);
        }

        internal void UntilTrue(List<Rule> rules)
        {
            UntilTrueEngine.Ruleset.AddRange(rules);
        }
    }
}
