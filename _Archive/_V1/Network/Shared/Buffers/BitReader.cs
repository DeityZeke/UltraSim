// Shared/Network/Buffers/BitReader.cs
// High-performance bit-level reader for packet deserialization

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Network.Shared.Network
{
    /// <summary>
    /// Ref struct for zero-allocation bit-level reading.
    /// Supports both byte-aligned and bit-packed operations.
    /// </summary>
    public ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _bytePosition;
        private int _bitPosition;  // 0-7, bits read from current byte

        /// <summary>
        /// Current byte position.
        /// </summary>
        public readonly int BytePosition => _bytePosition;

        /// <summary>
        /// Total bits that have been read.
        /// </summary>
        public readonly int BitsRead => _bytePosition * 8 + _bitPosition;

        /// <summary>
        /// Remaining bytes available for reading.
        /// </summary>
        public readonly int RemainingBytes => _buffer.Length - _bytePosition - (_bitPosition > 0 ? 1 : 0);

        /// <summary>
        /// Total buffer length in bytes.
        /// </summary>
        public readonly int Length => _buffer.Length;

        /// <summary>
        /// Whether at end of buffer.
        /// </summary>
        public readonly bool IsAtEnd => _bytePosition >= _buffer.Length;

        /// <summary>
        /// Whether currently byte-aligned.
        /// </summary>
        public readonly bool IsByteAligned => _bitPosition == 0;

        public BitReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _bytePosition = 0;
            _bitPosition = 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                         BIT OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a single bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBit()
        {
            if (_bytePosition >= _buffer.Length)
                throw new InvalidOperationException("Buffer underflow");

            bool value = (_buffer[_bytePosition] & (1 << _bitPosition)) != 0;

            _bitPosition++;
            if (_bitPosition == 8)
            {
                _bitPosition = 0;
                _bytePosition++;
            }

            return value;
        }

        /// <summary>
        /// Read multiple bits into a uint (LSB first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadBits(int bitCount)
        {
            if (bitCount < 0 || bitCount > 32)
                throw new ArgumentOutOfRangeException(nameof(bitCount));

            uint value = 0;
            for (int i = 0; i < bitCount; i++)
            {
                if (ReadBit())
                    value |= 1u << i;
            }

            return value;
        }

        /// <summary>
        /// Read multiple bits into a ulong (LSB first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadBitsLong(int bitCount)
        {
            if (bitCount < 0 || bitCount > 64)
                throw new ArgumentOutOfRangeException(nameof(bitCount));

            ulong value = 0;
            for (int i = 0; i < bitCount; i++)
            {
                if (ReadBit())
                    value |= 1ul << i;
            }

            return value;
        }

        /// <summary>
        /// Skip to next byte boundary.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AlignToByte()
        {
            if (_bitPosition > 0)
            {
                _bitPosition = 0;
                _bytePosition++;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                        BYTE OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a single byte. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            EnsureByteAligned();
            EnsureRemaining(1);

            return _buffer[_bytePosition++];
        }

        /// <summary>
        /// Read a signed byte. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        /// <summary>
        /// Read bytes into a span. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadBytes(Span<byte> destination)
        {
            EnsureByteAligned();
            EnsureRemaining(destination.Length);

            _buffer.Slice(_bytePosition, destination.Length).CopyTo(destination);
            _bytePosition += destination.Length;
        }

        /// <summary>
        /// Read bytes as a new array. Must be byte-aligned.
        /// Allocates - use sparingly in hot path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ReadBytes(int count)
        {
            EnsureByteAligned();
            EnsureRemaining(count);

            var result = _buffer.Slice(_bytePosition, count).ToArray();
            _bytePosition += count;
            return result;
        }

        /// <summary>
        /// Get a readonly span of the next N bytes without copying.
        /// Advances the read position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadSpan(int count)
        {
            EnsureByteAligned();
            EnsureRemaining(count);

            var span = _buffer.Slice(_bytePosition, count);
            _bytePosition += count;
            return span;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      INTEGER OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a 16-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            EnsureByteAligned();
            EnsureRemaining(2);

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_bytePosition));
            _bytePosition += 2;
            return value;
        }

        /// <summary>
        /// Read a 16-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            return (short)ReadUInt16();
        }

        /// <summary>
        /// Read a 32-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            EnsureByteAligned();
            EnsureRemaining(4);

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_bytePosition));
            _bytePosition += 4;
            return value;
        }

        /// <summary>
        /// Read a 32-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        /// <summary>
        /// Read a 64-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            EnsureByteAligned();
            EnsureRemaining(8);

            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_bytePosition));
            _bytePosition += 8;
            return value;
        }

        /// <summary>
        /// Read a 64-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            return (long)ReadUInt64();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      FLOATING POINT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a 32-bit float (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            EnsureByteAligned();
            EnsureRemaining(4);

            float value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_bytePosition));
            _bytePosition += 4;
            return value;
        }

        /// <summary>
        /// Read a 64-bit double (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            EnsureByteAligned();
            EnsureRemaining(8);

            double value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Slice(_bytePosition));
            _bytePosition += 8;
            return value;
        }

        /// <summary>
        /// Read a fixed-point value back to float.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFixedPoint(float min, float max, int bits)
        {
            uint quantized = ReadBits(bits);
            uint maxVal = (1u << bits) - 1;
            float normalized = quantized / (float)maxVal;
            return min + normalized * (max - min);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                          STRINGS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a string with 16-bit length prefix (UTF-8 encoded).
        /// Allocates a new string - use sparingly in hot path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString()
        {
            ushort length = ReadUInt16();
            if (length == 0)
                return string.Empty;

            EnsureRemaining(length);

            string value = Encoding.UTF8.GetString(_buffer.Slice(_bytePosition, length));
            _bytePosition += length;
            return value;
        }

        /// <summary>
        /// Read a fixed-length string (null-terminated within fixed bytes).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadFixedString(int byteLength)
        {
            EnsureByteAligned();
            EnsureRemaining(byteLength);

            var span = _buffer.Slice(_bytePosition, byteLength);
            _bytePosition += byteLength;

            // Find null terminator
            int end = span.IndexOf((byte)0);
            if (end >= 0)
                span = span.Slice(0, end);

            return Encoding.UTF8.GetString(span);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                          SPECIAL
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read a GUID (16 bytes).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Guid ReadGuid()
        {
            EnsureByteAligned();
            EnsureRemaining(16);

            var guid = new Guid(_buffer.Slice(_bytePosition, 16));
            _bytePosition += 16;
            return guid;
        }

        /// <summary>
        /// Read a boolean as a single byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        /// <summary>
        /// Read a variable-length unsigned integer (1-5 bytes).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt()
        {
            EnsureByteAligned();

            uint result = 0;
            int shift = 0;

            while (true)
            {
                EnsureRemaining(1);
                byte b = _buffer[_bytePosition++];

                result |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    break;

                shift += 7;
                if (shift > 28)
                    throw new InvalidOperationException("VarInt too large");
            }

            return result;
        }

        /// <summary>
        /// Read a signed variable-length integer using ZigZag decoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadVarInt()
        {
            uint encoded = ReadVarUInt();
            // ZigZag decode: (encoded >> 1) ^ -(encoded & 1)
            return (int)((encoded >> 1) ^ (~(encoded & 1) + 1));
        }

        // ═══════════════════════════════════════════════════════════════════
        //                          HELPERS
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void EnsureByteAligned()
        {
            if (_bitPosition != 0)
                throw new InvalidOperationException("Operation requires byte alignment");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void EnsureRemaining(int bytes)
        {
            if (_bytePosition + bytes > _buffer.Length)
                throw new InvalidOperationException($"Buffer underflow: need {bytes} bytes, have {_buffer.Length - _bytePosition}");
        }

        /// <summary>
        /// Peek at the next byte without advancing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly byte PeekByte()
        {
            if (_bitPosition != 0 || _bytePosition >= _buffer.Length)
                throw new InvalidOperationException("Cannot peek");

            return _buffer[_bytePosition];
        }

        /// <summary>
        /// Skip N bytes without reading.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(int bytes)
        {
            EnsureByteAligned();
            EnsureRemaining(bytes);
            _bytePosition += bytes;
        }

        /// <summary>
        /// Get remaining unread data as a span.
        /// </summary>
        public readonly ReadOnlySpan<byte> RemainingSpan
        {
            get
            {
                if (!IsByteAligned || _bytePosition >= _buffer.Length)
                    return ReadOnlySpan<byte>.Empty;

                return _buffer.Slice(_bytePosition);
            }
        }

        /// <summary>
        /// Reset reader to beginning of buffer.
        /// </summary>
        public void Reset()
        {
            _bytePosition = 0;
            _bitPosition = 0;
        }

        /// <summary>
        /// Seek to a specific byte position.
        /// </summary>
        public void Seek(int bytePosition)
        {
            if (bytePosition < 0 || bytePosition > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(bytePosition));

            _bytePosition = bytePosition;
            _bitPosition = 0;
        }
    }
}
