// Client/Network/NetworkClient.cs
// Client-side network manager

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Network.Shared.Network;
using Network.Shared.Network.Packets;

namespace Network.Client.Network
{
    /// <summary>
    /// Client network manager. Handles TCP/UDP connections to server.
    /// </summary>
    public sealed class NetworkClient : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════
        //                          SOCKETS
        // ═══════════════════════════════════════════════════════════════════

        private Socket? _tcpSocket;
        private Socket? _udpSocket;

        private readonly byte[] _tcpReceiveBuffer;
        private readonly byte[] _udpReceiveBuffer;
        private readonly ByteRingBuffer _tcpRingBuffer;

        private readonly SocketAsyncEventArgs _tcpReceiveArgs;
        private readonly SocketAsyncEventArgs _tcpSendArgs;
        private readonly SocketAsyncEventArgs _udpReceiveArgs;

        private readonly ConcurrentQueue<BufferSegment> _tcpSendQueue = new();
        private int _tcpSendPending;

        // ═══════════════════════════════════════════════════════════════════
        //                          STATE
        // ═══════════════════════════════════════════════════════════════════

        private ConnectionState _state = ConnectionState.Disconnected;
        public ConnectionState State => _state;

        private IPEndPoint? _serverTcpEndpoint;
        private IPEndPoint? _serverUdpEndpoint;

        // Session info from server
        private uint _sessionId;
        private Guid _udpToken;

        public uint SessionId => _sessionId;
        public bool IsConnected => _state == ConnectionState.Connected;
        public bool IsUdpEstablished => _udpSocket != null && _serverUdpEndpoint != null;

        // ═══════════════════════════════════════════════════════════════════
        //                        UDP SEQUENCING
        // ═══════════════════════════════════════════════════════════════════

        private int _udpSendSequence;  // Using int for Interlocked.Increment support
        private ushort _udpReceiveSequence;
        private uint _udpAckBits;
        private readonly ConcurrentDictionary<ushort, PendingUdpPacket> _pendingAcks = new();

        private struct PendingUdpPacket
        {
            public byte[] Data;
            public DateTime SentAt;
            public int RetryCount;
            public ChannelType Channel;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      PACKET HANDLING
        // ═══════════════════════════════════════════════════════════════════

        private readonly PacketRegistry<NetworkClient> _packetRegistry;

        // ═══════════════════════════════════════════════════════════════════
        //                          EVENTS
        // ═══════════════════════════════════════════════════════════════════

        public event Action? OnConnected;
        public event Action<string>? OnDisconnected;
        public event Action<string>? OnConnectionFailed;

        // ═══════════════════════════════════════════════════════════════════
        //                         METRICS
        // ═══════════════════════════════════════════════════════════════════

        private long _tcpBytesSent;
        private long _tcpBytesReceived;
        private long _udpBytesSent;
        private long _udpBytesReceived;

        public long TcpBytesSent => Interlocked.Read(ref _tcpBytesSent);
        public long TcpBytesReceived => Interlocked.Read(ref _tcpBytesReceived);
        public long UdpBytesSent => Interlocked.Read(ref _udpBytesSent);
        public long UdpBytesReceived => Interlocked.Read(ref _udpBytesReceived);

        // ═══════════════════════════════════════════════════════════════════
        //                        CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════

        private readonly ushort _protocolVersion;
        private readonly string _clientVersion;

