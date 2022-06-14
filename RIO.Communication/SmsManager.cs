using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace RIO.Communication
{
    /// <summary>
    /// This module relies upon an external service to send SMS, just specifying a comma separated list of mobile numbers and the text of the message,
    /// The request is posted as a JSON serialized object onto the service.
    /// </summary>
    [Export(typeof(IFeature))]
    public class SmsManager : IFeature
    {
        /// <inheritdoc/>
        public string Name => typeof(SmsManager).Name;

        /// <inheritdoc/>
        public IEnumerable Configuration
        {
            get
            {
                return new Property[]
                {
                    new Property
                    {
                        Name = "Service",
                        Type = "string",
                    },
                    new Property
                    {
                        Name = "Username",
                        Default = "",
                        Type = "string",
                    },
                    new Property
                    {
                        Name = "Password",
                        Default = "",
                        Type = "string",
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
                    new SmsManagerTask.SendAction()
                };
            }
        }

        /// <inheritdoc/>
        public IEnumerable<ITask> Setup(Settings configuration, Feature settings)
        {
            SmsManagerTask task = new SmsManagerTask();
            task.Setup(configuration, settings);

            yield return task;
        }

        /// <inheritdoc/>
        public string Version => "1.0.0";
    }

    internal class SmsManagerMetrics : IMetrics
    {
        public SmsManagerMetrics()
        {
        }
    }

    internal class SmsManagerTask : ITask
    {
        readonly SmsManagerMetrics metrics = new SmsManagerMetrics();
        public string Name => typeof(SmsManager).Name;

        public string Version => Feature.Version;
        public dynamic Status { get; private set; }

        public Feature Feature { get; private set; }

        public IMetrics Metrics => metrics;

        public string Service = null;
        public string Username = string.Empty;
        public string Password = string.Empty;

        internal class SendAction : Command
        {
            public SendAction()
            {
                Name = "send";
                Target = "SmsManager";
            }
            public override IEnumerable<Parameter> Parameters => new Parameter[] {
                new Parameter { Type = "string", Name = "recipient", Required = false },
                new Parameter { Type = "string", Name = "message", Required = false },
                new Parameter { Type = "*", Name = "macro", Required = false } };

            protected override object Run(ITask instance, Manager manager, Message response, dynamic parameters = null)
            {
                SmsManagerTask task = instance as SmsManagerTask;
                if (!string.IsNullOrEmpty(task?.Service))
                {
                    string recipient = parameters.recipient, message = parameters.message;
                    if (File.Exists(message))
                    {   // Use a template file to prepare the body of the email
                        message = File.ReadAllText(message);
                    }
                    message = Extensions.Substitute(message, Manager.Variables);
                    message = Extensions.Substitute(message, parameters);


                    HttpClient client = new HttpClient();

                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");

                    var request = new
                    {
                        username = task.Username,
                        password = task.Password,
                        toNumber = recipient.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                        body = message,
                        dateDelivery = DateTime.UtcNow
                    };
                    var content = new StringContent(JsonConvert.SerializeObject(request), System.Text.Encoding.UTF8, "application/json");
                    HttpResponseMessage webResponse = client.PostAsync(task.Service, content).Result;
                    if (webResponse.IsSuccessStatusCode)
                    {
                        string text = new System.IO.StreamReader(webResponse.Content.ReadAsStreamAsync().Result).ReadToEnd();
                        dynamic report = JsonConvert.DeserializeObject(text);
                        response.Add("Execution Result", "Mail delivery submitted", report.message.ToString());
                    }
                    else
                        response.Add("Execution Result", $"Mail sending failed for error {webResponse.StatusCode}");

                    return null;
                }

                response.Add("Execution Result", "No action taken");
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
            Feature.GetString("Service", out Service);
            Username = Feature.GetString("Username");
            Password = Feature.GetString("Password");
        }
    }
}
