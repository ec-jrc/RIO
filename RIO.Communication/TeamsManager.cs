using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RIO.Communication
{
    /// <summary>
    /// Use this module to post message onto a specific channel to be configured for the instance. You can have more than one instance of this module.
    /// The message must contain a valid JSON content, e.g. a card.
    /// </summary>
    /// <inheritdoc/>
    [Export(typeof(IFeature))]
    public class TeamsManager : IFeature
    {
        /// <inheritdoc/>
        public string Name => typeof(TeamsManager).Name;

        /// <inheritdoc/>
        public IEnumerable Configuration
        {
            get
            {
                return new Property[]
                {
                    new Property
                    {
                        Name = "Channel",
                        Type = "string"
                    }
                };
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Command> Commands
        {
            get
            {
                return new RIO.Command[]
                {
                    new TeamsManagerTask.PostToChannelAction()
                };
            }
        }

        /// <inheritdoc/>
        public IEnumerable<ITask> Setup(Settings configuration, Feature settings)
        {
            TeamsManagerTask task = new TeamsManagerTask();
            task.Setup(configuration, settings);

            yield return task;
        }

        /// <inheritdoc/>
        public string Version => "1.0.0";
    }

    internal class TeamsManagerMetrics : IMetrics
    {
        public TeamsManagerMetrics()
        {
        }
    }

    internal class TeamsManagerTask : ITask
    {
        readonly TeamsManagerMetrics metrics = new TeamsManagerMetrics();
        string ChannelPost = null;

        public string Name => typeof(TeamsManager).Name;

        public string Version => Feature.Version;
        public dynamic Status { get; private set; }

        public Feature Feature { get; private set; }

        public IMetrics Metrics => metrics;

        internal class PostToChannelAction : Command
        {
            public PostToChannelAction()
            {
                Name = "post";
                Target = "TeamsManager";
            }
            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "message", Required = true } };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                TeamsManagerTask task = instance as TeamsManagerTask;
                try
                {
                    if (!string.IsNullOrEmpty(task?.ChannelPost))
                    {
                        Uri channelPostUri = new Uri(task.ChannelPost);
                        string message = parameters.message;
                        HttpClient hc = new HttpClient();
                        HttpContent httpContent = new StringContent(message, Encoding.UTF8, "application/json");
                        var result = hc.PostAsync(channelPostUri, httpContent).Result;
                        string executionResult = result.IsSuccessStatusCode ? "Successfully posted" : result.ReasonPhrase;

                        response.Add("Execution Result", executionResult);
                    }
                    response.Add("Execution Result", "No action taken");
                }
                catch (Exception ex)
                {
                    response.Add("Execution Result", ex.Message);
                    response.Add("Execution Result", ex.StackTrace);
                }
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
            Feature = settings;
            ChannelPost = Feature.GetString("Channel");
        }
    }
}
