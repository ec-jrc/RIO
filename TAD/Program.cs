using JRC.CAP;
using Newtonsoft.Json;
using RIO;
using RIO.Communication;
using Serilog;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using stj = System.Text.Json;
using Test = System.Collections.Generic.Dictionary<string, RIO.Message[]>;

namespace TAD
{
    internal partial class Program
    {
        private const string HeartbeatChannel = "Heartbeat-Channel";
        private const string TelemetryChannel = "Telemetry-Channel";
        private const string AlertChannel = "RIO-TAD-Alert";
        private const string MgmtChannelFormat = "RIO-{0}-Mgmt";
        private const string DefaultConnectionString = "RIO-Article.redis.cache.windows.net:6380,password=vyk2tKiE79dwxq6TOAF7XeIh8NNPRePRhAzCaGGiFpw=,ssl=True,abortConnect=False";
        private static string QueueConnectionString = string.Empty;
        private static string MgmtChannelName;
        private static IChannel telemetry, management, alert;
        private static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyy-MM-dTHH:mm:ss.fffZ",
            Error = JsonSerializatioErrorManager
        };

        private static void JsonSerializatioErrorManager(object? sender, Newtonsoft.Json.Serialization.ErrorEventArgs e)
        {
            e.ErrorContext.Handled = true;
        }

        private static readonly stj.JsonSerializerOptions jsonSerializerOptions = new stj.JsonSerializerOptions { IgnoreNullValues = true };
        private static readonly object lkRecents = new object();
        private static readonly List<AlertNotification> recentCaps = new List<AlertNotification>();
        private static NetInterface server;
        private static SlackAPI.SlackSocketClient client;

        static string TadServerPost = "https://webcritech.jrc.ec.europa.eu/TAD_server/api/Data/PostAsync";
        private static Timer heartbeat;
        private static readonly Timer keepCapAlive;
        private static readonly DateTime lastCapReceived = DateTime.UtcNow;
        private static string settingsFilename = "Settings.json";

        private static readonly Lazy<Test> tests = new Lazy<Test>(Helpers.TestsLoader, true);

        public static Settings Settings { get; private set; }


