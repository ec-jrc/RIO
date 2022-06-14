using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RIO.Communication
{
    /// <summary>
    /// This module can send messages onto a Slack channel, given its token and a message.
    /// Optionally, an icon can be added; otherwise, the robot face is used.
    /// </summary>
    [Export(typeof(IFeature))]
    public class SlackManager : IFeature
    {
        /// <inheritdoc/>
        public string Name => typeof(SlackManager).Name;

        /// <inheritdoc/>
        public IEnumerable Configuration
        {
            get
            {
                return new Property[] { };
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Command> Commands
        {
            get
            {
                return new RIO.Command[]
                {
                    new SlackManagerTask.SendAction()
                };
            }
        }

        /// <inheritdoc/>
        public IEnumerable<ITask> Setup(Settings configuration, Feature settings)
        {
            SlackManagerTask task = new SlackManagerTask();
            task.Setup(configuration, settings);

            yield return task;
        }

        /// <inheritdoc/>
        public string Version => "1.0.0";
    }

    internal class SlackManagerMetrics : IMetrics
    {
        public SlackManagerMetrics()
        {
        }
    }

    internal class SlackManagerTask : ITask
    {
        string botToken = string.Empty, webProxy = string.Empty;

        SlackManagerMetrics metrics = new SlackManagerMetrics();
        public string Name => typeof(SlackManager).Name;

        public string Version => Feature.Version;
        public dynamic Status { get; private set; }

        public Feature Feature { get; private set; }

        public IMetrics Metrics => metrics;

        internal class SendAction : Command
        {
            public SendAction()
            {
                Name = "send";
                Target = "SlackManager";
            }
            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "channel", Required = true },
                new Parameter { Type = "string", Name = "message", Required = true },
                new Parameter { Type = "string", Name = "symbol", Required = false }
            };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                string botToken = string.Empty;
                SlackManagerTask task = instance as SlackManagerTask;
                if (!string.IsNullOrEmpty(botToken = task?.botToken))
                {
                    string channel = parameters.channel, message = parameters.message, symbol = parameters.symbol;
                    if (File.Exists(message))
                    {   // Use a template file to prepare the body of the email
                        message = File.ReadAllText(message);
                    }
                    message = Extensions.Substitute(message, Manager.Variables);
                    message = Extensions.Substitute(message, parameters);

                    SlackAPI.SlackSocketClient client = (string.IsNullOrEmpty(task?.webProxy)) ?
                         new SlackAPI.SlackSocketClient(botToken) :
                         new SlackAPI.SlackSocketClient(botToken, new WebProxy(task?.webProxy));
                    ManualResetEventSlim clientReady = new ManualResetEventSlim(false);

                    client.Connect((connected) =>
                    {
                        // This is called once the client has emitted the RTM start command
                        clientReady.Set();
                    }, () =>
                    {
                        // This is called once the RTM client has connected to the end point
                    });

                    if (clientReady.Wait(30000))
                    {
                        client.PostMessage((mr) =>
                        {
                            if (mr.ok)
                                response.Add("Execution Result", "Message sent");
                            else
                                response.Add("Error", "Slack error: {0}", mr.error);
                        }, channel, message, botName: Manager.Id, icon_emoji: symbol ?? ":robot_face:");
                    }
                    else
                    {
                        response.Add("Error", "Slack agent unavailable");
                    }
                }
                else
                    response.Add("Error", "Invalid Slack configuration");

                return null;
            }
        }

        public Task Start()
        {
            return null;
        }

        public Task Stop()
        {
            return null;
        }

        internal void Setup(Settings configuration, Feature settings)
        {
            if (configuration.EnableSlack)
                botToken = configuration.SlackToken;
            Feature = settings;
        }
    }
}
