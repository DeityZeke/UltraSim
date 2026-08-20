// Shared/Network/Protocol/PacketHeader.cs
// Wire format packet headers for TCP and UDP protocols

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Network.Shared.Network
{
    /// <summary>
    /// TCP packet header layout:
    /// [Length:2][Channel:1][PacketId:2] = 5 bytes total
    /// 
    /// Length includes the header itself.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct TcpPacketHeader
    {
        public const int Size = NetworkConstants.TcpHeaderSize;

        /// <summary>
        /// Total packet length including header (2 bytes, little-endian).
        /// </summary>
        public readonly ushort Length;

        /// <summary>
        /// Channel type (1 byte).
        /// </summary>
        public readonly ChannelType Channel;

        /// <summary>
        /// Packet identifier (2 bytes, little-endian).
        /// </summary>
        public readonly ushort PacketId;

        /// <summary>
        /// Payload length (total length minus header).
        /// </summary>
        public int PayloadLength => Length - Size;

        public TcpPacketHeader(ushort length, ChannelType channel, ushort packetId)
        {
            Length = length;
            Channel = channel;
            PacketId = packetId;
        }

        /// <summary>
        /// Create header for a packet with the given payload size.
        /// </summary>
        public static TcpPacketHeader Create(ushort packetId, ChannelType channel, int payloadLength)
        {
            return new TcpPacketHeader(
                (ushort)(Size + payloadLength),
                channel,
                packetId
            );
        }

        /// <summary>
        /// Read header from buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TcpPacketHeader Read(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"Buffer too small for TCP header: {buffer.Length} < {Size}");

            return new TcpPacketHeader(
                BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                (ChannelType)buffer[2],
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(3))
            );
        }

        /// <summary>
        /// Try to read header from buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(ReadOnlySpan<byte> buffer, out TcpPacketHeader header)
        {
            if (buffer.Length < Size)
            {
                header = default;
                return false;
            }

            header = new TcpPacketHeader(
                BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                (ChannelType)buffer[2],
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(3))
            );
            return true;
        }

        /// <summary>
        /// Write header to buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"Buffer too small for TCP header: {buffer.Length} < {Size}");

            BinaryPrimitives.WriteUInt16LittleEndian(buffer, Length);
            buffer[2] = (byte)Channel;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(3), PacketId);
        }

        /// <summary>
        /// Validate header values.
        /// </summary>
        public bool IsValid =>
            Length >= Size &&
            Length <= NetworkConstants.MaxTcpPacketLength &&
            (int)Channel <= 3;

        public override string ToString() =>
            $"TCP[Len={Length}, Ch={Channel}, Id=0x{PacketId:X4}]";
    }

    /// <summary>
    /// UDP packet header layout:
    /// [Channel:1][Sequence:2][Ack:2][AckBits:4][PacketId:2] = 11 bytes total
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct UdpPacketHeader
    {
        public const int Size = NetworkConstants.UdpHeaderSize;

        /// <summary>
        /// Channel type (1 byte).
        /// </summary>
        public readonly ChannelType Channel;

        /// <summary>
        /// Sequence number for this packet (2 bytes, little-endian).
        /// </summary>
        public readonly ushort Sequence;

        /// <summary>
        /// Most recently received sequence from remote (2 bytes, little-endian).
        /// </summary>
        public readonly ushort Ack;

        /// <summary>
        /// Bitfield of received packets before Ack (4 bytes, little-endian).
        /// Bit N = packet (Ack - 1 - N) was received.
        /// </summary>
        public readonly uint AckBits;

        /// <summary>
        /// Packet identifier (2 bytes, little-endian).
        /// </summary>
        public readonly ushort PacketId;

        public UdpPacketHeader(ChannelType channel, ushort sequence, ushort ack, uint ackBits, ushort packetId)
        {
            Channel = channel;
            Sequence = sequence;
            Ack = ack;
            AckBits = ackBits;
            PacketId = packetId;
        }

        /// <summary>
        /// Read header from buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UdpPacketHeader Read(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"Buffer too small for UDP header: {buffer.Length} < {Size}");

            return new UdpPacketHeader(
                (ChannelType)buffer[0],
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(1)),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(3)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(5)),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(9))
            );
        }

        /// <summary>
        /// Try to read header from buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(ReadOnlySpan<byte> buffer, out UdpPacketHeader header)
        {
            if (buffer.Length < Size)
            {
                header = default;
                return false;
            }

            header = Read(buffer);
            return true;
        }

        /// <summary>
        /// Write header to buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"Buffer too small for UDP header: {buffer.Length} < {Size}");

            buffer[0] = (byte)Channel;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(1), Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(3), Ack);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(5), AckBits);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(9), PacketId);
        }

        /// <summary>
        /// Create a copy with updated ACK fields.
        /// </summary>
        public UdpPacketHeader WithAck(ushort ack, uint ackBits)
        {
            return new UdpPacketHeader(Channel, Sequence, ack, ackBits, PacketId);
        }

        /// <summary>
        /// Validate header values.
        /// </summary>
        public bool IsValid => (int)Channel <= 3;

        public override string ToString() =>
            $"UDP[Ch={Channel}, Seq={Sequence}, Ack={Ack}, Id=0x{PacketId:X4}]";
    }

    /// <summary>
    /// Utility methods for sequence number operations.
    /// Handles 16-bit wraparound correctly.
    /// </summary>
    public static class SequenceNumber
    {
        /// <summary>
        /// Check if sequence s1 is more recent than s2.
        /// Handles wraparound correctly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNewer(ushort s1, ushort s2)
        {
            return ((s1 > s2) && (s1 - s2 <= NetworkConstants.SequenceHalf)) ||
                   ((s1 < s2) && (s2 - s1 > NetworkConstants.SequenceHalf));
        }

        /// <summary>
        /// Check if sequence s1 is older than or equal to s2.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOlderOrEqual(ushort s1, ushort s2)
        {
            return !IsNewer(s1, s2);
        }

        /// <summary>
        /// Get the difference between two sequence numbers.
        /// Result is always positive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Difference(ushort s1, ushort s2)
        {
            if (s1 >= s2)
                return s1 - s2;
            return 65536 - s2 + s1;
        }

        /// <summary>
        /// Increment sequence number with wraparound.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Increment(ushort sequence)
        {
            return (ushort)((sequence + 1) & 0xFFFF);
        }
    }
}
