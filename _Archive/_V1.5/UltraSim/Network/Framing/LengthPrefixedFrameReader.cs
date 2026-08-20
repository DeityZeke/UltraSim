using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class LengthPrefixedFrameReader : IDisposable
    {
        public const int LengthPrefixSize = 2;
        private const int DefaultMaxInvalidFrameScansPerPeek = 1024;

        private byte[] _buffer;
        private bool _bufferFromPool;
        private int _indexMask;
        private int _head;
        private int _tail;
        private int _count;
        private readonly int _maxFrameLength;
        private readonly int _maxBufferCapacity;
        private readonly InvalidFramePolicy _invalidFramePolicy;
        private readonly Action<InvalidFrameEvent>? _invalidFrameHandler;
        private readonly FrameReaderStats? _stats;
        private readonly int _maxInvalidFrameScansPerPeek;
        private int _invalidScanStreak;

        public LengthPrefixedFrameReader(
            int initialCapacity = 4096,
            int maxFrameLength = ushort.MaxValue,
            InvalidFramePolicy invalidFramePolicy = InvalidFramePolicy.Throw,
            FrameReaderStats? stats = null,
            Action<InvalidFrameEvent>? invalidFrameHandler = null,
            int maxBufferCapacity = int.MaxValue,
            int maxInvalidFrameScansPerPeek = DefaultMaxInvalidFrameScansPerPeek)
        {
            if (initialCapacity < LengthPrefixSize)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity is too small.");
            if (maxFrameLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFrameLength), "Max frame length must be positive.");
            if (maxBufferCapacity < initialCapacity)
                throw new ArgumentOutOfRangeException(
                    nameof(maxBufferCapacity),
                    "Max buffer capacity must be at least initial capacity.");
            if (maxInvalidFrameScansPerPeek <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxInvalidFrameScansPerPeek),
                    "Max invalid frame scans per peek must be positive.");

            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _bufferFromPool = true;
            _indexMask = GetPowerOfTwoMaskOrMinusOne(_buffer.Length);
            _head = 0;
            _tail = 0;
            _count = 0;
            _maxFrameLength = maxFrameLength;
            _maxBufferCapacity = maxBufferCapacity;
            _invalidFramePolicy = invalidFramePolicy;
            _stats = stats;
            _invalidFrameHandler = invalidFrameHandler;
            _maxInvalidFrameScansPerPeek = maxInvalidFrameScansPerPeek;
        }

        public int BufferedBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Length;
        }

        public int MaxFrameLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _maxFrameLength;
        }

        public int MaxBufferCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _maxBufferCapacity;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
            _invalidScanStreak = 0;
        }

        public void Append(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
                return;

            if (bytes.Length > _maxBufferCapacity - _count)
                ThrowBufferCapacityExceeded(_count + bytes.Length, _maxBufferCapacity);

            int required = _count + bytes.Length;
            EnsureCapacity(required);

            int firstCopy = Math.Min(bytes.Length, _buffer.Length - _head);
            bytes.Slice(0, firstCopy).CopyTo(_buffer.AsSpan(_head, firstCopy));

            int secondCopy = bytes.Length - firstCopy;
            if (secondCopy > 0)
                bytes.Slice(firstCopy, secondCopy).CopyTo(_buffer.AsSpan(0, secondCopy));

            _head = AdvanceIndex(_head, bytes.Length);
            _count += bytes.Length;
            _stats?.RecordAppend(bytes.Length);
        }

        public bool TryPeekFrame(out FrameSegments frame, out int frameLength)
        {
            frame = default;
            frameLength = 0;

            while (true)
            {
                if (_count < LengthPrefixSize)
                    return false;

                frameLength = ReadLengthPrefix();
                if (frameLength > _maxFrameLength)
                {
                    _invalidScanStreak++;
                    if (_invalidScanStreak >= _maxInvalidFrameScansPerPeek)
                    {
                        _stats?.RecordInvalidFrame(0);
                        throw new InvalidDataException(
                            $"Exceeded invalid frame scan limit ({_maxInvalidFrameScansPerPeek}).");
                    }

                    HandleInvalidFrame(frameLength);
                    continue;
                }

                if (_count < LengthPrefixSize + frameLength)
                    return false;

                int payloadStart = AdvanceIndex(_tail, LengthPrefixSize);
                int firstLength = Math.Min(frameLength, _buffer.Length - payloadStart);
                int secondLength = frameLength - firstLength;

                _invalidScanStreak = 0;
                frame = new FrameSegments(_buffer, payloadStart, firstLength, 0, secondLength);
                return true;
            }
        }

        public void ConsumeFrame(int frameLength)
        {
            if ((uint)frameLength > (uint)_maxFrameLength)
                throw new ArgumentOutOfRangeException(nameof(frameLength));
            if (_count < LengthPrefixSize + frameLength)
                throw new InvalidOperationException("Cannot consume frame that is not fully buffered.");

            ConsumeFrameNoValidation(frameLength);
        }

        public bool TryReadFrame(Span<byte> destination, out int frameLength)
        {
            frameLength = 0;
            if (!TryPeekFrame(out var frame, out frameLength))
                return false;

            if (destination.Length < frameLength)
                throw new ArgumentException("Destination span too small for frame payload.", nameof(destination));

            frame.CopyTo(destination);
            ConsumeFrameNoValidation(frameLength);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadLengthPrefix()
        {
            byte b0 = _buffer[_tail];
            byte b1 = _buffer[AdvanceIndex(_tail, 1)];
            return (ushort)(b0 | (b1 << 8));
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
                return;
            if (required > _maxBufferCapacity)
                ThrowBufferCapacityExceeded(required, _maxBufferCapacity);

            int newCapacity = _buffer.Length;
            while (newCapacity < required)
            {
                if (newCapacity > int.MaxValue / 2)
                {
                    newCapacity = required;
                    break;
                }

                newCapacity = Math.Max(newCapacity * 2, LengthPrefixSize);
            }
            if (newCapacity > _maxBufferCapacity)
                newCapacity = _maxBufferCapacity;

            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            if (_count > 0)
            {
                int firstLength = Math.Min(_count, _buffer.Length - _tail);
                _buffer.AsSpan(_tail, firstLength).CopyTo(newBuffer.AsSpan(0, firstLength));

                int secondLength = _count - firstLength;
                if (secondLength > 0)
                    _buffer.AsSpan(0, secondLength).CopyTo(newBuffer.AsSpan(firstLength, secondLength));
            }

            if (_bufferFromPool)
                ArrayPool<byte>.Shared.Return(_buffer);

            _buffer = newBuffer;
            _bufferFromPool = true;
            _indexMask = GetPowerOfTwoMaskOrMinusOne(_buffer.Length);
            _tail = 0;
            _head = _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AdvanceIndex(int index, int amount)
        {
            int length = _buffer.Length;
            if (length == 0)
                return 0;

            int mask = _indexMask;
            if (mask >= 0)
                return (index + amount) & mask;

            int value = index + amount;
            value %= length;
            return value < 0 ? value + length : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPowerOfTwoMaskOrMinusOne(int length)
        {
            if (length <= 0)
                return -1;
            return (length & (length - 1)) == 0 ? length - 1 : -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConsumeFrameNoValidation(int frameLength)
        {
            int total = LengthPrefixSize + frameLength;
            _tail = AdvanceIndex(_tail, total);
            _count -= total;
            _stats?.RecordConsume(frameLength);
        }

        private void HandleInvalidFrame(int frameLength)
        {
            _invalidFrameHandler?.Invoke(new InvalidFrameEvent(frameLength, _maxFrameLength, _count));

            int droppedBytes = 0;
            switch (_invalidFramePolicy)
            {
                case InvalidFramePolicy.Throw:
                    _stats?.RecordInvalidFrame(0);
                    ThrowInvalidFrameLength(frameLength, _maxFrameLength);
                    return;
                case InvalidFramePolicy.DropByte:
                    if (_count > 0)
                    {
                        _tail = AdvanceIndex(_tail, 1);
                        _count--;
                        droppedBytes = 1;
                    }
                    break;
                case InvalidFramePolicy.ResetBuffer:
                    droppedBytes = _count;
                    Clear();
                    break;
                default:
                    ThrowUnsupportedPolicy(_invalidFramePolicy);
                    break;
            }

            _stats?.RecordInvalidFrame(droppedBytes);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidFrameLength(int frameLength, int maxFrameLength) =>
            throw new InvalidDataException($"Invalid frame length {frameLength}; max allowed is {maxFrameLength}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowUnsupportedPolicy(InvalidFramePolicy policy) =>
            throw new InvalidOperationException($"Unsupported invalid frame policy: {policy}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowBufferCapacityExceeded(int required, int maxBufferCapacity) =>
            throw new InvalidDataException(
                $"Buffered bytes would exceed max buffer capacity. Required={required}, Max={maxBufferCapacity}.");

        public void Dispose()
        {
            if (_bufferFromPool)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _bufferFromPool = false;
            }

            _buffer = Array.Empty<byte>();
            _indexMask = -1;
            _head = 0;
            _tail = 0;
            _count = 0;
            _invalidScanStreak = 0;
        }
    }
}
