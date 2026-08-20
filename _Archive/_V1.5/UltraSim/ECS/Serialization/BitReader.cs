using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace UltraSim.ECS
{
    public ref struct BitReader
    {
        private ReadOnlySpan<byte> _buffer;
        private int _position;

        public BitReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Length;
        }

        public int Remaining
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Length - _position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _position = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadByte() != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            EnsureRemaining(1);
            return _buffer[_position++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            EnsureRemaining(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Slice(_position, 2));
            _position += 2;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            EnsureRemaining(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_position, 2));
            _position += 2;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            EnsureRemaining(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_position, 4));
            _position += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            EnsureRemaining(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position, 4));
            _position += 4;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            EnsureRemaining(8);
            long value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Slice(_position, 8));
            _position += 8;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            EnsureRemaining(8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_position, 8));
            _position += 8;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            EnsureRemaining(count);
            var span = _buffer.Slice(_position, count);
            _position += count;
            return span;
        }

        public string ReadString()
        {
            int byteCount = ReadInt32();
            if (byteCount < 0)
                throw new InvalidOperationException($"Invalid string length: {byteCount}.");

            EnsureRemaining(byteCount);
            string value = Encoding.UTF8.GetString(_buffer.Slice(_position, byteCount));
            _position += byteCount;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureRemaining(int needed)
        {
            if ((uint)(_position + needed) <= (uint)_buffer.Length)
                return;

            throw new InvalidOperationException($"BitReader underflow: need {needed} bytes, remaining {Remaining}.");
        }
    }
}
