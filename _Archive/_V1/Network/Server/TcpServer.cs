// Server/Network/TcpServer.cs
// TCP listener for accepting client connections

using System.Net;
using System.Net.Sockets;
using Network.Shared.Network;

namespace Network.Server.Network
{
    /// <summary>
    /// Accepts incoming TCP connections and creates sessions.
    /// </summary>
    public sealed class TcpServer : IDisposable
    {
        private readonly Socket _listener;
        private readonly SocketAsyncEventArgs _acceptArgs;
        private readonly UdpServer _udpServer;

        private volatile bool _running;

        /// <summary>
        /// Raised when a new TCP connection is accepted.
        /// </summary>
        public event Action<ServerSession>? OnSessionCreated;

        // Metrics
        private long _connectionsAccepted;
        private long _connectionsRejected;

        public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);
        public long ConnectionsRejected => Interlocked.Read(ref _connectionsRejected);
        public bool IsRunning => _running;

        // Connection limits
        private readonly int _maxConnections;
        private int _currentConnections;

        public int CurrentConnections => _currentConnections;
        public int MaxConnections => _maxConnections;

        public TcpServer(int port, UdpServer udpServer, int maxConnections = NetworkConstants.MaxConnections)
        {
            _udpServer = udpServer;
            _maxConnections = maxConnections;

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Socket options
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 64);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 1024 * 64);

            _listener.Bind(new IPEndPoint(IPAddress.Any, port));
            _listener.Listen(NetworkConstants.TcpBacklog);

            _acceptArgs = new SocketAsyncEventArgs();
            _acceptArgs.Completed += OnAcceptCompleted;

            Console.WriteLine($"[TcpServer] Listening on port {port} (max {maxConnections} connections)");
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        public void Start()
        {
            if (_running)
                return;

            _running = true;
            BeginAccept();
            Console.WriteLine("[TcpServer] Started accepting connections");
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            _running = false;
            Console.WriteLine("[TcpServer] Stopped accepting connections");
        }

        private void BeginAccept()
        {
            if (!_running)
                return;

            try
            {
                _acceptArgs.AcceptSocket = null;

                if (!_listener.AcceptAsync(_acceptArgs))
                {
                    ProcessAccept(_acceptArgs);
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener closed
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TcpServer] Accept error: {ex.Message}");
                if (_running)
                    BeginAccept();
            }
        }

        private void OnAcceptCompleted(object? sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            Socket? clientSocket = e.AcceptSocket;

            if (e.SocketError != SocketError.Success || clientSocket == null)
            {
                Console.WriteLine($"[TcpServer] Accept failed: {e.SocketError}");
                if (_running)
                    BeginAccept();
                return;
            }

            var endpoint = clientSocket.RemoteEndPoint as IPEndPoint;

            // Check connection limit
            if (_currentConnections >= _maxConnections)
            {
                Console.WriteLine($"[TcpServer] Rejected connection from {endpoint}: Server full");
                Interlocked.Increment(ref _connectionsRejected);

                try
                {
                    clientSocket.Close();
                }
                catch { }

                if (_running)
                    BeginAccept();
                return;
            }

            // Create session
            Interlocked.Increment(ref _currentConnections);
            Interlocked.Increment(ref _connectionsAccepted);

            var session = new ServerSession(clientSocket, _udpServer);

            // Register for UDP handshake
            _udpServer.RegisterPendingHandshake(session);

            // Wire up disconnect handler
            session.OnDisconnected += OnSessionDisconnected;

            Console.WriteLine($"[TcpServer] Accepted connection from {endpoint} -> Session {session.SessionId}");

            // Notify listeners
            OnSessionCreated?.Invoke(session);

            // Start receiving on the session
            session.StartReceiving();

            // Continue accepting
            if (_running)
                BeginAccept();
        }

        private void OnSessionDisconnected(ServerSession session, string reason)
        {
            Interlocked.Decrement(ref _currentConnections);
            _udpServer.UnregisterSession(session);
        }

        public void Dispose()
        {
            Stop();
            _acceptArgs.Dispose();
            _listener.Close();
            _listener.Dispose();
        }
    }
}
