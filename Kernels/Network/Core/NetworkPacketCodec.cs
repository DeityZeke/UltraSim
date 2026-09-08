using System.Buffers.Binary;
using System.Text;

namespace UltraSim.Network;

/// <summary>Transactional canonical big-endian writer over caller-owned packet memory.</summary>
public ref struct NetworkPacketWriter
{
    private Span<byte> _destination;
    private int _position;

    public NetworkPacketWriter(Span<byte> destination)
    {
        _destination = destination;
        _position = 0;
    }

    public readonly int WrittenCount => _position;
    public readonly int Remaining => _destination.Length - _position;
    public readonly ReadOnlySpan<byte> WrittenSpan => _destination[.._position];

    public bool TryWriteByte(byte value)
    {
        if (Remaining < 1) return false;
        _destination[_position++] = value;
        return true;
    }

    public bool TryWriteBoolean(bool value) => TryWriteByte(value ? (byte)1 : (byte)0);

    public bool TryWriteUInt16(ushort value)
    {
        if (Remaining < sizeof(ushort)) return false;
        BinaryPrimitives.WriteUInt16BigEndian(_destination[_position..], value);
        _position += sizeof(ushort);
        return true;
    }

    public bool TryWriteInt16(short value) => TryWriteUInt16(unchecked((ushort)value));

    public bool TryWriteUInt32(uint value)
    {
        if (Remaining < sizeof(uint)) return false;
        BinaryPrimitives.WriteUInt32BigEndian(_destination[_position..], value);
        _position += sizeof(uint);
        return true;
    }

    public bool TryWriteInt32(int value) => TryWriteUInt32(unchecked((uint)value));

    public bool TryWriteUInt64(ulong value)
    {
        if (Remaining < sizeof(ulong)) return false;
        BinaryPrimitives.WriteUInt64BigEndian(_destination[_position..], value);
        _position += sizeof(ulong);
        return true;
    }

    public bool TryWriteInt64(long value) => TryWriteUInt64(unchecked((ulong)value));

    public bool TryWriteVarUInt64(ulong value)
    {
        int required = NetworkCanonicalEncoding.MeasureVarUInt64(value);
        if (Remaining < required) return false;
        while (value >= 0x80)
        {
            _destination[_position++] = unchecked((byte)(value | 0x80));
            value >>= 7;
        }
        _destination[_position++] = unchecked((byte)value);
        return true;
    }

    public bool TryWriteBytes(scoped ReadOnlySpan<byte> value)
    {
        if (value.Length > Remaining) return false;
        value.CopyTo(_destination[_position..]);
        _position += value.Length;
        return true;
    }

    public bool TryWriteBoundedUtf8(string value, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        int byteCount;
        try { byteCount = NetworkCanonicalEncoding.StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { return false; }
        if (byteCount > maximumBytes) return false;
        int lengthBytes = NetworkCanonicalEncoding.MeasureVarUInt64((ulong)byteCount);
        if (Remaining < lengthBytes + byteCount) return false;
        NetworkPacketWriter staged = this;
        if (!staged.TryWriteVarUInt64((ulong)byteCount)) return false;
        try
        {
            int written = NetworkCanonicalEncoding.StrictUtf8.GetBytes(value, staged._destination[staged._position..]);
            staged._position += written;
        }
        catch (EncoderFallbackException) { return false; }
        this = staged;
        return true;
    }
}

/// <summary>Transactional canonical big-endian reader over untrusted packet bytes.</summary>
public ref struct NetworkPacketReader
{
    private ReadOnlySpan<byte> _source;
    private int _position;

    public NetworkPacketReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _position = 0;
    }

    public readonly int ConsumedCount => _position;
    public readonly int Remaining => _source.Length - _position;
    public readonly bool IsComplete => _position == _source.Length;

    public bool TryReadByte(out byte value)
    {
        if (Remaining < 1) { value = 0; return false; }
        value = _source[_position++];
        return true;
    }

    public bool TryReadBoolean(out bool value)
    {
        NetworkPacketReader staged = this;
        if (!staged.TryReadByte(out byte encoded) || encoded > 1)
        {
            value = false;
            return false;
        }
        value = encoded != 0;
        this = staged;
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (Remaining < sizeof(ushort)) { value = 0; return false; }
        value = BinaryPrimitives.ReadUInt16BigEndian(_source[_position..]);
        _position += sizeof(ushort);
        return true;
    }

    public bool TryReadInt16(out short value)
    {
        bool read = TryReadUInt16(out ushort encoded);
        value = unchecked((short)encoded);
        return read;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (Remaining < sizeof(uint)) { value = 0; return false; }
        value = BinaryPrimitives.ReadUInt32BigEndian(_source[_position..]);
        _position += sizeof(uint);
        return true;
    }

    public bool TryReadInt32(out int value)
    {
        bool read = TryReadUInt32(out uint encoded);
        value = unchecked((int)encoded);
        return read;
    }

    public bool TryReadUInt64(out ulong value)
    {
        if (Remaining < sizeof(ulong)) { value = 0; return false; }
        value = BinaryPrimitives.ReadUInt64BigEndian(_source[_position..]);
        _position += sizeof(ulong);
        return true;
    }

    public bool TryReadInt64(out long value)
    {
        bool read = TryReadUInt64(out ulong encoded);
        value = unchecked((long)encoded);
        return read;
    }

    public bool TryReadVarUInt64(out ulong value)
    {
        NetworkPacketReader staged = this;
        ulong result = 0;
        for (int index = 0; index < 10; index++)
        {
            if (!staged.TryReadByte(out byte current)) { value = 0; return false; }
            if (index == 9 && current > 1) { value = 0; return false; }
            result |= (ulong)(current & 0x7F) << (index * 7);
            if ((current & 0x80) == 0)
            {
                if (NetworkCanonicalEncoding.MeasureVarUInt64(result) != index + 1)
                {
                    value = 0;
                    return false;
                }
                value = result;
                this = staged;
                return true;
            }
        }
        value = 0;
        return false;
    }

    public bool TryReadBytes(int length, out ReadOnlySpan<byte> value)
    {
        if (length < 0 || length > Remaining) { value = default; return false; }
        value = _source.Slice(_position, length);
        _position += length;
        return true;
    }

    public bool TryReadBoundedUtf8Bytes(int maximumBytes, out ReadOnlySpan<byte> value)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        NetworkPacketReader staged = this;
        if (!staged.TryReadVarUInt64(out ulong encodedLength) || encodedLength > (ulong)maximumBytes ||
            encodedLength > int.MaxValue || !staged.TryReadBytes((int)encodedLength, out ReadOnlySpan<byte> bytes) ||
            !NetworkCanonicalEncoding.IsCanonicalUtf8(bytes))
        {
            value = default;
            return false;
        }
        value = bytes;
        this = staged;
        return true;
    }
}

public static class NetworkCanonicalEncoding
{
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int MeasureVarUInt64(ulong value)
    {
        int bytes = 1;
        while (value >= 0x80) { value >>= 7; bytes++; }
        return bytes;
    }

    public static bool IsCanonicalUtf8(ReadOnlySpan<byte> bytes)
    {
        try { _ = StrictUtf8.GetCharCount(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}