        static async Task Main(string[] args)
        {
            if (args.Length == 1)
                settingsFilename = args[0];

            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            jsonSerializerOptions.Converters.Add(new TimeSpanConverter());
            jsonSerializerOptions.Converters.Add(new JArrayConverter());

            string path = Path.Combine(Path.GetTempPath(), "checkLog-.txt");
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(path, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Booting Core {0}", Manager.Version);
            Manager.Notify += WriteToLogger;

            try
            {
                LoadSettings(settingsFilename);
            }
            catch
            {
                Settings = null;
            }
            if (Settings == null)
            {
                Settings = new Settings
                {
                    Id = "RIO-Uninitialized_device"
                };

                SaveSettings(settingsFilename);
            }

            Log.Information("Settings loaded for {0}", Settings.Id);
            (Settings as INotifyPropertyChanged).PropertyChanged += new PropertyChangedEventHandler(SettingsChanged);

            await Manager.ConfigureAsync(Settings);
            Log.Information("RIO Configured!");

            string queue = string.IsNullOrWhiteSpace(Settings.Queue) ? DefaultConnectionString : Settings.Queue;
            QueueConnectionString = string.IsNullOrWhiteSpace(Settings.QueueCredentials) ?
                queue : string.Format("{0},password={1}", queue, Settings.QueueCredentials);
            if (!string.IsNullOrWhiteSpace(Settings.WebAccess))
            {
                TadServerPost = Settings.WebAccess;
            }

            MgmtChannelName = string.Format(MgmtChannelFormat, Settings.Id);
            StackExchange.Redis.ConfigurationOptions options = StackExchange.Redis.ConfigurationOptions.Parse(QueueConnectionString);
            options.AbortOnConnectFail = false;
            StackExchange.Redis.ConnectionMultiplexer redisConnection =
                StackExchange.Redis.ConnectionMultiplexer.Connect(options);
            RedisChannel hb = new RedisChannel(HeartbeatChannel, redisConnection, false);
            RedisChannel redis = new RedisChannel(TelemetryChannel, redisConnection, false);
            RedisChannel mgmt = new RedisChannel(MgmtChannelName, redisConnection);
            RedisChannel alert = new RedisChannel(AlertChannel, redisConnection);
            Log.Information("REDIS: Done");

            mgmt.Received += ManagementRequest;
            alert.Received += AlertReceived;
            //redis.Received += Telemetry_Received;
            WebChannel web = new WebChannel(TadServerPost);

            TeamChannel team = new TeamChannel(TeamLogic.Any, 0.4F, redis, web);
            RetryChannel retry = new RetryChannel(team);

            retry.CumulatedUnsent += StoreRetryBuffer;
            Program.telemetry = retry;
            Program.management = mgmt;
            Program.alert = alert;

            Manager.Instance["telemetry"] = telemetry;
            Manager.Instance["management"] = management;
            Manager.Instance["webTelemetry"] = web;
            Manager.Instance["msgTelemetry"] = redis;
            Log.Information("Communications started");

            Manager.Notify += NotifyInfo;
            Manager.Notify += Broadcast;

            Log.Information("Starting heartbeat");
            heartbeat = new Timer(async (o) =>
            {
                TransmissionResult r = await hb.Send(JsonConvert.SerializeObject(
                            new
                            {
                                Timestamp = DateTime.UtcNow,
                                Settings.Id
                            }, jsonSerializerSettings));
                switch (r)
                {
                    case TransmissionResult.Failed:
                        // Check connection
                        break;
                    case TransmissionResult.NoConnection:
                        // Manage reconnections
                        break;
                }
            }, null, 500, 60000);

            if (Settings.LocalManagement)
            {
                server = new NetInterface(4005);
                server.Received += Server_Received;
                server.Connected += Server_Connected;
                server.Error += Server_Error;
                if (!server.Start())
                {
                    Log.Information("Local connection already present. Aborting!");
                    return;
                }

                Log.Information("Local connection started");
            }
            if (Settings.EnableSlack)
            {
                InitializeSlack();
            }

            Log.Information("Ready to accept commands");

            Task.Run(async () => await CheckRetryBuffer());

            Log.Information("Starting modules");
            await Manager.RunAsync();
            if (Settings.LocalManagement)
            {
                server?.Stop();
            }
        }
        private static async void NotifyInfo(object sender, object e)
        {
            switch (sender.ToString())
            {
                case "alert":
                    {
                        //Task rules = Task.Run(() => AlertReceived(sender, e));
                        if (alert == null)
                        {
                            Log.Information("Alert channel disabled");
                            break;
                        }
                        var messageString = JsonConvert.SerializeObject(e, jsonSerializerSettings);
                        await alert.Send(messageString);

                        break;
                    }
                case "telemetry":
                    {
                        if (telemetry == null)
                        {
                            Log.Information("Telemetry channel disabled");
                            break;
                        }
                        var messageString = JsonConvert.SerializeObject(e, jsonSerializerSettings);
                        await telemetry.Send(messageString);
                        break;
                    }
                case "error":
                    {
                        if (management == null)
                        {
                            Log.Information("Management channel disabled");
                            break;
                        }
                        Dictionary<string, object> info = new Dictionary<string, object>() { { "info", e } };

                        RIO.Message response = new RIO.Message() { Type = "error", Source = Settings.Id, Parameters = info };
                        var messageString = JsonConvert.SerializeObject(response, jsonSerializerSettings);
                        await management.Send(messageString);

                        if (client?.IsConnected == true)
                        {
                            client.PostMessage((mr) => { Report(mr); }, "alert", string.Format("error: {0}", messageString));
                        }
                        break;
                    }
                case "Remote Execution":
                    {
                        RIO.Message message = e as RIO.Message;
                        if (!message.Parameters.ContainsKey("command")) break;
                        Parameter parameter = new Parameter { Type = "parameters" };
                        Dictionary<string, dynamic> parameters = RIO.Command.Parse(parameter, message.Parameters["command"]);
                        Dictionary<string, dynamic> results = RIO.Command.Parse(parameter, message.Parameters["Execution Result"]);
                        RIO.Extensions.AddRange(parameters, results);
                        parameters["target"] = "TadDisplay";
                        RIO.Message m = new RIO.Message
                        {
                            Type = "exec",
                            Parameters = parameters,
                            Source = message.Source,
                            Id = message.Id
                        };
                        Manager.ManageRequest(m);
                        break;
                    }
                case "Manager":
                    {
                        if (management == null)
                        {
                            Log.Information("Management channel disabled");
                            break;
                        }
                        if (e is RIO.Message message)
                        {
                            var messageString = JsonConvert.SerializeObject(message, jsonSerializerSettings);
                            //var messageString = stj.JsonSerializer.SerializeToUtf8Bytes(response, jsonSerializerOptions);
                            await management.Send(messageString);
                        }
                        break;
                    }
                case "debug":
                    Log.Information("debug: {0}", e);
                    if (Settings.EnableRemoteDebug)
                    {
                        if (management == null)
                        {
                            Log.Information("Management channel disabled");
                            break;
                        }
                        Dictionary<string, object> info = new Dictionary<string, object>() { { "info", e } };

                        RIO.Message response = new RIO.Message() { Type = "debug", Source = Settings.Id, Parameters = info };
                        var messageString = JsonConvert.SerializeObject(response, jsonSerializerSettings);
                        await management.Send(messageString);
                    }
                    break;
            }
        }

        private static void WriteToLogger(object? sender, object notification)
        {
            try
            {
                var messageString = stj.JsonSerializer.Serialize(notification, jsonSerializerOptions)
                    .Replace("\":\"", "\t")
                    .Replace("\",\"", "\n")
                    .Replace(",\"", "\n")
                    .Replace("{\"", "\n")
                    .Replace("\":", "\t")
                    .Replace("}", "\n")
                    .Replace("\"", "");
                switch (sender.ToString()[..Math.Min(5, sender.ToString().Length)])
                {
                    case "error":
                        Log.Error("{0}: {1}", sender, messageString);
                        break;
                    case "debug":
                        Log.Debug(messageString);
                        break;
                    default:
                        Log.Information("{0}: {1}", sender, messageString);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("{0}: {1}", sender, ex.Message);
                Console.Error.WriteLine("{0}: {1}", sender, ex.Message);
            }
        }

        #region Settings
        internal static void SaveSettings(string filename = "Settings.json")
        {
            JsonSerializerSettings jsonSettings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            Newtonsoft.Json.JsonSerializer serializer = Newtonsoft.Json.JsonSerializer.Create(jsonSettings);
            using var stream = System.IO.File.Open(
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename), FileMode.Create);
            using TextWriter writer = new StreamWriter(stream);
            using JsonWriter w = new JsonTextWriter(writer);
            serializer.Serialize(w, Settings as Settings);
        }

        internal static void LoadSettings(string filename = "Settings.json")
        {
            try
            {
                using (var stream = System.IO.File.OpenRead(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename)))
                {
                    Newtonsoft.Json.JsonSerializer deserializer = Newtonsoft.Json.JsonSerializer.Create();
                    using TextReader reader = new StreamReader(stream);
                    using JsonReader r = new JsonTextReader(reader);
                    Settings = deserializer.Deserialize(r, typeof(Settings)) as Settings;
                    //Settings = stj.JsonSerializer.Deserialize<Settings>(reader.ReadToEnd());
                }
                List<string> set = new List<string>();
                bool saveRequired = false;
                foreach (Feature feature in Settings.Features.ToArray())
                {
                    if (set.Contains(feature.Id))
                    {
                        Settings.Features.Remove(feature);
                        saveRequired = true;
                    }
                    else
                    {
                        set.Add(feature.Id);
                    }
                }
                if (saveRequired)
                {
                    SaveSettings(settingsFilename);
                }
            }
            catch (FileNotFoundException) { throw; }
            catch { }
        }

