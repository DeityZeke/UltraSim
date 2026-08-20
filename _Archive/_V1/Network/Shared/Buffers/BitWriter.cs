// Shared/Network/Buffers/BitWriter.cs
// High-performance bit-level writer for packet serialization

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Network.Shared.Network
{
    /// <summary>
    /// Ref struct for zero-allocation bit-level writing.
    /// Supports both byte-aligned and bit-packed operations.
    /// </summary>
    public ref struct BitWriter
    {
        private readonly Span<byte> _buffer;
        private int _bytePosition;
        private int _bitPosition;  // 0-7, bits written in current byte

        /// <summary>
        /// Total bytes that have been written (including partial byte).
        /// </summary>
        public readonly int BytesWritten => _bitPosition > 0 ? _bytePosition + 1 : _bytePosition;

        /// <summary>
        /// Total bits that have been written.
        /// </summary>
        public readonly int BitsWritten => _bytePosition * 8 + _bitPosition;

        /// <summary>
        /// Remaining capacity in bytes.
        /// </summary>
        public readonly int RemainingBytes => _buffer.Length - BytesWritten;

        /// <summary>
        /// Total capacity in bytes.
        /// </summary>
        public readonly int Capacity => _buffer.Length;

        /// <summary>
        /// Whether currently byte-aligned (no pending bits).
        /// </summary>
        public readonly bool IsByteAligned => _bitPosition == 0;

        public BitWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _bytePosition = 0;
            _bitPosition = 0;

            // Clear buffer for clean bit operations
            buffer.Clear();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                         BIT OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a single bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBit(bool value)
        {
            if (_bytePosition >= _buffer.Length)
                throw new InvalidOperationException("Buffer overflow");

            if (value)
            {
                _buffer[_bytePosition] |= (byte)(1 << _bitPosition);
            }

            _bitPosition++;
            if (_bitPosition == 8)
            {
                _bitPosition = 0;
                _bytePosition++;
            }
        }

        /// <summary>
        /// Write multiple bits from a value (LSB first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBits(uint value, int bitCount)
        {
            if (bitCount < 0 || bitCount > 32)
                throw new ArgumentOutOfRangeException(nameof(bitCount));

            for (int i = 0; i < bitCount; i++)
            {
                WriteBit((value & (1u << i)) != 0);
            }
        }

        /// <summary>
        /// Write multiple bits from a ulong value (LSB first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBits(ulong value, int bitCount)
        {
            if (bitCount < 0 || bitCount > 64)
                throw new ArgumentOutOfRangeException(nameof(bitCount));

            for (int i = 0; i < bitCount; i++)
            {
                WriteBit((value & (1ul << i)) != 0);
            }
        }

        /// <summary>
        /// Align to next byte boundary by padding with zeros.
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
        /// Write a single byte. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            EnsureByteAligned();
            EnsureCapacity(1);

            _buffer[_bytePosition++] = value;
        }

        /// <summary>
        /// Write a signed byte. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
        {
            WriteByte((byte)value);
        }

        /// <summary>
        /// Write a span of bytes. Must be byte-aligned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            EnsureByteAligned();
            EnsureCapacity(bytes.Length);

            bytes.CopyTo(_buffer.Slice(_bytePosition));
            _bytePosition += bytes.Length;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      INTEGER OPERATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a 16-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value)
        {
            EnsureByteAligned();
            EnsureCapacity(2);

            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_bytePosition), value);
            _bytePosition += 2;
        }

        /// <summary>
        /// Write a 16-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt16(short value)
        {
            WriteUInt16((ushort)value);
        }

        /// <summary>
        /// Write a 32-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            EnsureByteAligned();
            EnsureCapacity(4);

            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_bytePosition), value);
            _bytePosition += 4;
        }

        /// <summary>
        /// Write a 32-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value)
        {
            WriteUInt32((uint)value);
        }

        /// <summary>
        /// Write a 64-bit unsigned integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            EnsureByteAligned();
            EnsureCapacity(8);

            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_bytePosition), value);
            _bytePosition += 8;
        }

        /// <summary>
        /// Write a 64-bit signed integer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value)
        {
            WriteUInt64((ulong)value);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      FLOATING POINT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a 32-bit float (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            EnsureByteAligned();
            EnsureCapacity(4);

            BinaryPrimitives.WriteSingleLittleEndian(_buffer.Slice(_bytePosition), value);
            _bytePosition += 4;
        }

        /// <summary>
        /// Write a 64-bit double (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            EnsureByteAligned();
            EnsureCapacity(8);

            BinaryPrimitives.WriteDoubleLittleEndian(_buffer.Slice(_bytePosition), value);
            _bytePosition += 8;
        }

        /// <summary>
        /// Write a float as a fixed-point value with specified precision.
        /// More compact than full float for values with known ranges.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedPoint(float value, float min, float max, int bits)
        {
            float normalized = (value - min) / (max - min);
            normalized = Math.Clamp(normalized, 0f, 1f);

            uint maxVal = (1u << bits) - 1;
            uint quantized = (uint)(normalized * maxVal);

            WriteBits(quantized, bits);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                          STRINGS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a string with 16-bit length prefix (UTF-8 encoded).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt16(0);
                return;
            }

            EnsureByteAligned();

            // Calculate UTF-8 byte count
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > ushort.MaxValue)
                throw new ArgumentException("String too long");

            EnsureCapacity(2 + byteCount);

            // Write length
            WriteUInt16((ushort)byteCount);

            // Write UTF-8 bytes directly to buffer
            Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.Slice(_bytePosition, byteCount));
            _bytePosition += byteCount;
        }

        /// <summary>
        /// Write a fixed-length string (padded or truncated).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedString(string? value, int byteLength)
        {
            EnsureByteAligned();
            EnsureCapacity(byteLength);

            var target = _buffer.Slice(_bytePosition, byteLength);
            target.Clear();

            if (!string.IsNullOrEmpty(value))
            {
                int written = Encoding.UTF8.GetBytes(
                    value.AsSpan(),
                    target);
                // Truncate if needed (already handled by target size)
            }

            _bytePosition += byteLength;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                          SPECIAL
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a GUID as 16 bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteGuid(Guid value)
        {
            EnsureByteAligned();
            EnsureCapacity(16);

            value.TryWriteBytes(_buffer.Slice(_bytePosition, 16));
            _bytePosition += 16;
        }

        /// <summary>
        /// Write a boolean as a single byte (0 or 1).
        /// For bit-packed booleans, use WriteBit().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        /// <summary>
        /// Write a variable-length integer (1-5 bytes).
        /// Efficient for small positive values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt(uint value)
        {
            EnsureByteAligned();

            while (value >= 0x80)
            {
                EnsureCapacity(1);
                _buffer[_bytePosition++] = (byte)(value | 0x80);
                value >>= 7;
            }

            EnsureCapacity(1);
            _buffer[_bytePosition++] = (byte)value;
        }

        /// <summary>
        /// Write a signed variable-length integer using ZigZag encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt(int value)
        {
            // ZigZag encode: (value << 1) ^ (value >> 31)
            uint encoded = (uint)((value << 1) ^ (value >> 31));
            WriteVarInt(encoded);
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
        private readonly void EnsureCapacity(int bytes)
        {
            if (_bytePosition + bytes > _buffer.Length)
                throw new InvalidOperationException($"Buffer overflow: need {bytes} bytes, have {_buffer.Length - _bytePosition}");
        }

        /// <summary>
        /// Get the written data as a span.
        /// </summary>
        public readonly ReadOnlySpan<byte> WrittenSpan => _buffer.Slice(0, BytesWritten);

        /// <summary>
        /// Get the remaining writable buffer.
        /// </summary>
        public readonly Span<byte> RemainingSpan => _buffer.Slice(BytesWritten);

        /// <summary>
        /// Reset writer to beginning of buffer.
        /// </summary>
        public void Reset()
        {
            _bytePosition = 0;
            _bitPosition = 0;
        }

        /// <summary>
        /// Copy written data to destination.
        /// </summary>
        public readonly void CopyTo(Span<byte> destination)
        {
            WrittenSpan.CopyTo(destination);
        }
    }
}
