using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Dynamic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// This example of RIO module provides random data at a given frequency in order to simulate a real component.
    /// Use it for testing and as a guideline to build your own.
    /// </summary>
    [Export(typeof(IFeature))]
    public class Simulation : IFeature
    {
        /// <summary>
        /// The type of feature.
        /// </summary>
        public string Name => typeof(Simulation).Name;
        /// <summary>
        /// List of the configuration parameters to set up an instance of the Simulation feature.
        /// </summary>
        public IEnumerable Configuration
        {
            get
            {
                return new Property[]
                {
                    new Property
                    {
                        Name = "Name",
                        Default = "Measure",
                        Type = "string"
                    },
                    new Property
                    {
                        Name = "Frequency",
                        Default = "2",
                        Type = "int"
                    },
                    new Property
                    {
                        Name = "Average",
                        Default = "0",
                        Type = "float"
                    },
                    new Property
                    {
                        Name = "Variance",
                        Default = "0",
                        Type = "float"
                    }
                };
            }
        }
        /// <summary>
        /// No commands are defined for this type of module.
        /// </summary>
        public IEnumerable<Command> Commands { get { yield break; } }
        /// <summary>
        /// Called by the <see cref="Manager"/>, this factory function returns an <see cref="ITask"/> to simulate a
        /// telemetry generating component.
        /// </summary>
        /// <param name="configuration">The <see cref="Manager"/> configuration.</param>
        /// <param name="settings">The specific task configuration.</param>
        /// <returns></returns>
        public IEnumerable<ITask> Setup(Settings configuration, Feature settings)
        {
            SimulationTask task = new SimulationTask();
            task.Setup(configuration, settings);

            yield return task;
        }
        /// <summary>
        /// The version of this module.
        /// </summary>
        public string Version => "1.0.0";
    }
    /// <summary>
    /// Metrics to be provided, when queried about the status of the module.
    /// </summary>
    public class SimulationMetrics : IMetrics
    {
        /// <summary>
        /// Origin time of the activity of the module.
        /// </summary>
        public DateTime start = DateTime.UtcNow;
        /// <summary>
        /// Number of samples generated.
        /// </summary>
        public long count = 0;
        /// <summary>
        /// Average of the generated samples.
        /// </summary>
        public double average = 0;
        /// <summary>
        /// Add a data: the average must converge to the same value of the module configuration, if the random number
        /// generator works properly.
        /// </summary>
        /// <param name="sample">The new value to be added.</param>
        public void Add(double sample) { average += sample / ++count; }
    }

    internal class SimulationTask : ITask
    {
        private Random random = new Random();
        public int Frequency;
        public float Average, Variance;
        public string Measure;
        private string deviceId = string.Empty, myId = string.Empty, status = "unset";
        private Timer timer = null;
        private readonly SimulationMetrics metrics = new SimulationMetrics();
        public string Name => string.Format("{0}: {1} ({2}{5}{3}) every {4}s, {6}", myId, Measure, Average, Variance, Frequency, '\xb1', status);
        public Feature Feature { get; private set; }

        public string Version => Feature.Version;
        public dynamic Status => string.Format("{0}: {1} ({2}{5}{3}) every {4}s, {6}", myId, Measure, Average, Variance, Frequency, '\xb1', status);

        public IMetrics Metrics => metrics;
        public void Setup(Settings configuration, Feature settings)
        {
            Feature = settings;
            deviceId = configuration.Id;
            myId = settings.Id;
            settings.GetString("Measure", out Measure, "Measure");
            settings.GetInt("Frequency", out Frequency, 2);
            settings.GetFloat("Average", out Average, 0);
            settings.GetFloat("Variance", out Variance, 0);
            timer = new Timer((obj) => generate(), this, Timeout.Infinite, Frequency * 1000);
            status = "configured";
        }

        private void generate()
        {
            double sample = Average - Variance + 2 * Variance * random.NextDouble();
            metrics.Add(sample);

            dynamic telemetryDataPoint = new ExpandoObject();
            var expandoDic = (IDictionary<string, object>)telemetryDataPoint;

            expandoDic.Add("Timestamp", DateTime.UtcNow);
            expandoDic.Add("DeviceId", deviceId);
            expandoDic.Add("FeatureId", myId);
            expandoDic.Add(Measure, sample.ToString("0.#####"));

            Manager.OnNotify("telemetry", telemetryDataPoint);
        }

        public Task Start()
        {
            timer.Change(0, Frequency * 1000);
            status = "started";
            return null;
        }

        public Task Stop()
        {
            timer.Change(Timeout.Infinite, Frequency * 1000);
            status = "stopped";
            return null;
        }
    }
}