        private static void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveSettings(settingsFilename);
        }
        #endregion // Settings

        #region LocalManagement
        private static void Server_Error(object sender, Exception e)
        {
            Log.Error(e, "[{Timestamp:HH:mm:ss} {Level:u3}] {Exception}");
        }

        private static readonly Dictionary<TcpClient, EventWaitHandle> locks = new Dictionary<TcpClient, EventWaitHandle>();
        private static readonly Dictionary<TcpClient, bool> interactiveClient = new Dictionary<TcpClient, bool>();
        private static async void Server_Connected(object sender, TcpClient e)
        {
            bool interactive = interactiveClient[e] = !GetSync(e).WaitOne(500);
            if (interactive)
            {
                StreamWriter sw = new StreamWriter(e.GetStream());

                await sw.WriteLineAsync("\x1B[1m");
                await sw.WriteLineAsync($"{Settings.Id} Core {Manager.Version}\n");
                await sw.WriteLineAsync("\x1B[0m");
                foreach (string line in Manager.Instance.Where(kv => kv.Value is ITask).Select(kv => string.Format("{0}:\t{1}, v{2}",
                     kv.Key, (kv.Value as ITask).Feature.Type, (kv.Value as ITask).Feature.Version)))
                    await sw.WriteLineAsync(line);
                await sw.WriteAsync($"{Settings.Id}> ");
                await sw.FlushAsync();
            }
        }

        private static EventWaitHandle GetSync(TcpClient e)
        {
            lock (locks)
                if (locks.TryGetValue(e, out EventWaitHandle wh))
                    return wh;
                else
                {
                    interactiveClient[e] = false;
                    wh = new AutoResetEvent(false);
                    return locks[e] = wh;
                }
        }

        private static async void Server_Received(object sender, string text)
        {
            TcpClient client = sender as TcpClient;
            GetSync(client).Set();
            bool interactive = interactiveClient[client];

            StreamWriter sw = (client != null) ? new StreamWriter(client.GetStream()) : null;
            if (!string.IsNullOrEmpty(text))
            {
                if ("bye".Equals(text.ToLower()) || text[0] == '\x04')
                {
                    sw?.Write("bye\n\r");
                    sw?.Flush();
                    client?.Close();
                    return;
                }
                RIO.Message request = Manager.ParseCommandLine(text, sender);
                if (request.IsValid)
                {
                    request.Source = "Local";

                    RIO.Message response = request;
                    switch (request.Type)
                    {
                        case "history":
                            break;
                        case "test":
                            {
                                string[] toPerform = new string[0];
                                if (request.Parameters.ContainsKey("tests"))
                                    toPerform = request.Parameters["tests"];
                                if (toPerform.Length == 0)
                                {
                                    Dictionary<string, dynamic> parameters = new Dictionary<string, dynamic>();
                                    parameters.AddRange(tests.Value.Keys.Select(s => new KeyValuePair<string, dynamic>(s, tests.Value[s].Length)));
                                    response = new RIO.Message() { Parameters = parameters };
                                }
                                else
                                {
                                    if (toPerform.Contains("all"))
                                        toPerform = tests.Value.Keys.ToArray();
                                    foreach (string name in toPerform)
                                    {
                                        sw?.WriteLine(
                                            string.Format("Test: _{0}_, _{1}_ Message(s)",
                                                name, tests.Value[name].Length).ToMarkDown().MdToTerminal());
                                        if (tests.Value.ContainsKey(name))
                                            foreach (RIO.Message message in tests.Value[name])
                                            {
                                                response = Manager.ManageRequest(message.Clone());
                                                if (response != null)
                                                    sw?.WriteLine(response.Parameters.ToMarkDown().MdToTerminal());
                                            }
                                    }
                                    response = null;
                                }
                            }
                            break;
                        case "retry":
                            if (telemetry is RetryChannel retry)
                            {
                                string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                                foreach (string filename in Directory.EnumerateFiles(folder, "retryBuffer*.txt"))
                                {
                                    Manager.OnNotify("info", "Retrying {0}", filename);
                                    string[] lines = File.ReadAllLines(filename);
                                    var failed = await retry.Recover(lines);
                                    if (failed.Count == 0)
                                    {
                                        File.Delete(filename);
                                        Manager.OnNotify("info", "Retrying {0} succeded", filename);
                                    }
                                    else
                                    {
                                        Manager.OnNotify("info", "Retrying {0} failed", filename);
                                        File.WriteAllLines(filename, failed.Select(o => o.ToString()));
                                    }
                                }
                            }
                            break;
                        default:
                            response = Manager.ManageRequest(request);
                            break;
                    }

                    if (response != null)
                    {
                        if (interactive)
                            sw?.WriteLine(response.Parameters.ToMarkDown().MdToTerminal());
                        else
                        {
                            var messageString = JsonConvert.SerializeObject(response.Parameters, jsonSerializerSettings);
                            sw?.WriteLine(messageString);
                        }
                    }
                }
            }
            if (interactive)
                sw?.Write("{0}> ", Settings.Id);
            sw?.Flush();
        }
        private static void Broadcast(object sender, object notification)
        {
            try
            {
                bool enabled = false;
                if (notification is RIO.Message message)
                {
                    enabled = true;
                    if (message.Type?.Equals("Execution Result") == true)
                    {
                        if (message.Parameters.TryGetValue("Execution Result", out object output))
                        {
                            notification = output.ToText();
                        }
                        else
                        if (message.Parameters.TryGetValue("Error", out object error))
                        {
                            notification = error.ToText();
                        }
                    }
                }
                if ("telemetry".Equals(sender))
                {
                    enabled = true;
                    dynamic data = notification;
                    notification = string.Format("Telemetry: {0}", data.FeatureId);
                }
                //var messageString = JsonConvert.SerializeObject(notification, jsonSerializerSettings);
                if (enabled)
                {
                    var messageString = stj.JsonSerializer.Serialize(notification, jsonSerializerOptions);
                    server?.Broadcast(messageString);
                }
            }
            catch { }
        }
        #endregion  // LocalManagement

        #region Alerts
        private static void AddRecent(alert cap)
        {
            lock (lkRecents)
            {
                recentCaps.Add(new AlertNotification() { Id = cap.identifier, Time = DateTime.UtcNow });
                recentCaps.RemoveAll(n => (DateTime.UtcNow - n.Time) > TimeSpan.FromMinutes(2));
            }
        }

        private static bool CheckRecent(alert cap)
        {
            lock (lkRecents)
            {
                recentCaps.RemoveAll(n => (DateTime.UtcNow - n.Time) > TimeSpan.FromMinutes(2));
                return recentCaps.Any(n => n.Id.Equals(cap?.identifier));
            }
        }
        private static bool CheckSetRecent(alert cap)
        {
            lock (lkRecents)
            {
                recentCaps.RemoveAll(n => (DateTime.UtcNow - n.Time) > TimeSpan.FromMinutes(2));
                if (recentCaps.Any(n => n.Id.Equals(cap?.identifier)))
                    return true;

                recentCaps.Add(new AlertNotification() { Id = cap.identifier, Time = DateTime.UtcNow });
                return false;
            }
        }


        private static async void AlertReceived(object sender, object o)
        {
            string text = o.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                alert cap = JRC.CAP.alert.ParseJson(text);
                if (cap != null && !cap.sender.Equals(Manager.Id) && !CheckSetRecent(cap))
                    await Manager.ManageAlert(cap);
            }
        }
        #endregion  // Alerts
        private static async void ManagementRequest(object sender, object o)
        {
            string text = o as string;
            if (!string.IsNullOrEmpty(text))
            {
                RIO.Message response = Manager.ManageRequest(text);
                if (response?.IsValid != true)
                {
                    return;
                }

                response.Source = Settings.Id;
                //string responseText = JsonConvert.SerializeObject(response, jsonSerializerSettings);
                string responseText = stj.JsonSerializer.Serialize(response, jsonSerializerOptions);

                if (!string.IsNullOrEmpty(responseText))
                {
                    await management.Send(responseText);
                }
            }
        }

        #region RetryBuffer
        private static async void StoreRetryBuffer(object sender, RetryChannel.RetryEventArgs e)
        {
            string filename = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "retryBuffer.txt");

            await File.WriteAllLinesAsync(filename, e.BackLog.Select(o => o.ToString()));
        }

        private static async Task CheckRetryBuffer()
        {
            string filename = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "retryBuffer.txt");
            if (File.Exists(filename))
            {
                string newName = filename.Replace(".txt", DateTime.UtcNow.ToString("-yyyy-MM-dd-HH:mm:ss") + ".txt");
                File.Move(filename, newName);
                string[] lines = File.ReadAllLines(newName);
                if (lines.Length > 0
                && telemetry is RetryChannel retry)
                {
                    var failed = await retry.Recover(lines);
                    if (failed.Count == 0)
                        File.Delete(newName);
                    else
                        File.WriteAllLines(newName, failed.Select(o => o.ToString()));
                }
            }
        }
        #endregion  // RetryBuffer

        #region Slack
        static void InitializeSlack()
        {
            string botToken = Settings.SlackToken;
            ManualResetEventSlim clientReady = new ManualResetEventSlim(false);
            if (string.IsNullOrEmpty(Settings.WebProxy))
            {
                client = new SlackAPI.SlackSocketClient(botToken);
            }
            else
            {
                client = new SlackAPI.SlackSocketClient(botToken, new WebProxy(Settings.WebProxy));
            }

            client.Connect((connected) =>
            {
                // This is called once the client has emitted the RTM start command
                clientReady.Set();
            }, () =>
            {
                // This is called once the RTM client has connected to the end point
            });

            client.OnMessageReceived += Slack_OnMessageReceived;

            if (clientReady.Wait(30000))
            {
                Log.Information("Slack agent started");
                client.PostMessage((mr) => { Report(mr); }, "rio", string.Format("{0}: started", Settings.Id), botName: Settings.Id);
            }
            else
            {
                Log.Information("Slack agent unavailable");
            }
        }

        private static void Slack_OnMessageReceived(SlackAPI.WebSocketMessages.NewMessage message)
        {
            string id = string.Format("{0} ", Settings.Id);
            // Handle each message as you receive them
            Log.Information(message.user + "(" + message.username + "): " + message.text);

            if (message.text.StartsWith(id))
            {
                RIO.Message request = Manager.ParseCommandLine(message.text[id.Length..]);
                if (!request.IsValid)
                {
                    return;
                }

                request.Source = "Slack";

                RIO.Message response = Manager.ManageRequest(request);

                client.PostMessage((mr) => { Report(mr); }, message.channel, response.ToMarkDown(),
                        botName: Settings.Id, icon_emoji: ":robot_face:");
            }
        }

        private static void Report(SlackAPI.PostMessageResponse mr)
        {
            if (mr?.ok != true || !string.IsNullOrWhiteSpace(mr.error))
                Log.Error(mr?.error);
        }
        #endregion  // Slack
    }
}