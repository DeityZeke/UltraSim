// Server/Network/ServerSession.cs
// Server-side session managing both TCP and UDP connections for a single client

using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Network.Shared.Network;

namespace Network.Server.Network
{
    /// <summary>
    /// Delegate for handling received packets.
    /// </summary>
    public delegate void PacketReceivedHandler(ServerSession session, ChannelType channel, ushort packetId, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Represents a connected client session with both TCP and UDP channels.
    /// Thread-safe for sending; receiving is single-threaded per session.
    /// </summary>
    public sealed class ServerSession : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════
        //                          IDENTITY
        // ═══════════════════════════════════════════════════════════════════

        private static uint _nextSessionId;

        /// <summary>Unique session identifier.</summary>
        public uint SessionId { get; }

        /// <summary>TCP endpoint.</summary>
        public IPEndPoint? TcpEndPoint { get; }

        /// <summary>UDP endpoint (set after UDP handshake).</summary>
        public IPEndPoint? UdpEndPoint { get; private set; }

        /// <summary>Token for UDP handshake association.</summary>
        public Guid UdpHandshakeToken { get; }

        // ═══════════════════════════════════════════════════════════════════
        //                          STATE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Current connection state.</summary>
        public SessionState State { get; private set; }

        /// <summary>Whether UDP channel is established.</summary>
        public bool IsUdpEstablished => UdpEndPoint != null;

        /// <summary>Time of last TCP activity.</summary>
        public DateTime LastTcpActivity { get; private set; }

        /// <summary>Time of last UDP activity.</summary>
        public DateTime LastUdpActivity { get; private set; }

        /// <summary>Session creation time.</summary>
        public DateTime ConnectedAt { get; }

        /// <summary>Custom user data (e.g., account, player reference).</summary>
        public object? UserData { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        //                         TCP STATE
        // ═══════════════════════════════════════════════════════════════════

        private Socket? _tcpSocket;
        private readonly ByteRingBuffer _tcpReceiveRing;
        private readonly byte[] _tcpReceiveBuffer;
        private readonly SocketAsyncEventArgs _tcpReceiveArgs;
        private readonly SocketAsyncEventArgs _tcpSendArgs;

        private readonly ConcurrentQueue<BufferSegment> _tcpSendQueue = new();
        private int _tcpSendPending;  // 0 or 1, used as lock-free flag

        // ═══════════════════════════════════════════════════════════════════
        //                         UDP STATE
        // ═══════════════════════════════════════════════════════════════════

        private int _udpSendSequence;  // Using int for Interlocked.Increment support
        private ushort _udpReceiveSequence;
        private uint _udpAckBits;

        // Reliable UDP pending packets waiting for ACK
        private readonly ConcurrentDictionary<ushort, PendingUdpPacket> _pendingAcks = new();

        private struct PendingUdpPacket
        {
            public byte[] Data;
            public DateTime SentAt;
            public int RetryCount;
            public ChannelType Channel;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                         METRICS
        // ═══════════════════════════════════════════════════════════════════

        private long _tcpBytesSent;
        private long _tcpBytesReceived;
        private long _udpBytesSent;
        private long _udpBytesReceived;
        private long _packetsSent;
        private long _packetsReceived;

        public long TcpBytesSent => Interlocked.Read(ref _tcpBytesSent);
        public long TcpBytesReceived => Interlocked.Read(ref _tcpBytesReceived);
        public long UdpBytesSent => Interlocked.Read(ref _udpBytesSent);
        public long UdpBytesReceived => Interlocked.Read(ref _udpBytesReceived);
        public long PacketsSent => Interlocked.Read(ref _packetsSent);
        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

        // ═══════════════════════════════════════════════════════════════════
        //                          EVENTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Called when a complete packet is received.</summary>
        public event PacketReceivedHandler? OnPacketReceived;

        /// <summary>Called when session is disconnected.</summary>
        public event Action<ServerSession, string>? OnDisconnected;

        // ═══════════════════════════════════════════════════════════════════
        //                        REFERENCES
        // ═══════════════════════════════════════════════════════════════════

        private readonly UdpServer _udpServer;

        // ═══════════════════════════════════════════════════════════════════
        //                        CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════

        public ServerSession(Socket tcpSocket, UdpServer udpServer)
        {
            SessionId = Interlocked.Increment(ref _nextSessionId);
            _tcpSocket = tcpSocket;
            _udpServer = udpServer;

            TcpEndPoint = tcpSocket.RemoteEndPoint as IPEndPoint;
            UdpHandshakeToken = Guid.NewGuid();
            State = SessionState.TcpConnected;
            ConnectedAt = DateTime.UtcNow;
            LastTcpActivity = DateTime.UtcNow;

            // Initialize TCP buffers
            _tcpReceiveRing = new ByteRingBuffer(NetworkConstants.TcpRingBufferSize);
            _tcpReceiveBuffer = new byte[NetworkConstants.TcpReceiveBufferSize];

            // Setup async event args
            _tcpReceiveArgs = new SocketAsyncEventArgs();
            _tcpReceiveArgs.SetBuffer(_tcpReceiveBuffer, 0, _tcpReceiveBuffer.Length);
            _tcpReceiveArgs.Completed += OnTcpIoCompleted;
            _tcpReceiveArgs.UserToken = this;

            _tcpSendArgs = new SocketAsyncEventArgs();
            _tcpSendArgs.Completed += OnTcpIoCompleted;
            _tcpSendArgs.UserToken = this;

            // Configure socket
            tcpSocket.NoDelay = true;
            tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     PUBLIC SEND API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send a packet using its declared channel type.
        /// Zero-boxing through generic constraints.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send<TPacket>(in TPacket packet) where TPacket : struct, IPacket
        {
            Console.WriteLine($"[Session {SessionId}] Sending {typeof(TPacket).Name} via {TPacket.Channel} (PacketId=0x{TPacket.PacketId:X4})");
            Send(TPacket.Channel, TPacket.PacketId, in packet);
        }

        /// <summary>
        /// Send a packet with explicit channel override.
        /// </summary>
        public void Send<TPacket>(ChannelType channel, in TPacket packet) where TPacket : struct, IPacket
        {
            Send(channel, TPacket.PacketId, in packet);
        }

        /// <summary>
        /// Send a packet with explicit channel and packet ID.
        /// </summary>
        private void Send<TPacket>(ChannelType channel, ushort packetId, in TPacket packet)
            where TPacket : struct, IPacket
        {
            // Rent buffer for serialization
            var buffer = BufferPool.Rent(NetworkConstants.DefaultPoolBufferSize);

            try
            {
                // Serialize packet to buffer (after header space)
                var config = ChannelConfig.Get(channel);
                int headerSize = config.Protocol == TransportProtocol.TCP
                    ? TcpPacketHeader.Size
                    : UdpPacketHeader.Size;

                var payloadSpan = buffer.AsSpan(headerSize);
                var writer = new BitWriter(payloadSpan);
                packet.Serialize(ref writer);
                int payloadLength = writer.BytesWritten;

                // Send via appropriate protocol
                if (config.Protocol == TransportProtocol.TCP)
                {
                    SendTcpRaw(channel, packetId, buffer, payloadLength);
                }
                else
                {
                    if (!IsUdpEstablished)
                    {
                        // Fallback to TCP if UDP not established
                        SendTcpRaw(ChannelType.ReliableOrdered, packetId, buffer, payloadLength);
                    }
                    else
                    {
                        SendUdpRaw(channel, packetId, buffer, payloadLength);
                    }
                }
            }
            catch
            {
                BufferPool.Return(buffer);
                throw;
            }
        }

        /// <summary>
        /// Send raw data over TCP.
        /// </summary>
        private void SendTcpRaw(ChannelType channel, ushort packetId, byte[] buffer, int payloadLength)
        {
            int totalLength = TcpPacketHeader.Size + payloadLength;
            var header = TcpPacketHeader.Create(packetId, channel, payloadLength);
            header.Write(buffer.AsSpan());

            // Queue for sending (buffer ownership transfers to queue)
            _tcpSendQueue.Enqueue(new BufferSegment(buffer, 0, totalLength));
            FlushTcpSendQueue();

            Interlocked.Increment(ref _packetsSent);
        }

        /// <summary>
        /// Send raw data over UDP.
        /// </summary>
        private void SendUdpRaw(ChannelType channel, ushort packetId, byte[] buffer, int payloadLength)
        {
            int totalLength = UdpPacketHeader.Size + payloadLength;
            ushort sequence = (ushort)Interlocked.Increment(ref _udpSendSequence);

            var header = new UdpPacketHeader(
                channel,
                sequence,
                _udpReceiveSequence,
                _udpAckBits,
                packetId
            );
            header.Write(buffer.AsSpan());

            var config = ChannelConfig.Get(channel);

            // Track for retry if reliable
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

            // Send via shared UDP server
            _udpServer.SendTo(buffer.AsSpan(0, totalLength), UdpEndPoint!);

            BufferPool.Return(buffer);
            Interlocked.Add(ref _udpBytesSent, totalLength);
            Interlocked.Increment(ref _packetsSent);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     TCP RECEIVE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Start receiving data on TCP socket.
        /// </summary>
        public void StartReceiving()
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
            catch (ObjectDisposedException)
            {
                Disconnect("Socket disposed");
            }
            catch (SocketException ex)
            {
                Disconnect($"Socket error: {ex.SocketErrorCode}");
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
                Disconnect(e.BytesTransferred == 0 ? "Connection closed" : $"Receive error: {e.SocketError}");
                return;
            }

            LastTcpActivity = DateTime.UtcNow;
            Interlocked.Add(ref _tcpBytesReceived, e.BytesTransferred);

            // Write received data to ring buffer
            _tcpReceiveRing.Write(new ReadOnlySpan<byte>(_tcpReceiveBuffer, 0, e.BytesTransferred));

            // Process complete packets
            ProcessTcpPackets();

            // Continue receiving
            StartReceiving();
        }

        private void ProcessTcpPackets()
        {
            while (_tcpReceiveRing.Count >= TcpPacketHeader.Size)
            {
                // Peek at header to get length
                if (!_tcpReceiveRing.TryPeekUInt16(0, out ushort length))
                    break;

                // Validate length
                if (length < TcpPacketHeader.Size || length > NetworkConstants.MaxTcpPacketLength)
                {
                    Disconnect($"Invalid packet length: {length}");
                    return;
                }

                // Wait for complete packet
                if (_tcpReceiveRing.Count < length)
                    break;

                // Read complete packet
                var packetBuffer = BufferPool.Rent(length);
                try
                {
                    _tcpReceiveRing.Read(packetBuffer.AsSpan(0, length));

                    var header = TcpPacketHeader.Read(packetBuffer);
                    if (!header.IsValid)
                    {
                        Disconnect("Invalid packet header");
                        return;
                    }

                    // Dispatch packet
                    var payload = packetBuffer.AsSpan(TcpPacketHeader.Size, header.PayloadLength);
                    Interlocked.Increment(ref _packetsReceived);

                    try
                    {
                        OnPacketReceived?.Invoke(this, header.Channel, header.PacketId, payload);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Session {SessionId}] Packet handler error: {ex.Message}");
                    }
                }
                finally
                {
                    BufferPool.Return(packetBuffer);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     TCP SEND
        // ═══════════════════════════════════════════════════════════════════

        private void FlushTcpSendQueue()
        {
            // Lock-free check if send already in progress
            if (Interlocked.CompareExchange(ref _tcpSendPending, 1, 0) != 0)
                return;

            if (_tcpSocket == null)
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
            catch (ObjectDisposedException)
            {
                ReturnSendBuffer(segment);
                Interlocked.Exchange(ref _tcpSendPending, 0);
                Disconnect("Socket disposed");
            }
            catch (SocketException ex)
            {
                ReturnSendBuffer(segment);
                Interlocked.Exchange(ref _tcpSendPending, 0);
                Disconnect($"Send error: {ex.SocketErrorCode}");
            }
        }

        private void ProcessTcpSend(SocketAsyncEventArgs e)
        {
            if (e.UserToken is BufferSegment segment)
            {
                ReturnSendBuffer(segment);
            }

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

        private static void ReturnSendBuffer(BufferSegment segment)
        {
            if (segment.Array != null)
            {
                BufferPool.Return(segment.Array);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     UDP HANDLING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by UdpServer when data arrives for this session.
        /// </summary>
        public void OnUdpReceive(ReadOnlySpan<byte> data, IPEndPoint from)
        {
            if (data.Length < UdpPacketHeader.Size)
                return;

            if (!UdpPacketHeader.TryRead(data, out var header))
                return;

            LastUdpActivity = DateTime.UtcNow;
            Interlocked.Add(ref _udpBytesReceived, data.Length);

            // Process ACK info from remote
            ProcessUdpAcks(header.Ack, header.AckBits);

            // Check sequencing for ordered channels
            var config = ChannelConfig.Get(header.Channel);
            if (config.RequiresOrdering)
            {
                if (!SequenceNumber.IsNewer(header.Sequence, _udpReceiveSequence))
                    return; // Old packet, discard
            }

            // Update our receive tracking
            UpdateUdpReceiveSequence(header.Sequence);

            // Dispatch packet
            var payload = data.Slice(UdpPacketHeader.Size);
            Interlocked.Increment(ref _packetsReceived);

            try
            {
                OnPacketReceived?.Invoke(this, header.Channel, header.PacketId, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {SessionId}] UDP packet handler error: {ex.Message}");
            }
        }

        private void ProcessUdpAcks(ushort ack, uint ackBits)
        {
            // Remote is acknowledging receipt of our packets
            _pendingAcks.TryRemove(ack, out _);

            // Check bitfield for older packets
            for (int i = 0; i < 32; i++)
            {
                if ((ackBits & (1u << i)) != 0)
                {
                    ushort seq = (ushort)(ack - 1 - i);
                    _pendingAcks.TryRemove(seq, out _);
                }
            }
        }

        private void UpdateUdpReceiveSequence(ushort sequence)
        {
            if (SequenceNumber.IsNewer(sequence, _udpReceiveSequence))
            {
                int diff = SequenceNumber.Difference(sequence, _udpReceiveSequence);
                if (diff <= 32)
                {
                    _udpAckBits <<= diff;
                    _udpAckBits |= 1u << (diff - 1);
                }
                else
                {
                    _udpAckBits = 0;
                }
                _udpReceiveSequence = sequence;
            }
            else
            {
                int diff = SequenceNumber.Difference(_udpReceiveSequence, sequence);
                if (diff <= 32)
                {
                    _udpAckBits |= 1u << (diff - 1);
                }
            }
        }

        /// <summary>
        /// Process reliable UDP retries. Call periodically from game loop.
        /// </summary>
        public void ProcessUdpRetries()
        {
            if (!IsUdpEstablished || _pendingAcks.IsEmpty)
                return;

            var now = DateTime.UtcNow;
            var toRetry = new List<ushort>();
            var toRemove = new List<ushort>();

            foreach (var kvp in _pendingAcks)
            {
                var config = ChannelConfig.Get(kvp.Value.Channel);
                var elapsed = now - kvp.Value.SentAt;

                if (elapsed.TotalMilliseconds > config.RetryIntervalMs)
                {
                    if (kvp.Value.RetryCount >= config.MaxRetries)
                    {
                        toRemove.Add(kvp.Key);
                    }
                    else
                    {
                        toRetry.Add(kvp.Key);
                    }
                }
            }

            foreach (var seq in toRemove)
            {
                _pendingAcks.TryRemove(seq, out _);
                // Could escalate to TCP or disconnect here
            }

            foreach (var seq in toRetry)
            {
                if (_pendingAcks.TryGetValue(seq, out var pending))
                {
                    // Update ACK fields before resend
                    var headerSpan = pending.Data.AsSpan(0, UdpPacketHeader.Size);
                    var header = UdpPacketHeader.Read(headerSpan);
                    var newHeader = header.WithAck(_udpReceiveSequence, _udpAckBits);
                    newHeader.Write(headerSpan);

                    // Update tracking
                    pending.SentAt = now;
                    pending.RetryCount++;
                    _pendingAcks[seq] = pending;

                    // Resend
                    _udpServer.SendTo(pending.Data, UdpEndPoint!);
                    Interlocked.Add(ref _udpBytesSent, pending.Data.Length);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     UDP ASSOCIATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Associate UDP endpoint after client sends handshake token.
        /// </summary>
        public bool AssociateUdp(IPEndPoint endpoint, Guid token)
        {
            if (token != UdpHandshakeToken)
                return false;

            if (UdpEndPoint != null)
                return false; // Already associated

            UdpEndPoint = endpoint;
            State = SessionState.FullyConnected;
            LastUdpActivity = DateTime.UtcNow;

            Console.WriteLine($"[Session {SessionId}] UDP associated: {endpoint}");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                     LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Disconnect the session.
        /// </summary>
        public void Disconnect(string reason)
        {
            if (State == SessionState.Disconnected)
                return;

            State = SessionState.Disconnected;

            Console.WriteLine($"[Session {SessionId}] Disconnected: {reason}");

            try
            {
                _tcpSocket?.Shutdown(SocketShutdown.Both);
            }
            catch { }

            _tcpSocket?.Close();
            _tcpSocket = null;

            // Clear pending sends
            while (_tcpSendQueue.TryDequeue(out var segment))
            {
                BufferPool.Return(segment.Array!);
            }

            _pendingAcks.Clear();

            OnDisconnected?.Invoke(this, reason);
        }

        public void Dispose()
        {
            Disconnect("Disposed");

            _tcpReceiveArgs.Dispose();
            _tcpSendArgs.Dispose();
        }
    }

    /// <summary>
    /// Session connection state.
    /// </summary>
    public enum SessionState
    {
        /// <summary>TCP connected, waiting for UDP handshake.</summary>
        TcpConnected,

        /// <summary>Both TCP and UDP channels active.</summary>
        FullyConnected,

        /// <summary>Session disconnected.</summary>
        Disconnected
    }
}
