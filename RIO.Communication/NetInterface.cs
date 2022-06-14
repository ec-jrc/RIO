using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RIO.Communication
{
    /// <summary>
    /// A minimal TCP server, that allows manage multiple clients. It allows one-to-one and broadcast
    /// communication. An automatic house-keeping will take care of failing connections.
    /// </summary>
    public class NetInterface : CancellationTokenSource
    {
        bool active = false;
        IPEndPoint endPoint = null;
        private TcpListener listener;
        private readonly List<TcpClient> clients = new List<TcpClient>();

        /// <summary>
        /// The <see cref="IPEndPoint"/> used to listen for incoming connections.
        /// </summary>
        public IPEndPoint EndPoint
        {
            get => endPoint;
            set
            {
                if (active)
                    throw new InvalidOperationException("Cannot change end point while running");
                endPoint = value;
            }
        }
        /// <summary>
        /// The port number used to listen for incoming connections.
        /// </summary>
        public int Port
        {
            get => endPoint.Port;
        }

        /// <summary>
        /// Default constructor to create a listener on a port number provided by the operating system and
        /// on all available addresses.
        /// </summary>
        public NetInterface()
        {
            endPoint = new IPEndPoint(IPAddress.Any, 0);
        }
        /// <summary>
        /// This constructor creates a listener on a given port number and
        /// on all available addresses.
        /// </summary>
        public NetInterface(int port)
        {
            endPoint = new IPEndPoint(IPAddress.Any, port);
        }
        /// <summary>
        /// This constructor creates a listener on a port number and the addresses provided through an
        /// <see cref="IPEndPoint"/>.
        /// </summary>
        public NetInterface(IPEndPoint ep)
        {
            endPoint = ep;
        }
        /// <summary>
        /// Starts listening for new connections.
        /// </summary>
        /// <returns>true, if the initialization succesds, and false when it is not possible to start
        /// listening, e.g. when the port is already in use.
        /// </returns>
        public bool Start()
        {
            try
            {
                active = true;
                listener = new TcpListener(endPoint);
                listener.Start(5);
                listener.BeginAcceptTcpClient(ListenerAcceptTcpClientCallback, this);
                return true;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    return false;
                throw ex;
            }
        }

        private void ListenerAcceptTcpClientCallback(IAsyncResult ar)
        {
            try
            {
                TcpClient client = listener.EndAcceptTcpClient(ar);
                Task.Run(() => Handle(client));
                Connected?.Invoke(this, client);
            }
            catch (Exception ex)
            {
                // bye
                Error?.Invoke(this, ex);
            }
            finally
            {
                // Start again
                if (active)
                    listener.BeginAcceptTcpClient(ListenerAcceptTcpClientCallback, this);
            }
        }

        private void Handle(TcpClient client)
        {
            lock (clients)
                clients.Add(client);
            using (StreamReader sr = new StreamReader(client.GetStream()))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    Received?.Invoke(client, line);
                    if (!client.Connected) break;
                }
            }
            lock (clients)
                clients.Remove(client);
            Disconnected?.Invoke(this, client);
        }

        /// <summary>
        /// The server is stopped and all the connections closed.
        /// </summary>
        public void Stop()
        {
            active = false;
            listener.Stop();
            foreach (TcpClient client in clients.ToArray())
                try
                {
                    client.Close();
                }
                catch (Exception) { }
        }

        /// <summary>
        /// Send a message to all connected clients.
        /// </summary>
        /// <param name="text">The message to be sent serialized as text.</param>
        public void Broadcast(string text)
        {
            if (!active) return;
            foreach (TcpClient client in clients.ToArray())
                try
                {
                    StreamWriter sw = new StreamWriter(client.GetStream());
                    sw.WriteLine(text);
                    sw.Flush();
                }
                catch (Exception) { }
        }

        /// <summary>
        /// Event raised when a client connects, after the connection is established and the client is
        /// started to be served. The related <see cref="TcpClient"/> is provided.
        /// </summary>
        public event EventHandler<TcpClient> Connected;
        /// <summary>
        /// Event raised when a client disconnects, after the connection is closed and the house-keeping
        /// completed. The related <see cref="TcpClient"/> is provided.
        /// </summary>
        public event EventHandler<TcpClient> Disconnected;
        /// <summary>
        /// Event raised when a client sends a message: it is passed as a string to the event handler.
        /// </summary>
        public event EventHandler<string> Received;
        /// <summary>
        /// Event raised when an error occurs. The related <see cref="Exception"/> is provided.
        /// </summary>
        public event EventHandler<Exception> Error;
    }
}
