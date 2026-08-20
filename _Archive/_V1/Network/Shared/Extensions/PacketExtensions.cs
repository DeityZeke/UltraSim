// Shared/Network/Extensions/PacketExtensions.cs
// Extension methods for common packet operations

using System.Runtime.CompilerServices;

namespace Network.Shared.Network.Extensions
{
    /// <summary>
    /// Delegate for writing an element to a BitWriter.
    /// </summary>
    public delegate void WriteElementDelegate<T>(ref BitWriter writer, T element);

    /// <summary>
    /// Delegate for reading an element from a BitReader.
    /// </summary>
    public delegate T ReadElementDelegate<T>(ref BitReader reader);

    /// <summary>
    /// Extension methods for BitWriter to simplify common patterns.
    /// </summary>
    public static class BitWriterExtensions
    {
        /// <summary>
        /// Write a position as three values (X, Y, Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePosition(this ref BitWriter writer, ushort x, ushort y, sbyte z)
        {
            writer.WriteUInt16(x);
            writer.WriteUInt16(y);
            writer.WriteSByte(z);
        }

        /// <summary>
        /// Write a 3D position as floats.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePosition3D(this ref BitWriter writer, float x, float y, float z)
        {
            writer.WriteFloat(x);
            writer.WriteFloat(y);
            writer.WriteFloat(z);
        }

        /// <summary>
        /// Write a color as RGB bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteColor(this ref BitWriter writer, byte r, byte g, byte b)
        {
            writer.WriteByte(r);
            writer.WriteByte(g);
            writer.WriteByte(b);
        }

        /// <summary>
        /// Write a color as RGBA bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteColorAlpha(this ref BitWriter writer, byte r, byte g, byte b, byte a)
        {
            writer.WriteByte(r);
            writer.WriteByte(g);
            writer.WriteByte(b);
            writer.WriteByte(a);
        }

        /// <summary>
        /// Write a normalized direction (0-1) packed into specified bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteNormalizedAngle(this ref BitWriter writer, float angle, int bits = 8)
        {
            // Normalize to 0-1 range (assuming input is 0-360 or 0-2π)
            float normalized = angle / 360f;
            if (normalized < 0) normalized += 1f;
            if (normalized >= 1) normalized -= 1f;

            uint maxVal = (1u << bits) - 1;
            uint quantized = (uint)(normalized * maxVal);
            writer.WriteBits(quantized, bits);
        }

        /// <summary>
        /// Write a nullable value with a presence bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteNullable(this ref BitWriter writer, uint? value)
        {
            writer.WriteBit(value.HasValue);
            if (value.HasValue)
                writer.WriteUInt32(value.Value);
        }

        /// <summary>
        /// Write an array with length prefix.
        /// </summary>
        public static void WriteArray<T>(this ref BitWriter writer, T[] array, WriteElementDelegate<T> writeElement)
        {
            if (array == null || array.Length == 0)
            {
                writer.WriteUInt16(0);
                return;
            }

            writer.WriteUInt16((ushort)array.Length);
            foreach (var item in array)
            {
                writeElement(ref writer, item);
            }
        }
    }

    /// <summary>
    /// Extension methods for BitReader to simplify common patterns.
    /// </summary>
    public static class BitReaderExtensions
    {
        /// <summary>
        /// Read a position as three values (X, Y, Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ushort X, ushort Y, sbyte Z) ReadPosition(this ref BitReader reader)
        {
            return (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadSByte());
        }

        /// <summary>
        /// Read a 3D position as floats.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (float X, float Y, float Z) ReadPosition3D(this ref BitReader reader)
        {
            return (reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        }

        /// <summary>
        /// Read a color as RGB bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (byte R, byte G, byte B) ReadColor(this ref BitReader reader)
        {
            return (reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        /// <summary>
        /// Read a color as RGBA bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (byte R, byte G, byte B, byte A) ReadColorAlpha(this ref BitReader reader)
        {
            return (reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        /// <summary>
        /// Read a normalized angle packed into specified bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadNormalizedAngle(this ref BitReader reader, int bits = 8)
        {
            uint quantized = reader.ReadBits(bits);
            uint maxVal = (1u << bits) - 1;
            return (quantized / (float)maxVal) * 360f;
        }

        /// <summary>
        /// Read a nullable value with a presence bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint? ReadNullableUInt32(this ref BitReader reader)
        {
            if (reader.ReadBit())
                return reader.ReadUInt32();
            return null;
        }

        /// <summary>
        /// Read an array with length prefix.
        /// </summary>
        public static T[] ReadArray<T>(this ref BitReader reader, ReadElementDelegate<T> readElement)
        {
            ushort length = reader.ReadUInt16();
            if (length == 0)
                return Array.Empty<T>();

            var array = new T[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = readElement(ref reader);
            }
            return array;
        }
    }

    /// <summary>
    /// Utility methods for packet operations.
    /// </summary>
    public static class PacketUtils
    {
        /// <summary>
        /// Get the wire size of a packet (including header).
        /// </summary>
        public static int GetWireSize<TPacket>(in TPacket packet, TransportProtocol protocol)
            where TPacket : struct, IPacket
        {
            // Serialize to measure
            var buffer = BufferPool.Rent(1024);
            try
            {
                var writer = new BitWriter(buffer);
                packet.Serialize(ref writer);
                int payloadSize = writer.BytesWritten;

                int headerSize = protocol == TransportProtocol.TCP
                    ? TcpPacketHeader.Size
                    : UdpPacketHeader.Size;

                return headerSize + payloadSize;
            }
            finally
            {
                BufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Create a debug string representation of a packet.
        /// </summary>
        public static string ToDebugString<TPacket>(in TPacket packet) where TPacket : struct, IPacket
        {
            return $"{typeof(TPacket).Name} [0x{TPacket.PacketId:X4}] ({TPacket.Channel})";
        }

        /// <summary>
        /// Validate packet fits within MTU for UDP.
        /// </summary>
        public static bool FitsInUdpMtu<TPacket>(in TPacket packet) where TPacket : struct, IPacket
        {
            int wireSize = GetWireSize(packet, TransportProtocol.UDP);
            return wireSize <= NetworkConstants.MaxUdpPacketSize;
        }
    }
}
