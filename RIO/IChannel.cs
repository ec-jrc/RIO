using System;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// Empty interface to qualify an object as a set of information about the status of a RIO component
    /// </summary>
    public interface IMetrics { }

    /// <summary>
    /// Implement this <c>interface</c>, whenever you need a RIO managed object, that can report about its status.
    /// Examples are <see cref="ITask"/>, <see cref="IChannel"/> and <see cref="Service"/>.
    /// </summary>
    public interface IMeasurable
    {
        /// <summary>
        /// An object containing meaningful information about the status of this RIO component.
        /// </summary>
        /// <value>
        /// The metrics.
        /// </value>
        IMetrics Metrics { get; }
    }

    /// <summary>
    /// Possible results of a transmission attempt.
    /// </summary>
    public enum TransmissionResult
    {
        /// <summary>
        /// Success
        /// </summary>
        OK,
        /// <summary>
        /// Not connection related failure
        /// </summary>
        Failed,
        /// <summary>
        /// The connection is not available or possible
        /// </summary>
        NoConnection
    }

    /// <summary>
    /// A channel is a connection to a single remote endpoint, implementing an asynchronous communication.
    /// The underlying mean can be of different nature, raw or protocol based.
    /// Logical channels can be built on top of others as well.
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    /// <seealso cref="RIO.IMeasurable" />
    public interface IChannel : IDisposable, IMeasurable
    {
        /// <summary>
        /// The object will be transformed, if needed, and sent asynchronously by the IChannel, reporting if sent succesfully: the delivery is not guaranteed, though.
        /// </summary>
        /// <param name="o">Usually strings or byte array are sent. Other objects should be serialized accorsingly.</param>
        /// <returns></returns>
        Task<TransmissionResult> Send(object o);

        /// <summary>
        /// This is the only way to receive data: asynchronously.
        /// Subscribe to the event and wait for data.
        /// </summary>
        event EventHandler<object> Received;
    }
}
