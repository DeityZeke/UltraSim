// Shared/Network/Buffers/ByteRingBuffer.cs
// Lock-free ring buffer for TCP stream reassembly

using System.Runtime.CompilerServices;

namespace Network.Shared.Network
{
    /// <summary>
    /// Circular byte buffer for efficient TCP stream handling.
    /// Eliminates need for buffer compaction after reading.
    /// NOT thread-safe - use from single thread only.
    /// </summary>
    public sealed class ByteRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private int _head;  // Write position
        private int _tail;  // Read position
        private int _count; // Bytes available for reading

        /// <summary>
        /// Number of bytes available for reading.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Total capacity of the buffer.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Available space for writing.
        /// </summary>
        public int FreeSpace => _capacity - _count;

        /// <summary>
        /// Whether the buffer is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Whether the buffer is full.
        /// </summary>
        public bool IsFull => _count == _capacity;

        public ByteRingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _capacity = capacity;
            _buffer = new byte[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Write data to the buffer.
        /// Returns number of bytes written (may be less than input if buffer full).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Write(ReadOnlySpan<byte> data)
        {
            int toWrite = Math.Min(data.Length, FreeSpace);
            if (toWrite == 0)
                return 0;

            int firstPart = Math.Min(toWrite, _capacity - _head);
            data.Slice(0, firstPart).CopyTo(_buffer.AsSpan(_head, firstPart));

            if (toWrite > firstPart)
            {
                // Wrap around
                int secondPart = toWrite - firstPart;
                data.Slice(firstPart, secondPart).CopyTo(_buffer.AsSpan(0, secondPart));
            }

            _head = (_head + toWrite) % _capacity;
            _count += toWrite;

            return toWrite;
        }

        /// <summary>
        /// Peek at data without consuming it.
        /// Copies up to 'length' bytes to destination.
        /// Returns actual number of bytes copied.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek(Span<byte> destination)
        {
            int toPeek = Math.Min(destination.Length, _count);
            if (toPeek == 0)
                return 0;

            int firstPart = Math.Min(toPeek, _capacity - _tail);
            _buffer.AsSpan(_tail, firstPart).CopyTo(destination);

            if (toPeek > firstPart)
            {
                // Wrap around
                int secondPart = toPeek - firstPart;
                _buffer.AsSpan(0, secondPart).CopyTo(destination.Slice(firstPart));
            }

            return toPeek;
        }

        /// <summary>
        /// Peek at a single byte at the given offset from read position.
        /// Returns false if offset is out of range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeekByte(int offset, out byte value)
        {
            if (offset < 0 || offset >= _count)
            {
                value = 0;
                return false;
            }

            int index = (_tail + offset) % _capacity;
            value = _buffer[index];
            return true;
        }

        /// <summary>
        /// Peek at two bytes as a ushort (little-endian) at the given offset.
        /// Returns false if not enough data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeekUInt16(int offset, out ushort value)
        {
            if (offset < 0 || offset + 2 > _count)
            {
                value = 0;
                return false;
            }

            int index1 = (_tail + offset) % _capacity;
            int index2 = (_tail + offset + 1) % _capacity;
            value = (ushort)(_buffer[index1] | (_buffer[index2] << 8));
            return true;
        }

        /// <summary>
        /// Advance read position, consuming data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (count < 0 || count > _count)
                throw new ArgumentOutOfRangeException(nameof(count));

            _tail = (_tail + count) % _capacity;
            _count -= count;
        }

        /// <summary>
        /// Read and consume data from the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(Span<byte> destination)
        {
            int read = Peek(destination);
            if (read > 0)
                Advance(read);
            return read;
        }

        /// <summary>
        /// Get a contiguous read span of available data.
        /// May not include all data if it wraps around - use for optimization only.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetContiguousReadSpan()
        {
            if (_count == 0)
                return ReadOnlySpan<byte>.Empty;

            int available = Math.Min(_count, _capacity - _tail);
            return _buffer.AsSpan(_tail, available);
        }

        /// <summary>
        /// Get both read spans (for when data wraps around).
        /// </summary>
        public void GetReadSpans(out ReadOnlySpan<byte> first, out ReadOnlySpan<byte> second)
        {
            if (_count == 0)
            {
                first = ReadOnlySpan<byte>.Empty;
                second = ReadOnlySpan<byte>.Empty;
                return;
            }

            int firstPart = Math.Min(_count, _capacity - _tail);
            first = _buffer.AsSpan(_tail, firstPart);

            if (_count > firstPart)
            {
                second = _buffer.AsSpan(0, _count - firstPart);
            }
            else
            {
                second = ReadOnlySpan<byte>.Empty;
            }
        }

        /// <summary>
        /// Get a span to write into directly (for socket receive).
        /// Call CommitWrite() after writing to update state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetWriteSpan(int maxLength)
        {
            int available = Math.Min(maxLength, FreeSpace);
            available = Math.Min(available, _capacity - _head); // Only contiguous portion
            return _buffer.AsSpan(_head, available);
        }

        /// <summary>
        /// Commit bytes written directly via GetWriteSpan().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CommitWrite(int bytesWritten)
        {
            if (bytesWritten < 0 || bytesWritten > FreeSpace)
                throw new ArgumentOutOfRangeException(nameof(bytesWritten));

            _head = (_head + bytesWritten) % _capacity;
            _count += bytesWritten;
        }

        /// <summary>
        /// Clear all data from the buffer.
        /// </summary>
        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Copy all available data to a new byte array.
        /// For debugging/logging only - allocates!
        /// </summary>
        public byte[] ToArray()
        {
            var result = new byte[_count];
            Peek(result);
            return result;
        }
    }
}
