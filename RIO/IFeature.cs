using JRC.CAP;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RIO
{
    /// <summary>
    /// Describes a module implementing a functionality, usually the driver for a hardware component.
    /// </summary>
    public interface IFeature
    {
        /// <summary>
        /// The name of the type of module.
        /// It is possible but not common to have more than one feature of the same type defined at the same moment.
        /// From this information, it is possible for the <see cref="Manager"/> to instantiate the right handler for the configured module.
        /// </summary>
        /// <value>
        /// The name of the type of feature.
        /// </value>
        string Name { get; }

        /// <summary>
        /// According to the specified configuration, it sets up one or more <see cref="ITask"/> to perform the requested activities.
        /// </summary>
        /// <param name="configuration">The overall configuration.</param>
        /// <param name="settings">The specific settings for the feature.</param>
        /// <returns></returns>
        IEnumerable<ITask> Setup(Settings configuration, Feature settings);
        /// <summary>
        /// Gets the configuration properties supported by the feature.
        /// </summary>
        /// <value>
        /// The configuration, that is a list of <see cref="Property"/>.
        /// </value>
        IEnumerable Configuration { get; }

        /// <summary>
        /// Gets the list of commands supported by the feature.
        /// </summary>
        /// <value>
        /// A list of <see cref="Command"/>, usually empty, supported by the feature.
        /// </value>
        IEnumerable<Command> Commands { get; }
        /// <summary>
        /// Gets the version of the feature, a value assigned statically in the Interface implementation.
        /// </summary>
        string Version { get; }
    }

    /// <summary>
    /// A property describes a setting the <see cref="IFeature"/> requires to set up. It supports a default value to be used, when not provided by the user.
    /// </summary>
    [DebuggerDisplay("{Type} {Name} {Default}")]
    public struct Property
    {
        /// <summary>
        /// Gets or sets the name of the property.
        /// </summary>
        /// <value>
        /// The name.
        /// </value>
        public string Name { get; set; }
        /// <summary>
        /// A string representing the default value for the property.
        /// </summary>
        /// <value>
        /// The default.
        /// </value>
        public string Default { get; set; }
        /// <summary>
        /// A string describing the data type of the property, e.g. int, float, string, uri, bool, ...
        /// </summary>
        /// <value>
        /// The data type name.
        /// </value>
        public string Type { get; set; }
    }

    /// <summary>
    /// A parameter describes the value required by a <see cref="Command"/> to perform.
    /// It will get a property of the input parameter of the <see cref="Command.Run(ITask, Manager, Message, dynamic)"/> method.
    /// </summary>
    [DebuggerDisplay("{Type} {Name} Required? {Required}")]
    public struct Parameter
    {
        /// <summary>
        /// The parameter name is case sensitive.
        /// </summary>
        /// <value>
        /// The name.
        /// </value>
        public string Name { get; set; }
        /// <summary>
        /// A string describing the data type of the parameter, e.g. int, float, string, uri, bool, ...
        /// </summary>
        /// <value>
        /// The data type name.
        /// </value>
        public string Type { get; set; }
        /// <summary>
        /// Gets or sets a flag indicating whether this <see cref="Parameter"/> is required.
        /// Without a required parameter, the <see cref="Command"/> will not be executed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if required; otherwise, <c>false</c>.
        /// </value>
        public bool Required { get; set; }
        /// <summary>
        /// When assigned a value, it describes the domain of possible values for the parameter,
        /// e.g. configuration.setting will mean that the <see cref="IFeature.Configuration"/> will
        /// include a <see cref="Property"/> named <em>setting</em>, like a string array, defining all
        /// the allowed values for the parameter. if <em>setting</em> is a <see cref="IDictionary"/>,
        /// its <see cref="IDictionary.Keys"/> will be used.
        /// </summary>
        public string Domain { get; set; }
        /// <summary>
        /// Returns a text presentation of the <see cref="Parameter"/>, mainly for debugging purposes.
        /// </summary>
        /// <returns>A string with all the properties.</returns>
        public override string ToString()
        {
            return string.Format("{0} {1}{2} {3}", Type, Name, Required ? " Required" : string.Empty, Domain);
        }
    }

    /// <summary>
    /// A single instance of an activity set up by a feature.
    /// </summary>
    /// <seealso cref="RIO.IMeasurable" />
    public interface ITask : IMeasurable
    {
        /// <summary>
        /// Gets the name as specified in the <see cref="Settings"/>.
        /// </summary>
        /// <value>
        /// The name of the task.
        /// </value>
        string Name { get; }

        /// <summary>
        /// Whatever the task needs to share to communicate its status.
        /// </summary>
        /// <value>
        /// A dynamic object containing the status.
        /// </value>
        dynamic Status { get; }

        /// <summary>
        /// The information used to set up the task, usually found in the <see cref="Settings"/>.
        /// </summary>
        /// <value>
        /// The task configuration.
        /// </value>
        [XmlIgnore()]
        [JsonIgnore]
        Feature Feature { get; }

        /// <summary>
        /// The task is considered to start as asynchronous.
        /// No activity should start if not for this method.
        /// </summary>
        Task Start();
        /// <summary>
        /// This method allows to stop activities and to free the resources claimed by the task.
        /// It is asynchronous.
        /// </summary>
        Task Stop();

        /// <summary>
        /// The version of a task should always be the related <see cref="IFeature"/> version.
        /// </summary>
        string Version { get; }
        //public string Version => Feature.Version;
    }
}