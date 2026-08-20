// Shared/Network/Core/NetworkConstants.cs
// Shared network configuration constants

namespace Network.Shared.Network
{
    /// <summary>
    /// Network configuration constants shared between client and server.
    /// </summary>
    public static class NetworkConstants
    {
        // ═══════════════════════════════════════════════════════════════════
        //                           PORTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Default TCP port for reliable communication.
        /// </summary>
        public const int DefaultTcpPort = 2593;

        /// <summary>
        /// Default UDP port for fast communication.
        /// </summary>
        public const int DefaultUdpPort = 2594;

        // ═══════════════════════════════════════════════════════════════════
        //                         BUFFER SIZES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Size of TCP receive buffer per session.
        /// </summary>
        public const int TcpReceiveBufferSize = 8192;

        /// <summary>
        /// Size of TCP send buffer per session.
        /// </summary>
        public const int TcpSendBufferSize = 16384;

        /// <summary>
        /// Size of the ring buffer for TCP receive data.
        /// </summary>
        public const int TcpRingBufferSize = 16384;

        /// <summary>
        /// Maximum size of a single UDP packet (MTU safe).
        /// </summary>
        public const int MaxUdpPacketSize = 1400;

        /// <summary>
        /// Size of shared UDP receive buffer.
        /// </summary>
        public const int UdpReceiveBufferSize = 2048;

        /// <summary>
        /// Default buffer pool bucket size.
        /// </summary>
        public const int DefaultPoolBufferSize = 512;

        // ═══════════════════════════════════════════════════════════════════
        //                         PACKET LIMITS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Minimum valid TCP packet length (header only).
        /// </summary>
        public const int MinTcpPacketLength = TcpHeaderSize;

        /// <summary>
        /// Maximum valid TCP packet length.
        /// </summary>
        public const int MaxTcpPacketLength = 65535;

        /// <summary>
        /// Minimum valid UDP packet length (header only).
        /// </summary>
        public const int MinUdpPacketLength = UdpHeaderSize;

        // ═══════════════════════════════════════════════════════════════════
        //                         HEADER SIZES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// TCP packet header: [Length:2][Channel:1][PacketId:2] = 5 bytes
        /// </summary>
        public const int TcpHeaderSize = 5;

        /// <summary>
        /// UDP packet header: [Channel:1][Seq:2][Ack:2][AckBits:4][PacketId:2] = 11 bytes
        /// </summary>
        public const int UdpHeaderSize = 11;

        /// <summary>
        /// UDP handshake packet size: [Marker:1][Token:16] = 17 bytes
        /// </summary>
        public const int UdpHandshakeSize = 17;

        /// <summary>
        /// UDP handshake marker byte.
        /// </summary>
        public const byte UdpHandshakeMarker = 0xFF;

        /// <summary>
        /// UDP handshake ACK marker.
        /// </summary>
        public const byte UdpHandshakeAck = 0x01;

        // ═══════════════════════════════════════════════════════════════════
        //                         TIMEOUTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// TCP connection timeout in milliseconds.
        /// </summary>
        public const int TcpConnectTimeoutMs = 10000;

        /// <summary>
        /// UDP handshake timeout in milliseconds.
        /// </summary>
        public const int UdpHandshakeTimeoutMs = 5000;

        /// <summary>
        /// Time before considering a connection dead (no activity).
        /// </summary>
        public const int ConnectionTimeoutMs = 30000;

        /// <summary>
        /// Interval between keepalive/ping packets.
        /// </summary>
        public const int KeepAliveIntervalMs = 5000;

        /// <summary>
        /// Default retry interval for reliable UDP packets.
        /// </summary>
        public const int UdpRetryIntervalMs = 100;

        /// <summary>
        /// Maximum retries for reliable UDP packets before giving up.
        /// </summary>
        public const int UdpMaxRetries = 10;

        // ═══════════════════════════════════════════════════════════════════
        //                       SEQUENCE NUMBERS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Window size for sequenced packet ordering.
        /// Packets older than this many sequences are dropped.
        /// </summary>
        public const int SequenceWindow = 32;

        /// <summary>
        /// Half of ushort.MaxValue, used for sequence number wraparound comparison.
        /// </summary>
        public const int SequenceHalf = 32768;

        // ═══════════════════════════════════════════════════════════════════
        //                       SERVER LIMITS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maximum concurrent connections on the server.
        /// </summary>
        public const int MaxConnections = 5000;

        /// <summary>
        /// Maximum packets to process per session per tick.
        /// </summary>
        public const int MaxPacketsPerTick = 100;

        /// <summary>
        /// Accept queue backlog for TCP listener.
        /// </summary>
        public const int TcpBacklog = 128;

        // ═══════════════════════════════════════════════════════════════════
        //                       PACKET ID RANGES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// System packets (handshake, keepalive, disconnect): 0x0000 - 0x00FF
        /// </summary>
        public const ushort SystemPacketRangeStart = 0x0000;
        public const ushort SystemPacketRangeEnd = 0x00FF;

        /// <summary>
        /// Movement packets: 0x0100 - 0x01FF
        /// </summary>
        public const ushort MovementPacketRangeStart = 0x0100;
        public const ushort MovementPacketRangeEnd = 0x01FF;

        /// <summary>
        /// Combat packets: 0x0200 - 0x02FF
        /// </summary>
        public const ushort CombatPacketRangeStart = 0x0200;
        public const ushort CombatPacketRangeEnd = 0x02FF;

        /// <summary>
        /// Chat packets: 0x0300 - 0x03FF
        /// </summary>
        public const ushort ChatPacketRangeStart = 0x0300;
        public const ushort ChatPacketRangeEnd = 0x03FF;

        /// <summary>
        /// Inventory packets: 0x0400 - 0x04FF
        /// </summary>
        public const ushort InventoryPacketRangeStart = 0x0400;
        public const ushort InventoryPacketRangeEnd = 0x04FF;

        /// <summary>
        /// Entity packets: 0x0500 - 0x05FF
        /// </summary>
        public const ushort EntityPacketRangeStart = 0x0500;
        public const ushort EntityPacketRangeEnd = 0x05FF;

        /// <summary>
        /// World/Map packets: 0x0600 - 0x06FF
        /// </summary>
        public const ushort WorldPacketRangeStart = 0x0600;
        public const ushort WorldPacketRangeEnd = 0x06FF;

        /// <summary>
        /// Skill packets: 0x0700 - 0x07FF
        /// </summary>
        public const ushort SkillPacketRangeStart = 0x0700;
        public const ushort SkillPacketRangeEnd = 0x07FF;

        /// <summary>
        /// Housing packets: 0x0800 - 0x08FF
        /// </summary>
        public const ushort HousingPacketRangeStart = 0x0800;
        public const ushort HousingPacketRangeEnd = 0x08FF;

        /// <summary>
        /// Custom/Script packets: 0x8000 - 0xFFFF
        /// </summary>
        public const ushort CustomPacketRangeStart = 0x8000;
        public const ushort CustomPacketRangeEnd = 0xFFFF;
    }
}
