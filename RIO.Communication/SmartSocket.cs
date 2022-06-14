using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// This class handles a TCP/IP <see cref="Socket"/>, that automatically reconnects it in case of error.
    /// </summary>
    public class SmartSocket
    {
        private readonly string host;
        private readonly int port;
        private readonly TimeSpan timeout;
        private TcpClient client;
        private AlertStream stream;
        private readonly bool retryEnabled = false;

        /// <summary>
        /// Retrieves the data connection stream of the socket through a <see cref="AlertStream"/>. In case of error, 
        /// it reconnects the socket.
        /// </summary>
        public Stream Stream
        {
            get
            {
                if (stream != null)
                    return stream;
                if (client.Connected && client.GetStream() != null)
                {
                    stream = new AlertStream(client.GetStream());
                    stream.Error += Stream_Error;
                    return stream;
                }
                return null;
            }
        }
        /// <summary>
        /// The hostname or the IP address to connect to.
        /// </summary>
        public string Host => host;
        /// <summary>
        /// Port number to connect to.
        /// </summary>
        public int Port => port;

        private void Stream_Error(object sender, string e)
        {
            stream.Error -= Stream_Error;
            stream = null;
            Connect();
        }
        /// <summary>
        /// Creates an instance to connect to the given host and port without automatic reconnection.
        /// </summary>
        /// <param name="host">Hostname or IP address.</param>
        /// <param name="port">Poer number.</param>
        public SmartSocket(string host, int port) : this(host, port, TimeSpan.MaxValue) { }
        /// <summary>
        /// Creates an instance to connect to the given host and port and automatic reconnection after the given timeout.
        /// </summary>
        /// <param name="host">Hostname or IP address.</param>
        /// <param name="port">Poer number.</param>
        /// <param name="reconnect">Do not try to connect again before this time elapses.</param>
        public SmartSocket(string host, int port, TimeSpan reconnect)
        {
            this.host = host;
            this.port = port;
            this.timeout = reconnect;
            retryEnabled = timeout < TimeSpan.MaxValue;
        }
        /// <summary>
        /// Begin the connection of the instance. In case a connection is already established, close it and reconnect.
        /// </summary>
        public void Connect()
        {
            if (client?.Connected == true)
                try
                {
                    client.Close();
                    client.Dispose();
                    stream = null;
                }
                catch { }
            client = new TcpClient();
            client.BeginConnect(host, port, TcpConnectCompleted, this);
        }
        /// <summary>
        /// Close the connection. Raises the <see cref="Disconnected"/> event.
        /// </summary>
        public void Close()
        {
            client.Close();
            Disconnected?.Invoke(this, new EventArgs());
            client = null;
        }

        private void TcpConnectCompleted(IAsyncResult ar)
        {
            try { client.EndConnect(ar); }
            catch { }

            if (client.Connected)
            {
                Connected?.Invoke(this, new EventArgs());
            }
            else
            {
                ConnectionFailed?.Invoke(this, new EventArgs());
                if (retryEnabled)
                {
                    Thread.Sleep(timeout);
                    try
                    { client.BeginConnect(host, port, TcpConnectCompleted, this); }
                    catch (Exception) { }
                }
            }
        }
        /// <summary>
        /// The event notifies that the connection process completed successfully.
        /// </summary>
        public event EventHandler Connected;
        /// <summary>
        /// The event notifies that the connection was interrupted.
        /// </summary>
        public event EventHandler Disconnected;
        /// <summary>
        /// The event notifies that the connection process did not complete successfully.
        /// </summary>
        public event EventHandler ConnectionFailed;
    }
}
