// Shared/Network/Packets/SystemPackets.cs
// System-level packets for connection management

namespace Network.Shared.Network.Packets
{
    // ═══════════════════════════════════════════════════════════════════════
    //                           HANDSHAKE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client → Server: Initial connection request.
    /// Sent immediately after TCP connection established.
    /// </summary>
    [EstimatedSize(32)]
    public readonly struct ClientHello : IPacket<ClientHello>, IClientPacket
    {
        public static ushort PacketId => 0x0001;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        /// <summary>
        /// Protocol version for compatibility checking.
        /// </summary>
        public readonly ushort ProtocolVersion;

        /// <summary>
        /// Client version string (e.g., "1.0.0").
        /// </summary>
        public readonly string ClientVersion;

        public ClientHello(ushort protocolVersion, string clientVersion)
        {
            ProtocolVersion = protocolVersion;
            ClientVersion = clientVersion ?? string.Empty;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt16(ProtocolVersion);
            writer.WriteString(ClientVersion);
        }

        public static ClientHello Deserialize(ref BitReader reader)
        {
            return new ClientHello(
                reader.ReadUInt16(),
                reader.ReadString()
            );
        }
    }

    /// <summary>
    /// Server → Client: Connection accepted response.
    /// Contains session info and UDP handshake token.
    /// </summary>
    [EstimatedSize(48)]
    public readonly struct ServerHello : IPacket<ServerHello>, IServerPacket
    {
        public static ushort PacketId => 0x0002;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        /// <summary>
        /// Assigned session ID.
        /// </summary>
        public readonly uint SessionId;

        /// <summary>
        /// Token for UDP handshake (send this via UDP to associate).
        /// </summary>
        public readonly Guid UdpToken;

        /// <summary>
        /// Server's current timestamp for time sync.
        /// </summary>
        public readonly long ServerTime;

        /// <summary>
        /// Whether UDP is enabled on this server.
        /// </summary>
        public readonly bool UdpEnabled;

        /// <summary>
        /// UDP port if different from default.
        /// </summary>
        public readonly ushort UdpPort;

        public ServerHello(uint sessionId, Guid udpToken, long serverTime, bool udpEnabled, ushort udpPort)
        {
            SessionId = sessionId;
            UdpToken = udpToken;
            ServerTime = serverTime;
            UdpEnabled = udpEnabled;
            UdpPort = udpPort;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(SessionId);
            writer.WriteGuid(UdpToken);
            writer.WriteInt64(ServerTime);
            writer.WriteBool(UdpEnabled);
            writer.WriteUInt16(UdpPort);
        }

        public static ServerHello Deserialize(ref BitReader reader)
        {
            return new ServerHello(
                reader.ReadUInt32(),
                reader.ReadGuid(),
                reader.ReadInt64(),
                reader.ReadBool(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: Connection rejected.
    /// </summary>
    [EstimatedSize(128)]
    public readonly struct ConnectionRejected : IPacket<ConnectionRejected>, IServerPacket
    {
        public static ushort PacketId => 0x0003;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly RejectReason Reason;
        public readonly string Message;

        public ConnectionRejected(RejectReason reason, string message)
        {
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte((byte)Reason);
            writer.WriteString(Message);
        }

        public static ConnectionRejected Deserialize(ref BitReader reader)
        {
            return new ConnectionRejected(
                (RejectReason)reader.ReadByte(),
                reader.ReadString()
            );
        }
    }

    public enum RejectReason : byte
    {
        None = 0,
        ServerFull = 1,
        VersionMismatch = 2,
        Banned = 3,
        Maintenance = 4,
        InvalidCredentials = 5,
        AlreadyConnected = 6,
        RateLimited = 7
    }

    [EstimatedSize(64)]
    public readonly struct LoginRequest : IPacket<LoginRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0007;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly string Username;
        public readonly string Password;

        public LoginRequest(string username, string password)
        {
            Username = username ?? string.Empty;
            Password = password ?? string.Empty;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteString(Username);
            writer.WriteString(Password);
        }

        public static LoginRequest Deserialize(ref BitReader reader)
        {
            return new LoginRequest(
                reader.ReadString(),
                reader.ReadString()
            );
        }
    }

    [EstimatedSize(48)]
    public readonly struct LoginResponse : IPacket<LoginResponse>, IServerPacket
    {
        public static ushort PacketId => 0x0008;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly bool Success;
        public readonly string ErrorMessage;
        public readonly Guid SessionToken;
        public readonly uint PlayerId;

        public LoginResponse(bool success, string errorMessage, Guid sessionToken, uint playerId)
        {
            Success = success;
            ErrorMessage = errorMessage ?? string.Empty;
            SessionToken = sessionToken;
            PlayerId = playerId;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteBit(Success);
            writer.AlignToByte();
            writer.WriteString(ErrorMessage);
            writer.WriteGuid(SessionToken);
            writer.WriteUInt32(PlayerId);
        }

        public static LoginResponse Deserialize(ref BitReader reader)
        {
            var success = reader.ReadBit();
            reader.AlignToByte();
            var error = reader.ReadString();
            var token = reader.ReadGuid();
            var playerId = reader.ReadUInt32();
            return new LoginResponse(success, error, token, playerId);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                         KEEP ALIVE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bidirectional: Ping packet for connection health.
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct Ping : IPacket<Ping>, IBidirectionalPacket
    {
        public static ushort PacketId => 0x0010;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        /// <summary>
        /// Sequence number to match with Pong.
        /// </summary>
        public readonly uint Sequence;

        /// <summary>
        /// Sender's local timestamp.
        /// </summary>
        public readonly long Timestamp;

        public Ping(uint sequence, long timestamp)
        {
            Sequence = sequence;
            Timestamp = timestamp;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Sequence);
            writer.WriteInt64(Timestamp);
        }

        public static Ping Deserialize(ref BitReader reader)
        {
            return new Ping(
                reader.ReadUInt32(),
                reader.ReadInt64()
            );
        }
    }

    /// <summary>
    /// Bidirectional: Pong response to Ping.
    /// </summary>
    [EstimatedSize(20)]
    public readonly struct Pong : IPacket<Pong>, IBidirectionalPacket
    {
        public static ushort PacketId => 0x0011;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        /// <summary>
        /// Sequence from original Ping.
        /// </summary>
        public readonly uint Sequence;

        /// <summary>
        /// Original timestamp from Ping.
        /// </summary>
        public readonly long OriginalTimestamp;

        /// <summary>
        /// Responder's local timestamp.
        /// </summary>
        public readonly long ResponseTimestamp;

        public Pong(uint sequence, long originalTimestamp, long responseTimestamp)
        {
            Sequence = sequence;
            OriginalTimestamp = originalTimestamp;
            ResponseTimestamp = responseTimestamp;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Sequence);
            writer.WriteInt64(OriginalTimestamp);
            writer.WriteInt64(ResponseTimestamp);
        }

        public static Pong Deserialize(ref BitReader reader)
        {
            return new Pong(
                reader.ReadUInt32(),
                reader.ReadInt64(),
                reader.ReadInt64()
            );
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                         DISCONNECT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bidirectional: Graceful disconnect notification.
    /// </summary>
    [EstimatedSize(64)]
    public readonly struct Disconnect : IPacket<Disconnect>, IBidirectionalPacket
    {
        public static ushort PacketId => 0x0020;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly DisconnectReason Reason;
        public readonly string Message;

        public Disconnect(DisconnectReason reason, string? message = null)
        {
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte((byte)Reason);
            writer.WriteString(Message);
        }

        public static Disconnect Deserialize(ref BitReader reader)
        {
            return new Disconnect(
                (DisconnectReason)reader.ReadByte(),
                reader.ReadString()
            );
        }
    }

    public enum DisconnectReason : byte
    {
        None = 0,
        ClientQuit = 1,
        ServerShutdown = 2,
        Kicked = 3,
        Timeout = 4,
        ProtocolError = 5,
        Banned = 6,
        Duplicate = 7
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                       UDP HANDSHAKE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client → Server: UDP channel association request.
    /// Sent via UDP to establish the UDP endpoint mapping.
    /// </summary>
    [EstimatedSize(17)]
    public readonly struct UdpHandshake : IPacket<UdpHandshake>, IClientPacket
    {
        public static ushort PacketId => 0x0030;
        public static ChannelType Channel => ChannelType.Unreliable;

        /// <summary>
        /// Token from ServerHello.
        /// </summary>
        public readonly Guid Token;

        public UdpHandshake(Guid token)
        {
            Token = token;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteGuid(Token);
        }

        public static UdpHandshake Deserialize(ref BitReader reader)
        {
            return new UdpHandshake(reader.ReadGuid());
        }
    }

    /// <summary>
    /// Server → Client: UDP channel established confirmation.
    /// </summary>
    [EstimatedSize(4)]
    public readonly struct UdpHandshakeAck : IPacket<UdpHandshakeAck>, IServerPacket
    {
        public static ushort PacketId => 0x0031;
        public static ChannelType Channel => ChannelType.Unreliable;

        public readonly bool Success;

        public UdpHandshakeAck(bool success)
        {
            Success = success;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteBool(Success);
        }

        public static UdpHandshakeAck Deserialize(ref BitReader reader)
        {
            return new UdpHandshakeAck(reader.ReadBool());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                         TIME SYNC
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server → Client: Server time broadcast for synchronization.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct TimeSync : IPacket<TimeSync>, IServerPacket
    {
        public static ushort PacketId => 0x0040;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        /// <summary>
        /// Server's current tick/frame number.
        /// </summary>
        public readonly uint ServerTick;

        /// <summary>
        /// Server's current time in milliseconds.
        /// </summary>
        public readonly long ServerTimeMs;

        public TimeSync(uint serverTick, long serverTimeMs)
        {
            ServerTick = serverTick;
            ServerTimeMs = serverTimeMs;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(ServerTick);
            writer.WriteInt64(ServerTimeMs);
        }

        public static TimeSync Deserialize(ref BitReader reader)
        {
            return new TimeSync(
                reader.ReadUInt32(),
                reader.ReadInt64()
            );
        }
    }
}
