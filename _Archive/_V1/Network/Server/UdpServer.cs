// Server/Network/UdpServer.cs
// Shared UDP socket for all client sessions

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Network.Shared.Network;

namespace Network.Server.Network
{
    /// <summary>
    /// Delegate for handling packets from unknown endpoints.
    /// </summary>
    public delegate void UnknownEndpointHandler(ReadOnlySpan<byte> data, IPEndPoint endpoint);

    /// <summary>
    /// Manages a single UDP socket shared by all client sessions.
    /// Routes incoming packets to appropriate sessions by endpoint.
    /// Thread-safe for concurrent sends from multiple sessions.
    /// </summary>
    public sealed class UdpServer : IDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _receiveBuffer;
        private readonly SocketAsyncEventArgs _receiveArgs;
        private readonly EndPoint _receiveEndpoint;

        private volatile bool _running;

        // Endpoint → Session mapping for routing
        private readonly ConcurrentDictionary<IPEndPoint, ServerSession> _endpointToSession = new();

        // Token → Session mapping for pending handshakes
        private readonly ConcurrentDictionary<Guid, ServerSession> _pendingHandshakes = new();

        /// <summary>
        /// Event raised when a packet is received for an unknown endpoint.
        /// Used for handling UDP handshakes.
        /// </summary>
        public event UnknownEndpointHandler? OnUnknownEndpoint;

        // Metrics
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsSent;
        private long _packetsReceived;

        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long PacketsSent => Interlocked.Read(ref _packetsSent);
        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

        public bool IsRunning => _running;
        public int ConnectedClients => _endpointToSession.Count;

        public UdpServer(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Allow address reuse for quick restarts
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // Increase buffer sizes
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 1024 * 1024);

            _socket.Bind(new IPEndPoint(IPAddress.Any, port));

            _receiveBuffer = new byte[NetworkConstants.UdpReceiveBufferSize];
            _receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);

            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.RemoteEndPoint = _receiveEndpoint;
            _receiveArgs.Completed += OnReceiveCompleted;

            Console.WriteLine($"[UdpServer] Bound to port {port}");
        }

        /// <summary>
        /// Start receiving UDP packets.
        /// </summary>
        public void Start()
        {
            if (_running)
                return;

            _running = true;
            BeginReceive();
            Console.WriteLine("[UdpServer] Started");
        }

        /// <summary>
        /// Stop the UDP server.
        /// </summary>
        public void Stop()
        {
            _running = false;
            Console.WriteLine("[UdpServer] Stopped");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                       RECEIVING
        // ═══════════════════════════════════════════════════════════════════

        private void BeginReceive()
        {
            if (!_running)
                return;

            try
            {
                _receiveArgs.RemoteEndPoint = _receiveEndpoint;

                if (!_socket.ReceiveFromAsync(_receiveArgs))
                {
                    ProcessReceive(_receiveArgs);
                }
            }
            catch (ObjectDisposedException)
            {
                // Socket closed
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[UdpServer] Receive error: {ex.SocketErrorCode}");
                // Continue receiving despite errors
                if (_running)
                    BeginReceive();
            }
        }

        private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                if (_running)
                    BeginReceive();
                return;
            }

            var endpoint = (IPEndPoint)e.RemoteEndPoint!;
            var data = new ReadOnlySpan<byte>(_receiveBuffer, 0, e.BytesTransferred);

            Interlocked.Add(ref _bytesReceived, e.BytesTransferred);
            Interlocked.Increment(ref _packetsReceived);

            try
            {
                ProcessPacket(data, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpServer] Process error from {endpoint}: {ex.Message}");
            }

            // Continue receiving
            if (_running)
                BeginReceive();
        }

        private void ProcessPacket(ReadOnlySpan<byte> data, IPEndPoint endpoint)
        {
            // Check if this is a known session
            if (_endpointToSession.TryGetValue(endpoint, out var session))
            {
                session.OnUdpReceive(data, endpoint);
                return;
            }

            // Check if this is a handshake packet
            if (data.Length >= NetworkConstants.UdpHandshakeSize &&
                data[0] == NetworkConstants.UdpHandshakeMarker)
            {
                ProcessHandshake(data, endpoint);
                return;
            }

            // Unknown endpoint
            OnUnknownEndpoint?.Invoke(data, endpoint);
        }

        private void ProcessHandshake(ReadOnlySpan<byte> data, IPEndPoint endpoint)
        {
            // [Marker:1][Token:16]
            if (data.Length < NetworkConstants.UdpHandshakeSize)
                return;

            var token = new Guid(data.Slice(1, 16));

            if (_pendingHandshakes.TryRemove(token, out var session))
            {
                if (session.AssociateUdp(endpoint, token))
                {
                    _endpointToSession[endpoint] = session;

                    // Send handshake ACK
                    Span<byte> ack = stackalloc byte[2];
                    ack[0] = NetworkConstants.UdpHandshakeMarker;
                    ack[1] = NetworkConstants.UdpHandshakeAck;
                    SendTo(ack, endpoint);

                    Console.WriteLine($"[UdpServer] Handshake complete for session {session.SessionId} from {endpoint}");
                }
            }
            else
            {
                Console.WriteLine($"[UdpServer] Unknown handshake token from {endpoint}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                       SENDING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send data to a specific endpoint.
        /// Thread-safe, can be called from any thread.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendTo(ReadOnlySpan<byte> data, IPEndPoint endpoint)
        {
            if (!_running || endpoint == null)
                return;

            try
            {
                // Use synchronous send for simplicity - UDP sends are typically fast
                int sent = _socket.SendTo(data.ToArray(), endpoint);

                Interlocked.Add(ref _bytesSent, sent);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[UdpServer] Send error to {endpoint}: {ex.SocketErrorCode}");
            }
        }

        /// <summary>
        /// Async send for when we have a byte array already.
        /// </summary>
        public void SendTo(byte[] data, IPEndPoint endpoint)
        {
            SendTo(data.AsSpan(), endpoint);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   SESSION MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a session for UDP handshake.
        /// </summary>
        public void RegisterPendingHandshake(ServerSession session)
        {
            _pendingHandshakes[session.UdpHandshakeToken] = session;

            // Set up timeout to clean up if handshake doesn't complete
            _ = CleanupPendingHandshake(session.UdpHandshakeToken, NetworkConstants.UdpHandshakeTimeoutMs);
        }

        private async Task CleanupPendingHandshake(Guid token, int timeoutMs)
        {
            await Task.Delay(timeoutMs);
            _pendingHandshakes.TryRemove(token, out _);
        }

        /// <summary>
        /// Remove a session's UDP mapping.
        /// </summary>
        public void UnregisterSession(ServerSession session)
        {
            _pendingHandshakes.TryRemove(session.UdpHandshakeToken, out _);

            if (session.UdpEndPoint != null)
            {
                _endpointToSession.TryRemove(session.UdpEndPoint, out _);
            }
        }

        /// <summary>
        /// Get session by endpoint.
        /// </summary>
        public ServerSession? GetSession(IPEndPoint endpoint)
        {
            _endpointToSession.TryGetValue(endpoint, out var session);
            return session;
        }

        public void Dispose()
        {
            Stop();
            _receiveArgs.Dispose();
            _socket.Close();
            _socket.Dispose();
        }
    }
}
