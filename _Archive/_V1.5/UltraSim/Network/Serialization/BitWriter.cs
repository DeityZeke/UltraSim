using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace UltraSim.Network
{
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

        public int Remaining
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Length - _position;
        }

        public Span<byte> WrittenSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Slice(0, _position);
        }

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
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            EnsureRemaining(value.Length);
            value.CopyTo(_buffer.Slice(_position, value.Length));
            _position += value.Length;
        }

        public void WriteString(string value)
        {
            if (value == null)
                ThrowArgumentNull(nameof(value));

            WriteString(value.AsSpan());
        }

        public void WriteString(ReadOnlySpan<char> value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if ((uint)byteCount > ushort.MaxValue)
                ThrowStringTooLong(byteCount);

            EnsureRemaining(2 + byteCount);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_position, 2), (ushort)byteCount);
            _position += 2;
            int written = Encoding.UTF8.GetBytes(value, _buffer.Slice(_position, byteCount));
            _position += written;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureRemaining(int needed)
        {
            if (needed >= 0 && needed <= _buffer.Length - _position)
                return;

            ThrowOverflow(needed);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowOverflow(int needed) =>
            throw new InvalidOperationException($"BitWriter overflow. Needed={needed}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowStringTooLong(int length) =>
            throw new ArgumentOutOfRangeException(nameof(length), $"String UTF-8 byte length {length} exceeds 65535.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNull(string name) => throw new ArgumentNullException(name);
    }
}