        public NetworkClient(ushort protocolVersion = 1, string clientVersion = "1.0.0")
        {
            _protocolVersion = protocolVersion;
            _clientVersion = clientVersion;

            _tcpReceiveBuffer = new byte[NetworkConstants.TcpReceiveBufferSize];
            _udpReceiveBuffer = new byte[NetworkConstants.UdpReceiveBufferSize];
            _tcpRingBuffer = new ByteRingBuffer(NetworkConstants.TcpRingBufferSize);

            _tcpReceiveArgs = new SocketAsyncEventArgs();
            _tcpReceiveArgs.SetBuffer(_tcpReceiveBuffer, 0, _tcpReceiveBuffer.Length);
            _tcpReceiveArgs.Completed += OnTcpIoCompleted;

            _tcpSendArgs = new SocketAsyncEventArgs();
            _tcpSendArgs.Completed += OnTcpIoCompleted;

            _udpReceiveArgs = new SocketAsyncEventArgs();
            _udpReceiveArgs.SetBuffer(_udpReceiveBuffer, 0, _udpReceiveBuffer.Length);
            _udpReceiveArgs.Completed += OnUdpReceiveCompleted;

            _packetRegistry = new PacketRegistry<NetworkClient>();

            // Register system handlers
            RegisterSystemHandlers();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                        CONNECTION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connect to server asynchronously.
        /// </summary>
        public async Task ConnectAsync(string host, int tcpPort, int udpPort, CancellationToken ct = default)
        {
            if (_state != ConnectionState.Disconnected)
                throw new InvalidOperationException($"Already in state: {_state}");

            _state = ConnectionState.Connecting;

            try
            {
                // Resolve host
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? throw new Exception($"Could not resolve {host}");

                _serverTcpEndpoint = new IPEndPoint(address, tcpPort);
                _serverUdpEndpoint = new IPEndPoint(address, udpPort);

                // Connect TCP
                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _tcpSocket.NoDelay = true;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(NetworkConstants.TcpConnectTimeoutMs);

                await _tcpSocket.ConnectAsync(_serverTcpEndpoint, timeoutCts.Token);

                // Start receiving TCP
                BeginTcpReceive();

                // Send handshake
                Send(new ClientHello(_protocolVersion, _clientVersion));

                Console.WriteLine($"[NetworkClient] TCP connected to {_serverTcpEndpoint}");
            }
            catch (Exception ex)
            {
                _state = ConnectionState.Disconnected;
                CleanupSockets();
                OnConnectionFailed?.Invoke(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Disconnect from server.
        /// </summary>
        public void Disconnect(string reason = "Client disconnect")
        {
            if (_state == ConnectionState.Disconnected)
                return;

            var wasConnected = _state == ConnectionState.Connected;

            // Send disconnect packet if connected
            if (wasConnected)
            {
                try
                {
                    Send(new Disconnect(DisconnectReason.ClientQuit, reason));
                }
                catch { }
            }

            _state = ConnectionState.Disconnected;
            CleanupSockets();

            Console.WriteLine($"[NetworkClient] Disconnected: {reason}");
            OnDisconnected?.Invoke(reason);
        }

        private void CleanupSockets()
        {
            try { _tcpSocket?.Shutdown(SocketShutdown.Both); } catch { }
            _tcpSocket?.Close();
            _tcpSocket?.Dispose();
            _tcpSocket = null;

            _udpSocket?.Close();
            _udpSocket?.Dispose();
            _udpSocket = null;

            _tcpRingBuffer.Clear();
            while (_tcpSendQueue.TryDequeue(out var seg))
                BufferPool.Return(seg.Array!);

            _pendingAcks.Clear();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      PACKET HANDLING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a packet handler.
        /// </summary>
        public void RegisterHandler<TPacket>(PacketHandler<NetworkClient, TPacket> handler)
            where TPacket : struct, IPacket<TPacket>
        {
            _packetRegistry.Register(handler);
        }

        private void RegisterSystemHandlers()
        {
            RegisterHandler<ServerHello>(OnServerHello);
            RegisterHandler<ConnectionRejected>(OnConnectionRejected);
            RegisterHandler<Pong>(OnPong);
            RegisterHandler<Disconnect>(OnServerDisconnect);
            RegisterHandler<UdpHandshakeAck>(OnUdpHandshakeAck);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                        SENDING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send a packet using its declared channel.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send<TPacket>(in TPacket packet) where TPacket : struct, IPacket
        {
            Send(TPacket.Channel, TPacket.PacketId, in packet);
        }

        /// <summary>
        /// Send with explicit channel override.
        /// </summary>
        public void Send<TPacket>(ChannelType channel, in TPacket packet) where TPacket : struct, IPacket
        {
            Send(channel, TPacket.PacketId, in packet);
        }

        private void Send<TPacket>(ChannelType channel, ushort packetId, in TPacket packet)
            where TPacket : struct, IPacket
        {
            if (_state == ConnectionState.Disconnected)
                return;

            var buffer = BufferPool.Rent(NetworkConstants.DefaultPoolBufferSize);

            try
            {
                var config = ChannelConfig.Get(channel);
                int headerSize = config.Protocol == TransportProtocol.TCP
                    ? TcpPacketHeader.Size
                    : UdpPacketHeader.Size;

                var payloadSpan = buffer.AsSpan(headerSize);
                var writer = new BitWriter(payloadSpan);
                packet.Serialize(ref writer);
                int payloadLength = writer.BytesWritten;

                if (config.Protocol == TransportProtocol.TCP || !IsUdpEstablished)
                {
                    SendTcpRaw(channel, packetId, buffer, payloadLength);
                }
                else
                {
                    SendUdpRaw(channel, packetId, buffer, payloadLength);
                }
            }
            catch
            {
                BufferPool.Return(buffer);
                throw;
            }
        }

        private void SendTcpRaw(ChannelType channel, ushort packetId, byte[] buffer, int payloadLength)
        {
            int totalLength = TcpPacketHeader.Size + payloadLength;
            var header = TcpPacketHeader.Create(packetId, channel, payloadLength);
            header.Write(buffer.AsSpan());

            _tcpSendQueue.Enqueue(new BufferSegment(buffer, 0, totalLength));
            FlushTcpSendQueue();
        }

        private void SendUdpRaw(ChannelType channel, ushort packetId, byte[] buffer, int payloadLength)
        {
            if (_udpSocket == null || _serverUdpEndpoint == null)
            {
                BufferPool.Return(buffer);
                return;
            }

            int totalLength = UdpPacketHeader.Size + payloadLength;
            ushort sequence = (ushort)Interlocked.Increment(ref _udpSendSequence);

            var header = new UdpPacketHeader(channel, sequence, _udpReceiveSequence, _udpAckBits, packetId);
            header.Write(buffer.AsSpan());

            var config = ChannelConfig.Get(channel);
            if (config.RequiresAck)
            {
                var copy = new byte[totalLength];
                buffer.AsSpan(0, totalLength).CopyTo(copy);
                _pendingAcks[sequence] = new PendingUdpPacket
                {
                    Data = copy,
                    SentAt = DateTime.UtcNow,
                    RetryCount = 0,
                    Channel = channel
                };
            }

            try
            {
                _udpSocket.SendTo(buffer, 0, totalLength, SocketFlags.None, _serverUdpEndpoint);
                Interlocked.Add(ref _udpBytesSent, totalLength);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[NetworkClient] UDP send error: {ex.SocketErrorCode}");
            }

            BufferPool.Return(buffer);
        }

        private void FlushTcpSendQueue()
        {
            if (Interlocked.CompareExchange(ref _tcpSendPending, 1, 0) != 0)
                return;

            if (_tcpSocket == null || !_tcpSocket.Connected)
            {
                Interlocked.Exchange(ref _tcpSendPending, 0);
                return;
            }

            if (!_tcpSendQueue.TryDequeue(out var segment))
            {
                Interlocked.Exchange(ref _tcpSendPending, 0);
                return;
            }

            _tcpSendArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
            _tcpSendArgs.UserToken = segment;

            try
            {
                if (!_tcpSocket.SendAsync(_tcpSendArgs))
                {
                    ProcessTcpSend(_tcpSendArgs);
                }
            }
            catch (Exception)
            {
                BufferPool.Return(segment.Array!);
                Interlocked.Exchange(ref _tcpSendPending, 0);
                Disconnect("Send error");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      TCP RECEIVING
        // ═══════════════════════════════════════════════════════════════════

        private void BeginTcpReceive()
        {
            if (_tcpSocket == null || !_tcpSocket.Connected)
                return;

            try
            {
                if (!_tcpSocket.ReceiveAsync(_tcpReceiveArgs))
                {
                    ProcessTcpReceive(_tcpReceiveArgs);
                }
            }
            catch
            {
                Disconnect("Receive error");
            }
        }

        private void OnTcpIoCompleted(object? sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessTcpReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessTcpSend(e);
                    break;
            }
        }

        private void ProcessTcpReceive(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred == 0 || e.SocketError != SocketError.Success)
            {
                Disconnect(e.BytesTransferred == 0 ? "Server closed connection" : $"Receive error: {e.SocketError}");
                return;
            }

            Interlocked.Add(ref _tcpBytesReceived, e.BytesTransferred);
            _tcpRingBuffer.Write(new ReadOnlySpan<byte>(_tcpReceiveBuffer, 0, e.BytesTransferred));

            ProcessTcpPackets();
            BeginTcpReceive();
        }

        private void ProcessTcpPackets()
        {
            while (_tcpRingBuffer.Count >= TcpPacketHeader.Size)
            {
                if (!_tcpRingBuffer.TryPeekUInt16(0, out ushort length))
                    break;

                if (length < TcpPacketHeader.Size || length > NetworkConstants.MaxTcpPacketLength)
                {
                    Disconnect("Invalid packet length");
                    return;
                }

                if (_tcpRingBuffer.Count < length)
                    break;

                var packetBuffer = BufferPool.Rent(length);
                try
                {
                    _tcpRingBuffer.Read(packetBuffer.AsSpan(0, length));
                    var header = TcpPacketHeader.Read(packetBuffer);

                    var payload = packetBuffer.AsSpan(TcpPacketHeader.Size, header.PayloadLength);
                    Console.WriteLine($"[NetworkClient] Received packet 0x{header.PacketId:X4} on {header.Channel} (len={header.PayloadLength})");
                    _packetRegistry.Dispatch(this, header.PacketId, payload);
                }
                finally
                {
                    BufferPool.Return(packetBuffer);
                }
            }
        }

        private void ProcessTcpSend(SocketAsyncEventArgs e)
        {
            if (e.UserToken is BufferSegment segment)
                BufferPool.Return(segment.Array!);

            Interlocked.Add(ref _tcpBytesSent, e.BytesTransferred);

            if (e.SocketError != SocketError.Success)
            {
                Interlocked.Exchange(ref _tcpSendPending, 0);
                Disconnect($"Send error: {e.SocketError}");
                return;
            }

            Interlocked.Exchange(ref _tcpSendPending, 0);
            FlushTcpSendQueue();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      UDP HANDLING
        // ═══════════════════════════════════════════════════════════════════

        private void SetupUdp(Guid token, int port)
        {
            _udpToken = token;
            _serverUdpEndpoint = new IPEndPoint(_serverTcpEndpoint!.Address, port);

            _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            BeginUdpReceive();
            SendUdpHandshake();

            Console.WriteLine($"[NetworkClient] UDP setup for {_serverUdpEndpoint}");
        }

        private void SendUdpHandshake()
        {
            if (_udpSocket == null || _serverUdpEndpoint == null)
                return;

            // [Marker:1][Token:16]
            var buffer = new byte[NetworkConstants.UdpHandshakeSize];
            buffer[0] = NetworkConstants.UdpHandshakeMarker;
            _udpToken.TryWriteBytes(buffer.AsSpan(1, 16));

            try
            {
                _udpSocket.SendTo(buffer, _serverUdpEndpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] UDP handshake send error: {ex.Message}");
            }
        }

        private void BeginUdpReceive()
        {
            if (_udpSocket == null)
                return;

            try
            {
                _udpReceiveArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                if (!_udpSocket.ReceiveFromAsync(_udpReceiveArgs))
                {
                    ProcessUdpReceive(_udpReceiveArgs);
                }
            }
            catch { }
        }

        private void OnUdpReceiveCompleted(object? sender, SocketAsyncEventArgs e)
        {
            ProcessUdpReceive(e);
        }

        private void ProcessUdpReceive(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                BeginUdpReceive();
                return;
            }

            Interlocked.Add(ref _udpBytesReceived, e.BytesTransferred);

            var data = new ReadOnlySpan<byte>(_udpReceiveBuffer, 0, e.BytesTransferred);

            // Check for handshake ACK
            if (data.Length >= 2 && data[0] == NetworkConstants.UdpHandshakeMarker)
            {
                // Handled by OnUdpHandshakeAck packet
            }
            else if (data.Length >= UdpPacketHeader.Size)
            {
                ProcessUdpPacket(data);
            }

            BeginUdpReceive();
        }

        private void ProcessUdpPacket(ReadOnlySpan<byte> data)
        {
            if (!UdpPacketHeader.TryRead(data, out var header))
                return;

            // Process ACKs
            ProcessUdpAcks(header.Ack, header.AckBits);

            // Check sequencing
            var config = ChannelConfig.Get(header.Channel);
            if (config.RequiresOrdering && !SequenceNumber.IsNewer(header.Sequence, _udpReceiveSequence))
                return;

            UpdateUdpReceiveSequence(header.Sequence);

            var payload = data.Slice(UdpPacketHeader.Size);
            _packetRegistry.Dispatch(this, header.PacketId, payload);
        }

        private void ProcessUdpAcks(ushort ack, uint ackBits)
        {
            _pendingAcks.TryRemove(ack, out _);
            for (int i = 0; i < 32; i++)
            {
                if ((ackBits & (1u << i)) != 0)
                    _pendingAcks.TryRemove((ushort)(ack - 1 - i), out _);
            }
        }

        private void UpdateUdpReceiveSequence(ushort sequence)
        {
            if (SequenceNumber.IsNewer(sequence, _udpReceiveSequence))
            {
                int diff = SequenceNumber.Difference(sequence, _udpReceiveSequence);
                _udpAckBits = diff <= 32 ? (_udpAckBits << diff) | (1u << (diff - 1)) : 0;
                _udpReceiveSequence = sequence;
            }
            else
            {
                int diff = SequenceNumber.Difference(_udpReceiveSequence, sequence);
                if (diff <= 32)
                    _udpAckBits |= 1u << (diff - 1);
            }
        }

        /// <summary>
        /// Process UDP retries. Call from game loop.
        /// </summary>
        public void ProcessUdpRetries()
        {
            if (!IsUdpEstablished || _pendingAcks.IsEmpty)
                return;

            var now = DateTime.UtcNow;

            foreach (var kvp in _pendingAcks)
            {
                var config = ChannelConfig.Get(kvp.Value.Channel);
                if ((now - kvp.Value.SentAt).TotalMilliseconds > config.RetryIntervalMs)
                {
                    if (kvp.Value.RetryCount >= config.MaxRetries)
                    {
                        _pendingAcks.TryRemove(kvp.Key, out _);
                    }
                    else
                    {
                        var pending = kvp.Value;
                        pending.SentAt = now;
                        pending.RetryCount++;
                        _pendingAcks[kvp.Key] = pending;

                        try
                        {
                            _udpSocket!.SendTo(pending.Data, _serverUdpEndpoint!);
                            Interlocked.Add(ref _udpBytesSent, pending.Data.Length);
                        }
                        catch { }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   SYSTEM PACKET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnServerHello(NetworkClient client, in ServerHello packet)
        {
            _sessionId = packet.SessionId;
            _state = ConnectionState.Connected;

            Console.WriteLine($"[NetworkClient] Connected! SessionId={_sessionId}");

            if (packet.UdpEnabled)
            {
                SetupUdp(packet.UdpToken, packet.UdpPort);
            }

            OnConnected?.Invoke();
        }

        private void OnConnectionRejected(NetworkClient client, in ConnectionRejected packet)
        {
            Console.WriteLine($"[NetworkClient] Connection rejected: {packet.Reason} - {packet.Message}");
            Disconnect($"Rejected: {packet.Message}");
        }

        private void OnPong(NetworkClient client, in Pong packet)
        {
            long rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packet.OriginalTimestamp;
            Console.WriteLine($"[NetworkClient] Pong received, RTT={rtt}ms");
        }

        private void OnServerDisconnect(NetworkClient client, in Disconnect packet)
        {
            Disconnect($"Server: {packet.Reason} - {packet.Message}");
        }

        private void OnUdpHandshakeAck(NetworkClient client, in UdpHandshakeAck packet)
        {
            if (packet.Success)
            {
                Console.WriteLine("[NetworkClient] UDP handshake complete");
            }
        }

        public void Dispose()
        {
            Disconnect("Disposed");
            _tcpReceiveArgs.Dispose();
            _tcpSendArgs.Dispose();
            _udpReceiveArgs.Dispose();
        }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }
}
