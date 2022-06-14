using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RIO
{
    public class ChannelMetrics : IMetrics
    {
        public DateTime start = DateTime.UtcNow;
        public int sent = 0;
        public int failed = 0;
        public int received = 0;
        public long bytesSent = (long)0;
        public long bytesReceived = (long)0;
        public string lastError = string.Empty;
    }

    public class RetryMetrics : ChannelMetrics
    {
        public int maxBackLog = 0;
        public int holding = 0;
    }
    /// <summary>
    /// An <see cref="IChannel"/> using a REDIS queue to send messages.
    /// </summary>
    /// <inheritdoc/>
    public class RedisChannel : IChannel
    {
        private readonly string channelName;
        private readonly ChannelMetrics metrics = new ChannelMetrics();
        protected ConnectionMultiplexer redis = null;
        ISubscriber sub = null;

        public RedisChannel(string channelName, ConnectionMultiplexer connection, bool start = true)
        {
            this.channelName = channelName;
            redis = connection;
            if (start)
                GetSubscriber();
        }

        public RedisChannel(string channelName, string connection, bool start = true)
        {
            this.channelName = channelName;
            ConfigurationOptions options = ConfigurationOptions.Parse(connection);
            options.AbortOnConnectFail = false;
            redis = ConnectionMultiplexer.Connect(options);
            if (start)
                GetSubscriber();
        }

        public IMetrics Metrics { get { return metrics; } }

        public event EventHandler<object> Received;

        private void MessageReceived(StackExchange.Redis.RedisChannel channel, RedisValue value)
        {
            metrics.received++;
            string text = value.ToString();
            metrics.bytesReceived += text.Length;
            Received?.Invoke(this, text);
        }

        public void Dispose()
        {
            if (sub != null)
            {
                sub.UnsubscribeAll();
            }
            sub = null;
            redis?.Dispose();
        }

        private ISubscriber Subscriber
        {
            get
            {
                return GetSubscriber();
            }
        }

        private ISubscriber GetSubscriber()
        {
            if (sub == null)
                try
                {
                    sub = redis.GetSubscriber();
                    sub.Subscribe(channelName, MessageReceived);
                }
                catch
                {
                    sub = null;
                    metrics.failed++;
                    return null;
                }
            return sub;
        }

        public async Task<TransmissionResult> Send(object o)
        {
            int size;
            if (Subscriber == null)
                return TransmissionResult.NoConnection;

            try
            {
                string s = o.ToString();
                await Subscriber.PublishAsync(channelName, s);
                size = s.Length;
            }
            catch (Exception ex)
            {
                sub = null;
                metrics.failed++;
                metrics.lastError = ex.Message;
                return TransmissionResult.Failed;
            }
            metrics.sent++;
            metrics.bytesSent += size;
            return TransmissionResult.OK;
        }
    }
    /// <summary>
    /// This <see cref="IChannel"/> uses an <see cref="UdpClient"/> to communicates.
    /// </summary>
    /// <inheritdoc/>
    public class UdpChannel : IChannel
    {
        private readonly int localPort = -1;
        private readonly bool synchronous;
        private readonly DnsEndPoint remote = null;
        private UdpClient client;
        private readonly ChannelMetrics metrics = new ChannelMetrics();

        public UdpChannel(string endPoint, bool synchronous = false) : this(endPoint, -1, synchronous) { }
        public UdpChannel(int localPort, bool synchronous = false) : this(null, localPort, synchronous) { }
        public UdpChannel(string endPoint, int localPort, bool synchronous = false)
        {
            this.localPort = localPort;
            this.synchronous = synchronous;
            if (endPoint != null)
            {
                string[] parts = endPoint?.Split(':');
                if (parts?.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    this.remote = new DnsEndPoint(parts[0], port);
                }
            }

            Task<UdpClient> task = GetSocket();
            task.Wait();
            client = task.Result;
        }

        private void OnReceived(IAsyncResult ar)
        {
            IPEndPoint e = new IPEndPoint(IPAddress.Any, 0);

            byte[] store = client.EndReceive(ar, ref e);

            client.BeginReceive(OnReceived, this);
            metrics.received++;
            metrics.bytesReceived += store.Length;

            Received?.Invoke(this, store);
        }

        public IMetrics Metrics { get { return metrics; } }

        public event EventHandler<object> Received;

        public void Dispose() { client?.Close(); }

        public async Task<TransmissionResult> Send(object o)
        {
            if (remote == null)
                return TransmissionResult.NoConnection;

            if (client == null)
                client = await GetSocket();

            if (o == null)
                return TransmissionResult.OK;
            byte[] data = o as byte[];

            try
            {
                int sent = await client.SendAsync(data, data.Length, remote.Host, remote.Port);
                metrics.bytesSent += sent;
                metrics.sent++;
            }
            catch (Exception ex)
            {
                metrics.failed++;
                metrics.lastError = ex.Message;
                return TransmissionResult.Failed;
            }
            return TransmissionResult.OK;
        }

        Task<UdpClient> GetSocket()
        {
            try
            {
                if (client?.Client != null)
                    return Task.FromResult(client);
                else
                    client = null;

                IPEndPoint ipLocalEndPoint = new IPEndPoint(IPAddress.Any, localPort != -1 ? localPort : 0);

                client = new UdpClient(ipLocalEndPoint);

                if (!synchronous)
                    client.BeginReceive(OnReceived, this);

                return Task.FromResult(client);
            }
            catch { }
            return null;
        }

        public async Task<byte[]> Receive()
        {
            if (client != null && client.Client.IsBound)
            {
                UdpReceiveResult result = await client.ReceiveAsync();
                return result.Buffer;
            }
            return null;
        }
    }
    /// <summary>
    /// This <see cref="IChannel"/> posts the messages onto an url, and it is not able to receive.
    /// </summary>
    /// <inheritdoc/>
    public class WebChannel : IChannel
    {
        private readonly string postUrl;
        private readonly ChannelMetrics metrics = new ChannelMetrics();

        public WebChannel(string url)
        {
            this.postUrl = url;
        }

        public IMetrics Metrics { get { return metrics; } }
        /// <summary>
        /// Requested by the <see cref="IChannel"/> implementation, but not used, since no unsolicited data may arrive from the web.
        /// </summary>
        public event EventHandler<object> Received;

        public void Dispose() { }

        public async Task<TransmissionResult> Send(object o)
        {
            try
            {
                Uri uri = new Uri(postUrl);
                byte[] bytes = Encoding.UTF8.GetBytes(o.ToString());
                ByteArrayContent baContent = new ByteArrayContent(bytes);
                using (HttpClient client = new HttpClient())
                {
                    HttpContent content = baContent;
                    content.Headers.Add("Content-Type", "application/json");
                    HttpResponseMessage response = await client.PostAsync(uri, content);
                    switch (response.StatusCode)
                    {
                        case System.Net.HttpStatusCode.OK:
                        case System.Net.HttpStatusCode.NoContent:
                        case System.Net.HttpStatusCode.Conflict:
                            break;
                        default:
                            throw new Exception(string.Format("Error while storing telemetry on server: {0}", response.StatusCode));
                    }
                    metrics.sent++;
                    metrics.bytesSent += bytes.Length;
                    return TransmissionResult.OK;
                }
            }
            catch (HttpRequestException ex)
            {
                metrics.failed++;
                metrics.lastError = ex.Message;
                return TransmissionResult.NoConnection;
            }
            catch (Exception ex)
            {
                metrics.failed++;
                metrics.lastError = ex.Message;
                return TransmissionResult.Failed;
            }
        }
    }
    /// <summary>
    /// This <see cref="IChannel"/> uses an underlying channel to send and receive messages. In case
    /// of failed delivery of a message, it is stored in a backlog buffer to be sent again later.
    /// </summary>
    /// <inheritdoc/>
    public class RetryChannel : IChannel
    {
        private Stack<object> backLog = new Stack<object>();
        private readonly IChannel channel;
        private int observers = 0;
        private readonly object access = new object();
        private readonly RetryMetrics metrics = new RetryMetrics();
        private DateTime failedSince = DateTime.MaxValue, failedLast;

        public RetryChannel(IChannel channel)
        {
            this.channel = channel;
        }

        public IMetrics Metrics { get { return metrics; } }

        private EventHandler<object> handler;
        public event EventHandler<object> Received
        {
            add
            {
                lock (access)
                {
                    if (observers <= 0)
                    {
                        channel.Received += Channel_Received;
                        observers = 0;
                    }
                    observers++;
                }
                handler += value;
            }
            remove
            {
                handler -= value;
                lock (access)
                {
                    observers--;
                    if (observers <= 0)
                        channel.Received -= Channel_Received;
                }
            }
        }

        private void Channel_Received(object sender, object e)
        {
            metrics.received++;
            metrics.bytesReceived += e.ToString().Length;
            handler?.Invoke(this, e);
        }

        public void Dispose()
        {
            channel.Dispose();
        }

        public async Task<TransmissionResult> Send(object o)
        {
            ChannelMetrics channelMetrics = channel.Metrics as ChannelMetrics;
            long pre = channelMetrics.bytesSent;
            TransmissionResult result = await channel.Send(o);
            switch (result)
            {
                case TransmissionResult.OK:
                    metrics.sent++;
                    metrics.bytesSent += (channelMetrics.bytesSent - pre);
                    var failed = await Consume(backLog);
                    if (failed.Count > 0)
                    {
                        failedSince = DateTime.MaxValue;
                        backLog = failed;
                    }
                    return TransmissionResult.OK;
                case TransmissionResult.Failed:
                    metrics.lastError = channelMetrics.lastError;
                    result = TransmissionResult.Failed;
                    break;
                case TransmissionResult.NoConnection:
                    if (result != TransmissionResult.Failed)
                        result = TransmissionResult.NoConnection;
                    break;
            }
            lock (backLog)
                backLog.Push(o);
            int c = backLog.Count;
            metrics.maxBackLog = Math.Max(metrics.maxBackLog, c);
            metrics.holding = c;
            metrics.failed++;
            failedLast = DateTime.UtcNow;
            if (failedLast < failedSince) failedSince = failedLast;

            if (c % 500 == 0)
            {
                RetryEventArgs rea = new RetryEventArgs() { FirstFailure = failedSince };
                lock (backLog)
                    rea.BackLog = backLog.ToArray().Reverse().ToArray();
                rea.FirstFailure = failedSince;
                CumulatedUnsent?.Invoke(this, rea);
            }
            return result;
        }

        private async Task<Stack<object>> Consume(Stack<object> failed)
        {
            Stack<object> next = new Stack<object>();
            while (failed.Count > 0)
            {
                object o = null;
                lock (failed)
                    if (failed.Count == 0)
                        break;
                    else o = failed.Pop();

                ChannelMetrics channelMetrics = channel.Metrics as ChannelMetrics;
                long pre = channelMetrics.bytesSent;
                TransmissionResult result = await channel.Send(o);
                if (result != TransmissionResult.OK)
                    next.Push(o);
                metrics.sent++;
                metrics.bytesSent += (channelMetrics.bytesSent - pre);
            }
            return next;
        }

        public async Task<Stack<object>> Recover(IEnumerable<object> failed)
        {
            Stack<object> backLog = new Stack<object>(failed);
            if (backLog.Count == 0)
                return backLog;
            return await Consume(backLog);
        }

        public class RetryEventArgs : EventArgs
        {
            public DateTime FirstFailure { get; set; }
            public object[] BackLog { get; set; }
        }

        public event EventHandler<RetryEventArgs> CumulatedUnsent;
    }
    /// <summary>
    /// The behaviour of a <see cref="TeamChannel"/> is determined by the chosen logic.
    /// </summary>
    public enum TeamLogic
    {
        /// <summary>
        /// A quorum of the Channels is required to succeed for the <see cref="TeamChannel"/> to succeed.
        /// </summary>
        Any,
        /// <summary>
        /// All the Channels are required to succeed for the <see cref="TeamChannel"/> to succeed.
        /// </summary>
        All
    }

    /// <summary>
    /// An <see cref="IChannel"/> implemented to use several channels at the same time: the 
    /// sending action will succeed if all, or enough channels succeed, depending from the chosen
    /// <see cref="TeamLogic"/>.
    /// </summary>
    /// <inheritdoc/>
    public class TeamChannel : IChannel
    {
        private readonly IChannel[] channels;
        private int observers = 0;
        private readonly object access = new object();
        private readonly ChannelMetrics metrics = new ChannelMetrics();
        private readonly float quorum;
        private readonly TeamLogic logic;

        public TeamChannel(params IChannel[] channels) : this(TeamLogic.Any, 0.5F, channels) { }

        public TeamChannel(TeamLogic logic, float quorum = 0.5F, params IChannel[] channels)
        {
            this.quorum = quorum;
            this.logic = logic;
            this.channels = channels;
        }

        public IMetrics Metrics { get { return metrics; } }

        private EventHandler<object> handler;
        public event EventHandler<object> Received
        {
            add
            {
                lock (access)
                {
                    if (observers <= 0)
                    {
                        foreach (IChannel ch in channels)
                            ch.Received += Ch_Received;
                        observers = 0;
                    }
                    observers++;
                }
                handler += value;
            }
            remove
            {
                handler -= value;
                lock (access)
                {
                    observers--;
                    if (observers <= 0)
                    {
                        foreach (IChannel ch in channels)
                            ch.Received -= Ch_Received;
                    }
                }
            }
        }

        private void Ch_Received(object sender, object e)
        {
            metrics.received++;
            metrics.bytesReceived += e.ToString().Length;
            handler?.Invoke(this, e);
        }

        public void Dispose()
        {
            foreach (IChannel ch in channels)
                ch.Dispose();
        }

        public async Task<TransmissionResult> Send(object o)
        {
            if (logic == TeamLogic.Any)
            {
                TransmissionResult result = TransmissionResult.Failed;
                foreach (IChannel ch in channels)
                {
                    ChannelMetrics channelMetrics = ch.Metrics as ChannelMetrics;
                    long pre = channelMetrics.bytesSent;
                    result = await ch.Send(o);
                    switch (result)
                    {
                        case TransmissionResult.OK:
                            metrics.sent++;
                            metrics.bytesSent += (channelMetrics.bytesSent - pre);
                            return TransmissionResult.OK;
                        case TransmissionResult.Failed:
                            metrics.lastError = channelMetrics.lastError;
                            result = TransmissionResult.Failed;
                            break;
                        case TransmissionResult.NoConnection:
                            if (result != TransmissionResult.Failed)
                                result = TransmissionResult.NoConnection;
                            break;
                    }
                }
                metrics.failed++;
                return result;
            }
            else
            {
                TransmissionResult result = TransmissionResult.OK;
                long sent = 0;
                int success = 0;
                foreach (IChannel ch in channels)
                {
                    ChannelMetrics channelMetrics = ch.Metrics as ChannelMetrics;
                    long pre = channelMetrics.bytesSent;
                    result = await ch.Send(o);
                    switch (result)
                    {
                        case TransmissionResult.OK:
                            success++;
                            sent += (channelMetrics.bytesSent - pre);
                            break;
                        case TransmissionResult.Failed:
                            metrics.lastError = channelMetrics.lastError;
                            result = TransmissionResult.Failed;
                            break;
                        case TransmissionResult.NoConnection:
                            if (result != TransmissionResult.Failed)
                                result = TransmissionResult.NoConnection;
                            break;
                    }
                }
                metrics.bytesSent += sent;
                if (success / (float)channels.Length < quorum)
                {
                    result = (result == TransmissionResult.OK) ? TransmissionResult.Failed : result;
                }
                if (result == TransmissionResult.OK)
                    metrics.sent++;
                else
                    metrics.failed++;
                return result;
            }
        }
    }

    /// <summary>
    /// An <see cref="IChannel"/> implemented using a <see cref="TcpClient"/>.
    /// </summary>
    /// <inheritdoc/>
    public class SocketChannel : IChannel
    {
        private readonly ChannelMetrics metrics = new ChannelMetrics();
        private readonly string casterUrl;
        private Socket client;
        private const int BUF_LEN = 12288;
        readonly byte[] readBuffer = new byte[BUF_LEN];

        public SocketChannel(string casterUrl)
        {
            this.casterUrl = casterUrl;
            GetSocket().Result.BeginReceive(readBuffer, 0, BUF_LEN, SocketFlags.None, ReceiveSocketCallback, this);
        }

        private async void ReceiveSocketCallback(IAsyncResult ar)
        {
            SocketChannel channel = (SocketChannel)ar.AsyncState;

            try
            {
                int numberOfBytesRead = client.EndReceive(ar);
                if (numberOfBytesRead == 0)
                {
                    client.Close();
                    client.Dispose();

                    return;
                }
                byte[] store = new byte[numberOfBytesRead];

                Array.Copy(readBuffer, store, numberOfBytesRead);

                client.BeginReceive(readBuffer, 0, BUF_LEN, SocketFlags.None, ReceiveSocketCallback, this);
                metrics.received++;
                metrics.bytesReceived += store.Length;

                Received?.Invoke(this, store);
            }
            catch
            {
                (await GetSocket()).BeginReceive(readBuffer, 0, BUF_LEN, SocketFlags.None, ReceiveSocketCallback, this);
            }
        }

        public IMetrics Metrics { get { return metrics; } }

        public event EventHandler<object> Received;
        public event EventHandler Connected;

        public void Dispose()
        {
            client?.Dispose();
        }

        async Task<Socket> GetSocket()
        {
            try
            {
                if (client?.Connected == true)
                    return client;
                else
                    client = null;
                client = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                string[] parts = casterUrl.Split(':');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[1], out int portno))
                        await client.ConnectAsync(parts[0], portno);
                    if (client.Connected)
                        Connected?.Invoke(this, new EventArgs());
                    return client;
                }
            }
            // Ignore all exceptions
            catch { }

            return null;
        }
        public async Task<TransmissionResult> Send(object o)
        {
            if (o == null)
                return TransmissionResult.OK;

            byte[] data = o as byte[];
            try
            {
                await GetSocket();
                int sent = await client.SendAsync(new ArraySegment<byte>(data), SocketFlags.None);
                metrics.bytesSent += sent;
                metrics.sent++;
            }
            catch (Exception ex)
            {
                metrics.failed++;
                metrics.lastError = ex.Message;
                return TransmissionResult.Failed;
            }
            return TransmissionResult.OK;
        }
    }
}
