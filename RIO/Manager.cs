using JRC.CAP;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    ///   <para>This is the main class of a RIO system, similar to the scheduler of an Operating System.</para>
    ///   <para>It allows interacting with the RIO system, its configuration, all the defined features and the running tasks.</para>
    /// </summary>
    public class Manager : Dictionary<string, IMeasurable>
    {
        private readonly List<ITask> tasks = new List<ITask>();
        private static readonly EventWaitHandle wh = new AutoResetEvent(false);
        private bool running;
        private string myId;
        private Settings settings = null;
        private DateTime shutdownExpire = DateTime.MinValue;
        RuleEngine ruleEngine = null;
        private readonly Scheduler cron = new Scheduler();
        private static readonly string RIO_FEATURE_NAME = "RIO";

        /// <summary>
        /// The boot starting time
        /// </summary>
        public static DateTime StartTime { get; } = DateTime.UtcNow;
        /// <summary>
        /// The RIO version.
        /// </summary>
        public static string Version => "1.5.2";
        // 1.1.1: Kos
        // 1.2.0: Improved local and remote user interaction
        // 1.3.0: Scheduling and error broadcasting
        // 1.4.0: Execution result broadcasting
        // 1.5.0: Massive libraries update
        // 1.5.1: Rules revision and documentation
        // 1.5.2: File synchronization with website
        /// <summary>
        /// The Id of the singleton <see cref="Instance"/> as defined in <see cref="Settings"/>.
        /// </summary>
        public static string Id => Instance.settings.Id;

        /// <summary>  Returns the settings of the RIO system.
        /// It is useful to connect to its Changed event.</summary>
        /// <value>The <see cref="Settings"/> instance.</value>
        public Settings Settings { get => settings; }

        /// <summary>Gets the Manager singleton.</summary>
        /// <value>The only instance of Manager that can be present in a process.</value>
        public static Manager Instance { get; private set; } = new Manager();
        /// <summary>Gets the Manager scheduler.</summary>
        /// <value>This crontab-like facility is used to schedule single or periodic activities.</value>
        public static Scheduler Scheduler { get => Instance.cron; }
        static Dictionary<string, Command> AvailableCommands { get; set; }
        static readonly Dictionary<string, dynamic> variables = new Dictionary<string, dynamic>();
        /// <summary>
        /// A dictionary of variables set using the telemetry of the RIO, the <see cref="Settings"/>
        /// and the info in the processed alerts.
        /// </summary>
        public static Dictionary<string, dynamic> Variables
        {
            get
            {
                lock (variables)
                {
                    return variables.Copy();
                }
            }
        }

        /// <summary>
        /// Runs asynchronously the RIO system. When the method returns, the RIO shut down.
        /// </summary>
        public static async Task RunAsync()
        {
            await Instance.InstanceRun();
        }

        /// <summary>
        /// Configures asynchronously the RIO system according to the <paramref name="settings"/>.
        /// </summary>
        /// <param name="settings">The <see cref="RIO.Settings"/> loaded by the caller.</param>
        /// <param name="universal">true when running in Windows Universal Applications</param>
        public static async Task ConfigureAsync(Settings settings, bool universal = false)
        {
            await Instance.InstanceSetup(settings, universal);
        }

        private static void UpdateVariables(dynamic telemetry)
        {
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string feature = null;
            DateTime? ts = null;
            foreach (var prop in ((object)telemetry).AllProperties())
            {
                switch (prop.Name)
                {
                    case "DeviceId":
                        // This is ignored, because used only for the RIO itself
                        break;
                    case "FeatureId":
                        feature = prop.Value;
                        break;
                    case "Timestamp":
                        ts = DateTime.SpecifyKind(prop.Value, DateTimeKind.Utc);
                        break;
                    default:
                        values.Add(prop.Name, prop.Value);
                        break;
                }
            }
            lock (variables)
            {
                foreach (string key in values.Keys)
                    variables[string.Format("{0}_{1}", feature, key)] = values[key];
                variables[string.Format("{0}_last", feature)] = ts;
            }
        }

        [ImportMany]
        private IEnumerable<IFeature> Plugins { get; set; }
        /// <summary>
        /// The rule engine used to evaluate the alerts.
        /// </summary>
        /// <value>
        /// The <see cref="RIO.RuleEngine"/>.
        /// </value>
        public RuleEngine RuleEngine
        {
            get
            {
                lock (this)
                {
                    if (ruleEngine == null)
                    {
                        ruleEngine = RuleEngine.LoadConfiguration("Ruleset.json");
                    }
                    return ruleEngine;
                }
            }
        }

        /// <summary>
        /// Configures the RIO Rule engine. It gives the engine the rules to check at every alert received and the mapping from invalid to acceptable device identifiers.
        /// </summary>
        /// <param name="translations">The translations for invalid formatted device ids like XXX-YYY, that should be XXX_YYY or other strings that cannot be an expression.</param>
        /// <param name="rules">The rules.</param>
        /// <param name="actions">The set of commands referred by the rules, to be executed in case of rule matching.</param>
        public void ConfigureEngine(Dictionary<string, string> translations, IList<Rule> rules, Dictionary<string, IList<Execution>> actions)
        {
            lock (this)
            {
                RuleEngine.Devices.Clear();
                RuleEngine.Devices.AddRange(translations.Keys);
                RuleEngine.Devices.Add("RIO.MGMT");

                RuleEngine.Ruleset.Clear();
                RuleEngine.Ruleset.AddRange(rules);

                RuleEngine.Translations.Clear();
                RuleEngine.Translations.AddRange(translations);
                RuleEngine.Translations.Add("RIO.MGMT", "RIO_MGMT");

                RuleEngine.Actions.Clear();
                foreach (string key in actions.Keys)
                    RuleEngine.Actions[key] = actions[key].ToList();

                RuleEngine.SaveConfiguration("Ruleset.json");
            }
        }

        static IEnumerable<Assembly> LoadAssemblies(string path, params string[] patterns)
        {
            SearchOption searchOption = SearchOption.TopDirectoryOnly;
            foreach (string pattern in patterns)
                foreach (string name in Directory.GetFiles(path, pattern, searchOption))
                {
                    Assembly assembly;
                    try
                    {
                        assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(name);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to load assembly {0}, because {1}", name, ex.InnerException?.Message ?? ex.Message);
                        continue;
                    }
                    yield return assembly;
                }
        }

        private static void LoadCommands(IEnumerable<Assembly> libraries)
        {
            AvailableCommands = new Dictionary<string, Command>();
            //foreach (IFeature feature in plugins)
            //    foreach (Command command in feature.Commands)
            //        AvailableCommands.Add(string.Format("{0}+{1}", command.Target, command.Name), command);

            Type type = typeof(Command);
            foreach (Type commandType in libraries.SelectMany<Assembly, Type>(a => a.GetTypes().Where<Type>(t => type.IsAssignableFrom(t))))
                if (!commandType.Equals(type))
                {
                    Command cmd = Activator.CreateInstance(commandType) as Command;
                    AvailableCommands.Add(string.Format("{0}+{1}", cmd.Target, cmd.Name), cmd);
                }
        }

        private async Task InstanceSetup(Settings settings, bool universal = false)
        {
            myId = settings.Id;
            this.settings = settings;

            if (!string.IsNullOrEmpty(settings.Location))
            {
                string[] fields = settings.Location.Split(',');
                double latitude = double.NaN, longitude = double.NaN;
                bool
                goodLat = double.TryParse(fields[0], out latitude),
                goodLong = double.TryParse(fields[1], out longitude);

                if (goodLong && goodLat)
                {
                    IMetrics m = new LocationMetrics(longitude, latitude);
                    lock (variables)
                    {
                        variables["longitude"] = longitude;
                        variables["latitude"] = latitude;
                    }
                    Set("location", m);
                }
            }

            if (universal)
            {
                List<Assembly> assemblies = new List<Assembly>();
                string path = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Manager)).Location);

                List<Assembly> libraries = Directory.GetFiles(path, "TAD*.dll")
                    .Union(Directory.GetFiles(path, "JRC*.dll"))
                    .Union(Directory.GetFiles(path, "RIO*.dll"))
                    .Select(s => Assembly.LoadFrom(s)).ToList();
                var type = typeof(IFeature);
                var types = libraries
                    .SelectMany(s => s.GetTypes())
                    .Where(p => type.IsAssignableFrom(p) && !p.Equals(type));
                Plugins = types.Select(t => (IFeature)Activator.CreateInstance(t)).ToArray();
                AvailableCommands = new Dictionary<string, Command>();
                foreach (IFeature feature in Plugins)
                    foreach (Command command in feature.Commands)
                        AvailableCommands.Add(string.Format("{0}+{1}", command.Target, command.Name), command);
            }
            else
            {
                var conventions = new ConventionBuilder();
                conventions
                    .ForTypesDerivedFrom<IFeature>()
                    .Export<IFeature>()
                    .Shared();

                string path = Directory.GetCurrentDirectory();

                List<Assembly> libraries = new List<Assembly>(LoadAssemblies(path, "TAD*.dll", "JRC*.dll", "RIO*.dll"));    // HACK: Core creates a lot of not managed dll to be avoided

                var configuration = new ContainerConfiguration().WithAssemblies(libraries, conventions);

                //var configuration = new ContainerConfiguration().WithAssembly(typeof(Manager).Assembly);

                var container = configuration.CreateContainer();
                //container.SatisfyImports(this);

                //var catalog = new AggregateCatalog();
                //catalog.Catalogs.Add(new DirectoryCatalog(path, "TAD.*.dll"));
                //catalog.Catalogs.Add(new DirectoryCatalog(path, "JRC.*.dll"));
                //catalog.Catalogs.Add(new DirectoryCatalog(path, "RIO.*.dll"));
                //catalog.Catalogs.Add(new DirectoryCatalog(path, "RIO.*.exe"));
                //var container = new CompositionContainer(catalog);

                Plugins = container.GetExports<IFeature>();

                LoadCommands(libraries);
            }

            foreach (IFeature plugin in Plugins)
            {
                OnNotify("Manager", "Configuring feature {0}, {1}", plugin.GetType().Name, plugin.Version);
                Feature[] features = settings.Features.Where(f => f.Type.Equals(plugin.GetType().Name)).ToArray();
                if (features.Length == 0)
                {
                    Feature feature = new Feature() { Enabled = false, Id = string.Format("default {0}", plugin.GetType().Name), Type = plugin.GetType().Name, Version = plugin.Version };
                    foreach (dynamic item in plugin.Configuration)
                    {
                        feature.Properties[item.Name] = item.Default;
                    }
                    settings.Features.Add(feature);
                    settings.OnChanged(nameof(settings.Features));
                    features = new Feature[] { feature };
                    OnNotify(feature.Id, "default configuration");
                }
                foreach (Feature feature in features)
                {
                    variables[string.Format("{0}_version", feature.Id)] =
                    feature.Version = plugin.Version;
                    variables[string.Format("{0}_type", feature.Id)] = feature.Type;

                    if (feature.Enabled)
                    {
                        foreach (string key in feature.Properties.Keys)
                        {
                            //OnNotify(feature.Id, "{0} = {1}", key, feature.Properties[key]);
                            variables[string.Format("{0}_{1}", feature.Id, key)] = feature.Properties[key];
                        }

                        string log = string.Empty;
                        //OnNotify(feature.Id, log);
                        try
                        {
                            foreach (ITask task in plugin.Setup(settings, feature))
                            {
                                log += string.Format("{1}{0} ", task.Status, log);
                                tasks.Add(task);
                            }
                        }
                        catch (Exception ex)
                        {
                            log += ex.Message;
                        }
                        OnNotify(feature.Id, log);
                    }
                }
            }

            foreach (ITask task in tasks)
                if (task.Feature != null)
                    this[task.Feature.Id] = task;

            OnNotify("Manager", "Configured {0}", Plugins.Count());

            //LoadCommands(libraries);
            OnNotify("Manager", "Command found {0}", AvailableCommands.Count);

            cron.Initialize(settings);
            OnNotify("Manager", "Scheduler configured {0}", cron.Count());
        }

        private async Task InstanceRun()
        {
            //scheduler = new Timer(schedulerManager, null, 1000 + DateTime.Now.Millisecond, 1000);
            cron.Start();
            Array.ForEach(tasks.ToArray(), t => t.Start());
            Array.ForEach(tasks.ToArray(), t => OnNotify("Manager", "{0}: {1}", t.Name, t.Status));

            OnNotify("Manager", "Started {0}", tasks.Count);
            OnNotify("Manager", new Message() { Type = "update", Source = Id });

            running = true;
            while (running)
                wh.WaitOne(1000);
            OnNotify("exit", "RIO stopped");
        }

        /// <summary>
        /// Requires the RIO to shutdown.
        /// </summary>
        /// <param name="force">if set to <c>false</c> the RIO will wait for all the tasks to complete; otherwise, it will stop immediately.</param>
        public static void Shutdown(bool force = false)
        {
            if (!force)
                Instance.InstanceShutdown();
            Instance.running = false;
            wh.Set();
        }

        private void InstanceShutdown()
        {
            try { cron.Stop(); }
            catch { }
            foreach (var item in this.Values)
            {
                if (item is ITask task)
                    try
                    { task.Stop()?.Wait(); }
                    catch { }
            }
        }

        /// <summary>
        /// Allows to provide a set of information under a well-known name, e.g. Location, which will hold the position of the device.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="metrics">The metrics about the service.</param>
        public static void Set(string name, IMetrics metrics)
        {
            Service service;
            if (Instance.TryGetValue(name, out IMeasurable entry))
            {
                service = entry as Service;
                if (service == null)
                    return;
            }
            else
                Instance[name] = (service = new Service());
            service.Metrics = metrics;
            //OnNotify("Verbose", string.Format("Manager property {0}: {1}", name, metrics));
        }

        /// <summary>
        /// Call this method to notify all observer of the <see cref="Notify"/> event.
        /// </summary>
        /// <param name="source">The source of the notification.</param>
        /// <param name="notification">The notification, that is usually serialized and sent through the communication channels.</param>
        /// <param name="info">Optional list of string to be formatted through notification used as a formatting string</param>
        public static void OnNotify(string source, object notification, params object[] info)
        {
            try
            {
                switch (source)
                {
                    case "alert":
                        Instance.InstanceManageAlert(notification as alert);
                        break;
                    case "telemetry":
                        UpdateVariables(notification);
                        break;
                }
            }
            // I will ignore all errors from customers
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            //if (notification is Message message)
            //{
            //    if (message.Type?.Equals("Execution Result") == true)
            //    {
            //if (message.Parameters.TryGetValue("Execution Result", out object output))
            //{
            //    notification = output.ToText();
            //}
            //else
            //if (message.Parameters.TryGetValue("Error", out object error))
            //{
            //    notification = error.ToText();
            //}
            //    }
            //}
            try
            {
                if (info.Length > 0)
                    notification = string.Format(notification.ToString(), info);
                Notify?.Invoke(source, notification);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Manages the request contained in a string.
        /// </summary>
        /// <param name="requestText">The request text to be parsed in a <see cref="Message"/> and handled.</param>
        /// <returns>A response <see cref="Message"/> with the result of the request handler.</returns>
        public static Message ManageRequest(string requestText)
        {
            try
            {
                Message request = Message.ParseJson(requestText);
                if (!request.IsValid)
                    return null;
                return Instance.InstanceManage(request);
            }
            catch (Exception ex)
            {
                OnNotify("error", string.Format("Request failed: {0}", ex.Message));
            }
            return null;
        }

        /// <summary>
        /// Manages the request [Message] and returns a response [Message] to be returned to the request sender.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>A response <see cref="Message"/> with the result of the request handler.</returns>
        public static Message ManageRequest(Message request)
        {
            try
            {
                if (!request.IsValid)
                    return null;
                Message response = Instance.InstanceManage(request);
                OnNotify("Manager", response);
                return response;
            }
            catch (Exception ex)
            {
                OnNotify("error", string.Format("Request failed: {0}", ex.Message));
            }
            return null;
        }

        /// <summary>Builds a cancellation alert from a given one.</summary>
        /// <param name="source">The source of the cancellation message.</param>
        /// <param name="alert">The original CAP that the result is meant to cancel.</param>
        /// <returns>A complete well formed <see cref="alert"/> of type <see cref="alertMsgType.Cancel"/> to cancel the supplied one.</returns>
        public static alert BuildCancel(string source, alert alert)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("message", nameof(source));
            }
            alert cancel = null;
            try
            {
                cancel = new alert()
                {
                    source = source,
                    sender = Instance.Settings.Id,
                    identifier = string.Format("{0}.{1}", Instance.Settings.Id, DateTime.UtcNow.ToString("yyyyMMddHHmmss")),
                    msgType = alertMsgType.Cancel,
                    scope = alert.scope,
                    sent = DateTime.UtcNow,
                    status = alert.status,
                    addresses = alert.addresses,
                    references = alert.identifier
                };
            }
            catch { }
            return cancel;
        }

        /// <summary>Builds a cancellation alert from a given one.</summary>
        /// <param name="source">The source of the cancellation message.</param>
        /// <param name="reference">The if of the original CAP that the result is meant to cancel.</param>
        /// <returns>A complete well formed <see cref="alert"/> of type <see cref="alertMsgType.Cancel"/> to cancel the supplied one.</returns>
        public static alert BuildCancel(string source, string reference)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("message", nameof(source));
            }
            alert cancel = null;
            try
            {
                cancel = new alert()
                {
                    source = source,
                    sender = Instance.Settings.Id,
                    identifier = string.Format("{0}.{1}", Instance.Settings.Id, DateTime.UtcNow.ToString("yyyyMMddHHmmss")),
                    msgType = alertMsgType.Cancel,
                    scope = alertScope.Public,
                    sent = DateTime.UtcNow,
                    status = alertStatus.Actual,
                    references = reference
                };
            }
            catch { }
            return cancel;
        }

        /// <summary>Builds the alert.</summary>
        /// <param name="source">The source.</param>
        /// <param name="category">The category.</param>
        /// <param name="event">The event.</param>
        /// <param name="status">The status of the alert: RIO uses internally System, Actual to interact with external services.</param>
        /// <param name="parameters">The parameters.</param>
        /// <returns>A complete well formed <see cref="alert"/> with an info part based on the passed <paramref name="parameters"/>.</returns>
        public static alert BuildAlert(string source, string category, string @event, alertStatus status = alertStatus.Actual, Dictionary<string, string> parameters = null)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("message", nameof(source));
            }

            if (string.IsNullOrEmpty(category))
            {
                throw new ArgumentException("message", nameof(category));
            }

            if (string.IsNullOrEmpty(@event))
            {
                throw new ArgumentException("message", nameof(@event));
            }

            dynamic location = new { Latitude_Decimal = 0.0, Longitude_Decimal = 0.0 };
            if (Instance.ContainsKey("location"))
                location = Instance["location"].Metrics;

            alertInfoCategory catCode = alertInfoCategory.Other;
            Enum.TryParse(category, out catCode);
            alert al = null;

            try
            {
                al = new alert()
                {
                    source = source,
                    sender = Instance.Settings.Id,
                    identifier = string.Format("{0}.{1}", Instance.Settings.Id, DateTime.UtcNow.ToString("yyyyMMddHHmmss")),
                    msgType = alertMsgType.Alert,
                    scope = alertScope.Public,
                    sent = DateTime.UtcNow,
                    status = status,
                    info = new alertInfo[] { new alertInfo() {
                        category=new alertInfoCategory[]{ catCode },
                        @event=@event,
                        certainty = alertInfoCertainty.Observed,
                        urgency = alertInfoUrgency.Immediate,
                        severity = alertInfoSeverity.Severe,
                        parameter = parameters?.Select<KeyValuePair<string,string>,alertInfoParameter>(kv => new alertInfoParameter(){ valueName=kv.Key, value=kv.Value}).ToArray(),
                        area=new alertInfoArea[]{ new alertInfoArea() { areaDesc="Impact area", circle=new string[] {string.Format("{0},{1} 1", location.Latitude_Decimal, location.Longitude_Decimal) } } }
                 } }
                };
            }
            catch (Exception ex) { Manager.OnNotify("error", "CAP creation failed: {0}", ex.Message); }
            return al;
        }

        private Message InstanceManage(Message request)
        {
            OnNotify("debug", request.ToString());

            Message response = new Message() { Source = myId, IsValid = true, Id = request.Id, Type = request.Type };

            if (!request.IsValid)
            {
                response.Type = "error";
                response.Add("Error", "Invalid request");
                return response;
            }

            if (request.Source?.Equals(myId) == true)   // Do nothing. it is the echo of my own message
                return null;

            switch (request.Type)
            {   //  publish RIO-TAD-TR-000-Mgmt "{ \"Source\": \"Manager\", \"Type\": \"list\", \"Id\": \"123\"}"
                case "status":
                    {
                        Dictionary<string, object> info = new Dictionary<string, object>
                        {
                            [settings.Id] = Status
                        };
                        info.AddRange(this.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)));
                        //foreach (string key in this.Keys)
                        //    info[key] = this[key];
                        //Dictionary<string, object> info = new Dictionary<string, object>(this.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)));

                        response.Parameters = info;
                    }
                    break;
                case "update":
                    {
                        if (request.Parameters == null) return null;
                        const string mediaParameterName = "Media";
                        response.Parameters = new Dictionary<string, dynamic>();
                        List<dynamic> report = new List<dynamic>();
                        foreach (string name in request.Parameters.Keys)
                            switch (name.ToLower())
                            {
                                case "media":
                                    foreach (dynamic media in request.Parameters[name])
                                    {
                                        if ("DELETE".Equals(media.Action?.ToString()))
                                            try
                                            {
                                                File.Delete(media.FileName.ToString());
                                                report.Add(new { MediaId = media.MediaId.ToString(), Action = "CONFIRM" });
                                            }
                                            catch
                                            {
                                                report.Add(new { MediaId = media.MediaId.ToString(), Action = "ERROR" });
                                                OnNotify("Manager", $"Unable to delete file {media.FileName.ToString()}");
                                            }
                                    }
                                    foreach (dynamic media in request.Parameters[name])
                                    {
                                        if ("ADD".Equals(media.Action?.ToString()))
                                            try
                                            {
                                                // TODO handle certificates correctly
                                                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                                                WebClient wc = new WebClient();
                                                wc.DownloadFile(media.Url.ToString(), media.FileName.ToString());
                                                report.Add(new { MediaId = media.MediaId.ToString(), Action = "CONFIRM" });
                                            }
                                            catch (WebException ex)
                                            {
                                                OnNotify("Manager", $"Unable to download file {media.FileName}");
                                                report.Add(new { MediaId = media.MediaId.ToString(), Action = "ERROR" });
                                            }
                                    }
                                    response.Parameters[mediaParameterName] = report.ToArray();
                                    break;
                            }
                    }
                    break;
                case "schedule":
                    {
                        if (request.Parameters.Count == 0)
                        {
                            response.Type = "scheduler";
                            response.Parameters = new Dictionary<string, dynamic>
                            {
                                ["crontab"] = cron.ToArray(),
                                ["commands"] = cron.Commands
                            };
                            break;
                        }
                        if (request.Parameters.TryGetValue("command", out dynamic command))
                        {
                            response.Parameters = new Dictionary<string, dynamic>();
                            lock (variables)
                            {
                                variables["RIO_Id"] = Id;
                                variables["RIO_Version"] = Version;
                                variables["RIO_Start"] = StartTime;

                                variables["RIO_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy");
                                variables["RIO_Time"] = DateTime.UtcNow.ToString("HH:mm:ss");
                            }
                            switch (command)
                            {
                                case "get":
                                    if (request.Parameters.Count == 1)
                                    {
                                        response.Type = "Variables";
                                        response.Parameters.AddRange(Variables);
                                        break;
                                    }
                                    if (request.Parameters.TryGetValue("names", out dynamic names))
                                    {
                                        response.Type = "Variables";
                                        response.Parameters = new Dictionary<string, dynamic>();
                                        var vars = Variables;
                                        foreach (string name in names)
                                            foreach (string variable in vars.Keys)
                                                if (variable.StartsWith(name))
                                                    response.Parameters[variable] = vars[variable];

                                        break;
                                    }
                                    break;
                                case "set":
                                    foreach (string name in request.Parameters.Keys.Where(k => !k.Equals("command")))
                                    {
                                        foreach (string variable in variables.Keys.ToArray())
                                            if (variable.StartsWith(name))
                                                response.Parameters[variable] =
                                                    variables[variable] = request.Parameters[name];
                                    }
                                    break;
                                case "reload":
                                    {
                                        string result = cron.Reload();
                                        response.Type = "Execution Result";
                                        response.Parameters = new Dictionary<string, dynamic>
                                        {
                                            ["Execution Result"] = result
                                        };
                                        OnNotify("Scheduler", result);
                                    }
                                    break;
                                case "debug":
                                    {
                                        response.Type = "Execution Result";
                                        response.Parameters = new Dictionary<string, dynamic>
                                        {
                                            ["Execution Result"] = cron.Rules.ToArray()
                                        };
                                    }
                                    break;
                                default:
                                    {
                                        response.Type = "Execution Result";
                                        response.Parameters = new Dictionary<string, dynamic>();
                                        if (!cron.Execute(command, response, request.Parameters))
                                            response.Parameters.Add("Error", string.Format("Command {0} not found", command));
                                    }
                                    break;
                            }
                        }
                    }
                    break;
                case "config":
                    {
                        bool changed = false;
                        try
                        {
                            response.Parameters = new Dictionary<string, dynamic>
                            {
                                // Always report the RIO configuration
                                { RIO_FEATURE_NAME, Status }
                            };

                            if (request.Parameters.TryGetValue("target", out dynamic module) &&
                               (settings.Id.Equals(module) || RIO_FEATURE_NAME.Equals(module)))
                            {
                                foreach (string key in request.Parameters?.Keys)
                                {
                                    switch (key)
                                    {
                                        case "LocalManagement":
                                            settings.LocalManagement = request.Parameters[key];
                                            break;
                                        case "Location":
                                            settings.Location = request.Parameters[key];
                                            break;
                                        case "Queue":
                                            settings.Queue = request.Parameters[key];
                                            break;
                                        case "WebAccess":
                                            settings.WebAccess = request.Parameters[key];
                                            break;
                                        case "WebProxy":
                                            settings.WebProxy = request.Parameters[key];
                                            break;
                                        case "EnableSlack":
                                            settings.WebProxy = request.Parameters[key];
                                            break;
                                    }
                                    settings.OnChanged(key);
                                }
                            }
                            else
                            {
                                Feature[] features = SelectFeatures(request, true);
                                foreach (Feature feature in features)
                                {
                                    IFeature driver = Plugins.FirstOrDefault(d => d.Name.Equals(feature.Type));
                                    if (driver == null) continue;

                                    if (request.Parameters.Count > 0)
                                    {
                                        foreach (Property item in driver.Configuration)
                                        {
                                            string propertyName = item.Name;
                                            if (request.Parameters.TryGetValue(item.Name, out object o))
                                            {
                                                changed = true;
                                                lock (variables)
                                                {
                                                    variables[string.Format("{0}_{1}", feature.Id, propertyName)] =
                                                        feature.Properties[propertyName] = o ?? item.Default;
                                                }
                                            }
                                        }
                                    }
                                    response.Parameters.Add(feature.Id, feature.Properties);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            response.Add("error", string.Format("Request failed: {0}", ex.Message));
                        }
                        finally { if (changed) settings.OnChanged(nameof(settings.Features)); }
                        response.Type = "config";
                        //if (!used) response.Add("error", "No feature selected");
                    }
                    break;
                case "enable":
                    {
                        bool changed = false;
                        try
                        {
                            Feature[] features = SelectFeatures(request);
                            foreach (Feature feature in features.Where(f => !f.Enabled))
                            {
                                changed = true;
                                feature.Enabled = true;
                                OnNotify("Manager", "--> {0}", feature.Id);
                                IFeature plugin = Plugins.FirstOrDefault(p => p.GetType().Name.Equals(feature.Type));
                                if (plugin != null)
                                    foreach (ITask task in plugin.Setup(settings, feature))
                                    {
                                        tasks.Add(task);
                                        this[task.Name] = task;

                                        response.Add("target", task.Name);
                                    }
                            }
                        }
                        finally { if (changed) settings.OnChanged(nameof(settings.Features)); }
                        if (!changed) response.Add("error", "No feature selected");
                    }
                    break;
                case "disable":
                    {
                        bool changed = false;
                        try
                        {
                            Feature[] features = SelectFeatures(request);
                            foreach (Feature feature in features.Where(f => f.Enabled))
                            {
                                changed = true;
                                feature.Enabled = false;
                                OnNotify("Manager", "--! {0}", feature.Id);
                                response.Add("target", feature.Id);
                            }
                        }
                        finally { if (changed) settings.OnChanged(nameof(settings.Features)); }
                        if (!changed) response.Add("error", "No feature selected");
                    }
                    break;
                case "start":
                    {
                        bool changed = false;
                        try
                        {
                            Feature[] features = SelectFeatures(request);
                            foreach (Feature feature in features.Where(f => f.Enabled))
                                foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)))
                                {
                                    changed = true;
                                    task.Start();
                                    response.Add("target", task.Name);
                                }
                        }
                        catch { }
                        if (!changed) response.Add("error", "No feature selected");
                    }
                    break;
                case "stop":
                    {
                        bool changed = false;
                        try
                        {
                            Feature[] features = SelectFeatures(request);
                            foreach (Feature feature in features)
                                foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)).ToArray())
                                {
                                    changed = true;
                                    task.Stop();
                                    if (!feature.Enabled)
                                        tasks.Remove(task);
                                    response.Add("target", task.Name);
                                }
                        }
                        catch { }
                        if (!changed) response.Add("error", "No feature selected");
                    }
                    break;
                case "list":
                    {
                        List<string> requested = new List<string>();
                        bool all = false;
                        if (request.Parameters?.ContainsKey("selection") == true)
                        {
                            if (request.Parameters["selection"] is IEnumerable<dynamic> modules)
                                requested.AddRange(modules.Select<dynamic, string>(m => m.ToString()));
                            else
                                requested.Add(request.Parameters["selection"]);
                        }
                        else
                            all = true;
                        response = new Message() { Source = myId, Id = request.Id, Type = "list", Parameters = new Dictionary<string, object>() };
                        if (requested.Contains("features") || all)
                            response.Parameters["features"] = settings.Features
                                .Union(new Feature[] { AsFeature() }).ToArray();
                        if (requested.Contains("tasks") || all)
                            response.Parameters["tasks"] = this.Where(kv => kv.Value is ITask).Select(kv => new
                            {
                                Name = kv.Key,
                                Settings = kv.Value,
                                kv.Value.Metrics,
                                (kv.Value as ITask).Version
                            }).ToList();
                        if (requested.Contains("drivers") || all)
                            response.Parameters["drivers"] = Plugins.ToArray();
                    }
                    break;
                case "help":
                    {
                        try
                        {
                            response.Parameters = new Dictionary<string, object>();
                            string action = null;
                            Feature[] features = SelectFeatures(request, true);
                            if (request.Parameters.ContainsKey("action"))
                            {
                                action = request.Parameters["action"].ToString();
                                foreach (Feature feature in features)
                                {
                                    string id = string.Format("{0}+{1}", feature.Type, action);
                                    if (AvailableCommands.ContainsKey(id))
                                    {
                                        Command cmd = AvailableCommands[id];
                                        if (cmd.Parameters.Any())
                                            response.Add(id, cmd.Parameters.Select(p => p.ToString()).ToArray());
                                        else
                                            response.Add(id, string.Empty);
                                    }
                                }
                            }
                            else
                            {
                                foreach (Feature feature in features)
                                {
                                    var cmds = AvailableCommands.Values.Where(cmd => cmd.Target.Equals(feature.Type));
                                    if (cmds.Any())
                                        response.Add(feature.Type, cmds.Select(cmd => cmd.Name).ToArray());
                                }
                                response.Add(RIO_FEATURE_NAME,
                                    AvailableCommands.Values.Where(cmd => cmd.Target.Equals(RIO_FEATURE_NAME))
                                    .Select(cmd => cmd.Name).ToArray());
                                return response;
                            }
                        }
                        catch { }
                    }
                    break;
                case "exec":
                    {
                        try
                        {
                            response.Type = "Execution Result";

                            string action = null;
                            if (request.Parameters.ContainsKey("action"))
                                action = request.Parameters["action"].ToString();
                            else
                            {
                                response.Add("Error", "Unknown action");
                                return response;
                            }

                            bool found = false;
                            Feature[] features = SelectFeatures(request);
                            foreach (Feature feature in features.Where(f => f.Enabled))
                                if (RIO_FEATURE_NAME.Equals(feature.Id) || settings.Id.Equals(feature.Id))
                                    try
                                    {
                                        OnNotify("Manager", "{0}: {1}", RIO_FEATURE_NAME, action);
                                        Execute(action, response, request.Parameters);
                                        found = true;
                                    }
                                    catch (Exception ex) { response.Add("Error", RIO_FEATURE_NAME, ex.Message); }
                                else foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)))
                                        try
                                        {
                                            OnNotify("Manager", "{0}: {1}", task.Name, action);
                                            Execute(task, action, response, request.Parameters);
                                            found = true;
                                        }
                                        catch (Exception ex) { response.Add("Error", task.Name, ex.Message); }
                            if (!found)
                            {   // No command found, try by ruleset and scheduler
                                found = cron?.Execute(action, response, request.Parameters) == true
                                        || ruleEngine?.Execute(action, response, request.Parameters) == true;
                            }
                            if (!found)
                            {
                                response.Add("Error", $"Action {action} not found");
                                return response;
                            }
                        }
                        catch { }
                    }
                    break;
                case "shutdown":
                    {   //  publish RIO-TAD-00Y-Mgmt "{ \"Source\": \"Manager\", \"Type\": \"shutdown\"}"
                        response = new Message() { Source = myId, Type = "Shutdown", Parameters = new Dictionary<string, object>() };
                        response.Parameters["By"] = request.Source;
                        if (DateTime.UtcNow < shutdownExpire)
                        {
                            response.Parameters["Status"] = "Confirmed";
                            OnNotify("Shutdown", "Shutting down immediately");
                            Shutdown();
                        }
                        else
                        {
                            response.Parameters["Status"] = "Requested";
                            OnNotify("Shutdown", "Shutdown requested");
                            shutdownExpire = DateTime.UtcNow.AddSeconds(10);
                        }
                    }
                    break;
                case "ruleset":
                    {
                        if (request.Parameters != null && request.Parameters.ContainsKey("reload"))
                        {
                            RuleEngine re = RuleEngine.LoadConfiguration("Ruleset.json");
                            string result = "Unable to install the new ruleset";
                            if (re != null)
                            {
                                ruleEngine = re;
                                result = string.Format("New ruleset installed: {0} rules", re.Ruleset.Count);
                            }
                            OnNotify("Rule Engine", result);
                        }
                        if (request.Parameters == null || (!request.Parameters.ContainsKey("Translations") && !request.Parameters.ContainsKey("Ruleset")))
                        {
                            response.Source = myId;
                            response.Parameters = new Dictionary<string, object>() {
                                    { "Ruleset", RuleEngine.Ruleset },
                                    { "Devices", RuleEngine.Devices.Where(name => !name.Equals("RIO.MGMT")) },
                                    { "Translations", RuleEngine.Translations.Where(kv => !kv.Key.Equals("RIO.MGMT")).ToExpando() },
                                    { "Actions", RuleEngine.Actions }
                            };
                            break;
                        }
                        if (!request.Parameters.ContainsKey("Translations") || !request.Parameters.ContainsKey("Ruleset"))
                        {
                            response.Source = myId;
                            response.Parameters = new Dictionary<string, object>() {
                                {"status","incomplete" }
                            };
                            break;
                        }
                        Dictionary<string, string> translations = ParseDictionary(request.Parameters["Translations"]);
                        if (translations == null)
                        {
                            response.Source = myId;
                            response.Parameters = new Dictionary<string, object>() {
                                {"error","Invalid translation table" }
                            };
                            break;
                        }
                        Dictionary<string, IList<Execution>> actions = ParseActions(request.Parameters["Actions"]);
                        IList<Rule> rules = ParseRules(request.Parameters["Ruleset"], actions);
                        if (rules == null)
                        {
                            response.Source = myId;
                            response.Parameters = new Dictionary<string, object>() {
                                {"error","Invalid rules set" }
                            };
                            break;
                        }
                        ConfigureEngine(translations, rules, actions);
                        response.Source = myId;
                        response.Parameters = new Dictionary<string, object>() {
                                {"status","Ruleset uploaded" }
                            };
                    }
                    break;
                case "name":
                    {
                        dynamic s = null;
                        if (request.Parameters?.TryGetValue("id", out s) != false)
                        {
                            //string s = request.Parameters["id"];
                            Settings.Id = s;
                        }
                        response.Source = myId;
                        response.Parameters = new Dictionary<string, object>() {
                                {"id", Settings.Id }
                            };
                    }
                    break;
                default:
                    return null;
            }
            if (response != null)
            {
                response.Source = myId;
                response.IsValid = true;
            }
            return response;
        }
        /// <summary>
        /// Return the first <see cref="ITask"/> definition matching either the type or the id with the id parameter.
        /// </summary>
        /// <param name="id">Either the type or the id or the task to search for.</param>
        /// <returns>The <see cref="Feature"/> describing the <see cref="ITask"/>.</returns>
        public static Feature FindFeature(string id)
        {
            return Instance.InstanceGetFeature(id);
        }
        /// <summary>
        /// Stops all the <see cref="ITask"/>s matching <paramref name="id"/> either with the <see cref="Feature.Id"/>
        /// or with the <see cref="Feature.Type"/>.
        /// </summary>
        /// <param name="id">The <see cref="Feature.Id"/> or the <see cref="Feature.Type"/></param>
        /// <returns>true, if at least one task was stopped.</returns>
        public static bool Stop(string id)
        {
            return Instance.InstanceStop(id);
        }

        /// <summary>
        /// Starts all the <see cref="ITask"/>s matching <paramref name="id"/> either with the <see cref="Feature.Id"/>
        /// or with the <see cref="Feature.Type"/>.
        /// </summary>
        /// <param name="id">The <see cref="Feature.Id"/> or the <see cref="Feature.Type"/></param>
        /// <returns>true, if at least one task was started.</returns>
        public static bool Start(string id)
        {
            return Instance.InstanceStart(id);
        }
        /// <summary>
        /// Configure all the <see cref="ITask"/>s matching <paramref name="id"/> either with the <see cref="Feature.Id"/>
        /// or with the <see cref="Feature.Type"/> according to <paramref name="settings"/>.
        /// </summary>
        /// <param name="id">The <see cref="Feature.Id"/> or the <see cref="Feature.Type"/></param>
        /// <param name="settings">The new configuration.</param>
        /// <returns>true, if at least one task was started.</returns>
        public static bool Configure(string id, Feature settings)
        {
            return Instance.InstanceConfigure(id, settings);
        }

        private bool InstanceStop(string id)
        {
            bool changed = false;
            try
            {
                Feature[] features = SelectFeatures(id).ToArray();
                foreach (Feature feature in features)
                    foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)).ToArray())
                    {
                        changed = true;
                        task.Stop();
                        tasks.Remove(task);
                        OnNotify("Manager", "Task {0} terminated", task.Name);
                    }
            }
            catch { }
            return changed;
        }

        private bool InstanceStart(string id)
        {
            bool changed = false;
            try
            {
                Feature[] features = SelectFeatures(id).ToArray();
                foreach (Feature feature in features)
                {
                    if (feature.Enabled)
                    {
                        if (!tasks.Any(t => t.Feature.Equals(feature)))
                        {
                            foreach (string key in feature.Properties.Keys)
                            {
                                //OnNotify(feature.Id, "{0} = {1}", key, feature.Properties[key]);
                                variables[string.Format("{0}_{1}", feature.Id, key)] = feature.Properties[key];
                            }

                            string log = string.Empty;
                            //OnNotify(feature.Id, log);
                            try
                            {
                                IFeature plugin = Plugins.FirstOrDefault(p => p.GetType().Name.Equals(feature.Type));
                                foreach (ITask task in plugin.Setup(settings, feature))
                                {
                                    log += string.Format("{1}{0} ", task.Status, log);
                                    tasks.Add(task);
                                }
                            }
                            catch (Exception ex)
                            {
                                log += ex.Message;
                            }
                            OnNotify(feature.Id, log);
                        }
                        foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)).ToArray())
                        {
                            changed = true;
                            try
                            {
                                task.Start();
                                OnNotify("Manager", "Task {0} started", task.Name);
                            }
                            catch (Exception ex)
                            {
                                OnNotify("Manager", "Starting {0} error: {1}", task.Name, ex.Message);
                            }
                        }
                    }
                }
            }
            catch { }
            return changed;
        }

        private bool InstanceConfigure(string id, Feature settings)
        {
            bool changed = false;
            try
            {
                Feature[] features = SelectFeatures(id).ToArray();
                switch (features.Length)
                {
                    case 0:
                        Settings.Features.Add(settings);

                        return true;
                    case 1:
                        Settings.Features.Remove(features[0]);
                        Settings.Features.Add(settings);
                        return true;
                    default:
                        return false;
                }
            }
            catch { }
            return changed;
        }

        private Feature InstanceGetFeature(string id)
        {
            try
            {
                Feature[] features = SelectFeatures(id).ToArray();
                if (features.Length == 0)
                    return null;

                return features[0];
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Feature AsFeature()
        {
            Feature me = new Feature()
            {
                Id = Settings.Id,
                Enabled = true,
                Type = RIO_FEATURE_NAME,
                Version = Version
            };
            if (TryGetValue("location", out IMeasurable service) && service is Service location)
            {
                if (location.Metrics is LocationMetrics metrics)
                    settings.Location = $"{metrics.Latitude_Decimal},{metrics.Longitude_Decimal}";
            }
            me.Properties.AddRange<string, object>(
                    new Dictionary<string, dynamic>()
                    {
                        { "LocalManagement",settings.LocalManagement},
                        { "Location",settings.Location ?? string.Empty},
                        { "Queue",settings.Queue ?? string.Empty},
                        { "WebAccess",settings.WebAccess ?? string.Empty},
                        { "WebProxy",settings.WebProxy ?? string.Empty },
                        { "StartTime", StartTime }
                    });
            return me;
        }
        private Dictionary<string, dynamic> Status =>
            new Dictionary<string, dynamic>()
                                {
                                    {"Version",Version },
                                    {"Name",RIO_FEATURE_NAME },
                                    {"Feature",AsFeature()},
                                    {"Status", string.Empty},
                                    {"Metrics", string.Empty },
                                    { "StartTime", StartTime }
                                };

        private static readonly object lkHistory = new object();
        private static readonly Dictionary<object, List<string>> AllHistories = new Dictionary<object, List<string>>();
        private static string GetHistory(object reference, string text)
        {
            if (reference == null)
                return null;

            lock (lkHistory)
            {
                if (!AllHistories.ContainsKey(reference))
                    AllHistories[reference] = new List<string>();

                int idx = 0;
                if (text.Contains("!!") && AllHistories[reference].Count > 0)
                {
                    bool justRepeat = text.Trim().Equals("!!");    //  No need to put it twice in the history
                    text = text.Replace("!!", AllHistories[reference].Last());
                    if (justRepeat)
                        return text;
                }
                else if ((idx = text.IndexOf('!')) > -1)
                {
                    string historyReference = text.Substring(idx).Split(' ')[0];
                    if (int.TryParse(historyReference.Substring(1), out int index))
                    {
                        if (index - 1 < AllHistories[reference].Count)
                            text = text.Replace(historyReference, AllHistories[reference][index - 1]);
                        else
                            return string.Empty;
                    }
                    else
                    {
                        string cmd = string.Empty;
                        if ((cmd = AllHistories[reference].LastOrDefault(s => s.StartsWith(historyReference.Substring(1)))) != null)
                            text = text.Replace(historyReference, cmd);
                        else
                            return string.Empty;
                    }
                }
                if ("history".StartsWith(text))
                    text = "history";
                AllHistories[reference].Add(text);
            }
            return text;
        }

        static string[] GetHistory(object reference)
        {
            lock (lkHistory)
            {
                if (!AllHistories.ContainsKey(reference))
                    return Array.Empty<string>();
                return AllHistories[reference].ToArray();
            }
        }

        private static readonly string[] knownVerbs = new string[] { "schedule", "update", "config", "disable", "enable", "exec", "help", "list", "name", "retry", "ruleset", "shutdown", "start", "status", "stop", "test" };
        /// <summary>
        /// Extracts a <see cref="RIO.Message"/> from a command line as passed from an interactive shell like a socket connection or a console input.
        /// </summary>
        /// <param name="text">The string with the user input</param>
        /// <param name="reference">The object the history is related to, e.g. the interactive session or the remote connection.</param>
        /// <returns>A <see cref="RIO.Message"/> containing the parsed command. Check the <see cref="RIO.Message.IsValid"/> property to verify it was succesfully parsed.</returns>
        public static Message ParseCommandLine(string text, object reference = null)
        {
            text = ManageEditing(text);

            Message message = new Message();
            if (string.IsNullOrWhiteSpace(text))
                return message;

            // History management
            string history = GetHistory(reference, text);
            if (!string.IsNullOrEmpty(history))
                text = history;

            string[] fields = text.Trim().SplitQuotedRE(new char[] { ' ' }).ToArray();
            if ("history".Equals(text))
            {
                message.IsValid = true;
                message.Type = "history";
                message.Parameters = new Dictionary<string, dynamic>();

                int line = 0;
                Dictionary<int, string> commands = new Dictionary<int, string>();
                commands.AddRange(GetHistory(reference).Select(s => new KeyValuePair<int, string>(++line, s)));
                if (fields.Length > 1)
                {
                    message.Parameters["history"] = commands
                        .Where(kv => kv.Value.Contains(fields[1]))
                        .Select(kv => string.Format("{0:####}. {1}", kv.Key, kv.Value));
                }
                else
                {
                    message.Parameters["history"] = commands.Select(kv => string.Format("{0:####}. {1}", kv.Key, kv.Value));
                }
                return message;
            }

            int idx = 0;

            //if (fields.Length < 2)
            //    return message;

            if (knownVerbs.Contains(fields[0]))
            { message.Type = fields[idx++]; }
            else
                message.Type = "exec";

            if (message.Parameters == null) message.Parameters = new Dictionary<string, dynamic>();
            if (message.Type.Equals("test"))
            {
                message.Parameters["tests"] = fields.Skip(idx).ToArray();
                message.IsValid = true;
                return message;
            }
            if (message.Type.Equals("schedule") && fields.Length > 1)
            {
                switch (fields[1])
                {
                    case "get":
                        if (fields.Length > 2)
                            message.Parameters["names"] = fields.Skip(2).ToArray();
                        break;
                    case "set":
                        if (fields.Length > 3)
                        {
                            for (int i = 2; i < fields.Length - 1; i += 2)
                                message.Parameters[fields[i]] = fields[i + 1];
                        }
                        break;
                    default:
                        if (fields.Length > 2)
                        {
                            for (int i = 2; i < fields.Length - 1; i += 2)
                                message.Parameters[fields[i]] = fields[i + 1];
                        }
                        break;
                }
                message.Parameters["command"] = fields[1];
                message.IsValid = true;
                return message;
            }
            if ((new string[] { "disable", "enable", "start", "stop" }).Contains(message.Type)
                && !fields.Contains("target") && idx < fields.Length)
            {
                message.Parameters["target"] = fields.Skip(idx).ToArray();
                idx = fields.Length;
            }
            if ((new string[] { "config", "exec", "help" }).Contains(message.Type))
            {
                if (!fields.Contains("target") && idx < fields.Length)
                    message.Parameters["target"] = fields[idx++];
            }
            if (message.Type.Equals("exec") || message.Type.Equals("help"))
            {
                if (!fields.Contains("action") && idx < fields.Length)
                    message.Parameters["action"] = fields[idx++];
            }
            if (message.Type.Equals("list") && !fields.Contains("selection") && idx < fields.Length)
            {
                message.Parameters["selection"] = fields.Skip(idx).ToArray();
                idx = fields.Length;
            }

            for (; idx < fields.Length; idx++)
            {
                string key = fields[idx++];
                string value = string.Empty, s;
                if (idx < fields.Length)
                {
                    if (fields[idx].StartsWith("("))
                    {
                        List<string> values = new List<string>();
                        do
                        {
                            s = fields[idx].Trim('(', ')', ' ');
                            if (!string.IsNullOrEmpty(s))
                                values.Add(s);
                        }
                        while (!fields[idx++].EndsWith(")") && idx < fields.Length);

                        message.Parameters[key] = values.ToArray();
                        //message.Parameters[key] = string.Join(" ", "(", string.Join(" ", values.ToArray()), ")");
                        continue;
                    }
                    else
                        value = fields[idx];
                }

                message.Parameters[key] = value;
            }
            message.IsValid = true;
            return message;
        }

        private static string ManageEditing(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\x08')
                {
                    if (sb.Length > 0)
                        sb.Remove(sb.Length - 1, 1);
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static Dictionary<string, IList<Execution>> ParseActions(dynamic actions)
        {
            Dictionary<string, IList<Execution>> retVal = new Dictionary<string, IList<Execution>>();

            foreach (JProperty jProperty in actions)
            {
                string preset = jProperty.Name;
                List<Execution> commands = new List<Execution>();
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
                        commands.Add(new Execution() { Target = target, Command = cmd, Parameters = parameters });
                }
                if (commands.Count > 0)
                    retVal[preset] = commands;
            }

            return retVal;
        }
        private static IList<Rule> ParseRules(dynamic tuples, Dictionary<string, IList<Execution>> actions)
        {
            List<Rule> result = new List<Rule>();

            try
            {
                foreach (dynamic dyn in tuples)
                {
                    Rule rule = new Rule()
                    {
                        Id = dyn.Id,
                        Expression = dyn.Expression,
                        TimeTrigger = TimeSpan.Parse(dyn.TimeTrigger.ToString()),
                        ActionList = dyn.Actions
                    };
                    if (actions.TryGetValue(rule.ActionList, out IList<Execution> commands))
                    {
                        rule.Actions = commands;
                        result.Add(rule);
                    }
                }
            }
            catch { return null; }

            return result;
        }

        private Command GetCommand(string target, string action)
        {
            Feature executor = settings.Features.FirstOrDefault<Feature>(f => f.Id.Equals(target));
            if (executor == null)
                return null;

            IFeature plugin = Plugins.FirstOrDefault(f => executor.Type.Equals(f.GetType().Name));
            if (plugin == null)
                return null;

            foreach (Command command in plugin.Commands)
                if (action.Equals(command.Name))
                    return command;

            return null;
        }

        private static Dictionary<string, string> ParseDictionary(dynamic tuples)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                foreach (dynamic dyn in tuples)
                    result[dyn.Name] = dyn.Value.ToString();
            }
            catch { return null; }
            return result;
        }
        private static Dictionary<string, dynamic> ParseDictionaryStringDynamic(dynamic tuples)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            foreach (dynamic dyn in tuples)
                result[dyn.Name] = dyn.Value is JValue ? dyn.Value.Value : dyn.Value;
            return result;
        }

        /// <summary>
        /// Process the information present in a CAP message, including testing the rules and taking the appropriate actions, if needed.
        /// </summary>
        /// <param name="alert">The CAP message.</param>
        /// <returns></returns>
        public static async Task ManageAlert(alert alert)
        {
            try
            {
                await Instance.InstanceManageAlert(alert);
            }
            catch (Exception ex)
            {
                OnNotify("error", string.Format("Status failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Parse a CAP message from a JSON text, then processes it, including testing the rules and taking the appropriate actions, if needed.
        /// </summary>
        /// <param name="alertText">The CAP format is XML: this is a JSON serialization of the same object that can be obtained by the XML, a <see cref="JRC.CAP.alert"/>.</param>
        public static async Task ManageJsonAlert(string alertText)
        {
            try
            {
                await Instance.InstanceManageAlert(alertText);
            }
            catch (Exception ex)
            {
                OnNotify("error", string.Format("Status failed: {0}", ex.Message));
            }
        }

        private async Task InstanceManageAlert(string alertText)
        {
            Debug.WriteLine(alertText);

            alert alert = alert.ParseJson(alertText);
            await InstanceManageAlert(alert);
        }

        private async Task InstanceManageAlert(alert alert)
        {
            (List<Rule> passed, _, _, List<Execution> actions) = RuleEngine.Process(alert);
            try { cron.Update(alert); }
            catch { }

            Scheduler.UntilFalse(passed);

            foreach (Execution action in actions)
                OnNotify("Manager", Execute(action));
        }

        private static void Compose(Dictionary<string, dynamic> parameters, alert alert)
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
        /// Use this method to find the <see cref="Command"/> called <paramref name="command"/>, that can be performed
        /// by a <see cref="ITask"/> with either <see cref="Feature.Type"/> or <see cref="Feature.Id"/> matching with
        /// <paramref name="target"/>.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        public static Command FindCommand(string target, string command)
        {
            string fullname = string.Format("{0}+{1}",
                "RIO".Equals(target) ? target :
                Instance.settings.Features.Any(f => f.Type.Equals(target)) ? target
                : Instance.settings.Features.FirstOrDefault(f => f.Id.Equals(target))?.Type,
                command);
            return AvailableCommands.ContainsKey(fullname) ? AvailableCommands[fullname] : null;
        }
        /// <summary>
        /// Use this method to find all <see cref="Command"/>s defined in <see cref="Feature.Type"/> <paramref name="target"/>.
        /// If <paramref name="extended"/> is true, the research will be extended to <see cref="Feature.Id"/> also.
        /// </summary>
        /// <param name="target"><see cref="Feature.Type"/> or <see cref="Feature.Id"/> to look for.</param>
        /// <param name="extended">If true, the research will include also the <see cref="Feature.Id"/></param>
        /// <returns>The requested <see cref="Command"/>, or null if not found.</returns>
        public static IEnumerable<Command> FindCommands(string target, bool extended = false)
        {
            if (Instance.settings.Features.Any(f => f.Type.Equals(target)))
            {
                foreach (Command cmd in AvailableCommands.Values.Where(c => c.Target.Equals(target)))
                    yield return cmd;
                yield break;
            }
            else if (extended)
            {
                if (Instance.settings.Features.Any(f => f.Id.Equals(target)))
                {
                    string featureType = Instance.settings.Features.FirstOrDefault(f => f.Id.Equals(target))?.Type;
                    foreach (Command cmd in AvailableCommands.Values.Where(c => c.Target.Equals(featureType)))
                        yield return cmd;
                    yield break;
                }
            }
        }

        /// <summary>
        /// Use this method to find a <see cref="Command"/> named <paramref name="name"/> defined either in
        /// <see cref="Feature.Type"/> or <see cref="Feature.Id"/> equals to <paramref name="target"/> and return it in
        /// <paramref name="command"/>.
        /// </summary>
        /// <param name="target"><see cref="Feature.Type"/> or <see cref="Feature.Id"/> to look for.</param>
        /// <param name="name">The name of the <see cref="Command"/></param>
        /// <param name="command">The <see cref="Command"/> to assign the found value to.</param>
        /// <returns>true if found, or false if not found.</returns>
        public static bool FindCommand(string target, string name, out Command command)
        {
            command = FindCommand(target, name);
            return command != null;
        }

        private IEnumerable<Feature> SelectFeatures(string module)
        {
            List<Feature> result = new List<Feature>();
            Feature[] features = settings.Features.Where(f => f.Type.CompareTo(module.ToString()) == 0).ToArray();
            if (features.Length == 0)
                features = settings.Features.Where(f => f.Id.CompareTo(module.ToString()) == 0).ToArray();
            result.AddRange(features);
            if (module.Equals(settings.Id) || module.Equals(RIO_FEATURE_NAME))
                result.Add(AsFeature());

            return result;
        }

        private Feature[] SelectFeatures(Message request, bool notEmpty = false)
        {
            List<Feature> result = new List<Feature>();
            if (request.Parameters.ContainsKey("target"))
            {
                if (request.Parameters["target"] is IEnumerable<dynamic> modules)
                    foreach (var module in modules)
                        result.AddRange(SelectFeatures(module.ToString()));
                else
                    result.AddRange(SelectFeatures(request.Parameters["target"].ToString()));
                request.Parameters.Remove("target");
            }
            if (result.Count == 0 && notEmpty)
                result.AddRange(settings.Features);
            return result.ToArray();
        }

        #region Execute
        private object Execute(string action, Message response, Dictionary<string, object> parameters)
        {
            if (AvailableCommands.TryGetValue(string.Format("{0}+{1}", RIO_FEATURE_NAME, action), out Command command))
            {
                return command.Execute(null, this, response, parameters);
            }
            return null;
        }

        private object Execute(ITask instance, string action, Message response, Dictionary<string, dynamic> parameters)
        {
            IFeature plugin = Plugins.FirstOrDefault(f => instance.Feature.Type.Equals(f.GetType().Name));
            if (plugin == null) return null;

            //Dictionary<string, dynamic> input = new Dictionary<string, dynamic>();
            //Extensions.AddRange(input, parameters);
            //input["target"] = instance.Name;
            //input["action"] = action;
            //response.Parameters["command"] = input;

            parameters["IDdevice"] = Settings.Id;
            parameters["Settings"] = Settings;
            foreach (Command command in plugin.Commands)
            {
                if (action.Equals(command.Name))
                    return command.Execute(instance, this, response, parameters);
            }
            return null;
        }

        /// <summary>
        /// Requested the specified target to execute a command named action with the parameters. If requested by another task implements an IPC mechanism.
        /// </summary>
        /// <param name="target">The target of the request: all tasks of a feature or a task.</param>
        /// <param name="action">The name of the command to be executed.</param>
        /// <param name="parameters">The parameters to be passed to the command.</param>
        /// <returns></returns>
        public static async Task<Message> Execute(string target, string action, Dictionary<string, object> parameters)
        {
            return await Task.Run<Message>(() => Instance.InstanceExecute(target, action, parameters));
        }
        /// <summary>
        /// Request to exec an <see cref="Execution"/> and return the Execution Result in a <see cref="Message"/>.
        /// </summary>
        /// <param name="execution">A </param>
        /// <returns></returns>
        public static Message Execute(Execution execution)
        {
            return Instance.InstanceExecute(execution);
        }

        private Message InstanceExecute(Execution execution)
        {
            lock (variables)
            {
                variables["RIO_Id"] = Id;
                variables["RIO_Version"] = Version;
                variables["RIO_Start"] = StartTime;

                variables["RIO_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy");
                variables["RIO_Time"] = DateTime.UtcNow.ToString("HH:mm:ss");
            }
            Message response = new Message() { Source = Instance.myId, Type = "Execution Result", Parameters = new Dictionary<string, object>() };

            try
            {
                if (RIO_FEATURE_NAME.Equals(execution.Target) || settings.Id.Equals(execution.Target))
                    InternalExecution(execution, response);   // Local command, like system.execute
                else
                    foreach (Feature feature in SelectFeatures(execution.Target).Where(f => f.Enabled))
                        foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)))
                            try
                            {
                                Execute(task, execution.Command.Name, response, execution.Parameters);
                            }
                            catch (Exception ex) { response.Add("Error", task.Name, ex.Message); }
            }
            catch { }

            return response;
        }

        private void InternalExecution(Execution execution, Message response)
        {
            try
            {
                switch (execution.Command.Name)
                {
                    case "execute":
                        execution.Command.Execute(null, this, response, execution.Parameters);
                        break;
                    default:
                        response.Parameters.Add("Error", string.Format("Command {0} not found", execution.Command.Name));
                        break;
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private Message InstanceExecute(string target, string action, Dictionary<string, object> parameters)
        {
            Message response = new Message() { Source = Instance.myId, Type = "Execution Result", Parameters = new Dictionary<string, object>() };

            try
            {
                if (RIO_FEATURE_NAME.Equals(target) || settings.Id.Equals(target))
                    ; // InternalExecution(execution, response);   // Local command, like system.execute
                else
                    foreach (Feature feature in SelectFeatures(target).Where(f => f.Enabled))
                        foreach (ITask task in tasks.Where(t => t.Feature.Equals(feature)))
                            try
                            {
                                Execute(task, action, response, parameters);
                            }
                            catch (Exception ex) { response.Add("Error", task.Name, ex.Message); }
            }
            catch { }

            return response;
        }
        #endregion

        #region Commands
        private class ExecuteCommand : Command
        {
            public ExecuteCommand()
            {
                Name = "execute";
                Target = "RIO";
            }

            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "program", Required = true },
                new Parameter { Type = "string", Name = "arguments", Required = false },
                new Parameter { Type = "*", Name = "parameters", Required = false }
            };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                string executable = parameters.program;
                string arguments = parameters.arguments;
                arguments = arguments.Substitute(Manager.Variables);
                arguments = Extensions.Substitute(arguments, parameters);

                Process process = Process.Start(executable, arguments);

                return process;
            }
        }

        private class AlertCommand : Command
        {
            public AlertCommand()
            {
                Name = "alert";
                Target = "RIO";
            }

            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "category", Required = true },
                new Parameter { Type = "string", Name = "eventType", Required = true },
                new Parameter { Type = "string", Name = "status", Required = false },
                new Parameter { Type = "*", Name = "parameters" }
            };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                alertStatus status = alertStatus.Actual;
                Enum.TryParse<alertStatus>(parameters.status, out status);
                alert alert = BuildAlert(Id, parameters.category, parameters.eventType, status, parameters.parameters);
                response.Add("Execution Result", "alert", alert.identifier);
                OnNotify("alert", alert);

                return alert.identifier;
            }
        }

        private class CancelCommand : Command
        {
            public CancelCommand()
            {
                Name = "cancel";
                Target = "RIO";
            }

            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "id", Required = true }
            };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                alert cancel = BuildCancel(Id, parameters.id);
                response.Add("Execution Result", "alert", cancel.identifier);
                OnNotify("alert", cancel);

                return cancel.identifier;
            }
        }
        #endregion

        /// <summary>
        /// Subscribers to this event will be notified about events happening to the tasks.
        /// <see cref="OnNotify(string, object, object[])"/>
        /// </summary>
        public static event EventHandler<object> Notify;
    }
}
