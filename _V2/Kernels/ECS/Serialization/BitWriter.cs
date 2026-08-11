using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace UltraSim.ECS;

public ref struct BitWriter
{
    private Span<byte> _buffer;
    private int _position;

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public int Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position;
    }

    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;
    }

    public int Remaining
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length - _position;
    }

    public Span<byte> WrittenSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer[.._position];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _position = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        EnsureRemaining(1);
        _buffer[_position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        EnsureRemaining(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.Slice(_position, 2), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        EnsureRemaining(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_position, 2), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        EnsureRemaining(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        EnsureRemaining(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        EnsureRemaining(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.Slice(_position, 8), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        EnsureRemaining(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_position, 8), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureRemaining(data.Length);
        data.CopyTo(_buffer.Slice(_position, data.Length));
        _position += data.Length;
    }

    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(byteCount);
        EnsureRemaining(byteCount);
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.Slice(_position, byteCount));
        _position += written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureRemaining(int needed)
    {
        if ((uint)(_position + needed) <= (uint)_buffer.Length)
            return;

        throw new InvalidOperationException($"BitWriter overflow: need {needed} bytes, remaining {Remaining}.");
    }
}
