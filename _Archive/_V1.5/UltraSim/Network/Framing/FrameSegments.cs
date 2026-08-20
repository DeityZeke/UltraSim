using System;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public readonly struct FrameSegments
    {
        private readonly byte[] _buffer;
        private readonly int _firstOffset;
        private readonly int _firstLength;
        private readonly int _secondOffset;
        private readonly int _secondLength;

        internal FrameSegments(byte[] buffer, int firstOffset, int firstLength, int secondOffset, int secondLength)
        {
            _buffer = buffer;
            _firstOffset = firstOffset;
            _firstLength = firstLength;
            _secondOffset = secondOffset;
            _secondLength = secondLength;
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _firstLength + _secondLength;
        }

        public bool IsContiguous
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _secondLength == 0;
        }

        public ReadOnlySpan<byte> First
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.AsSpan(_firstOffset, _firstLength);
        }

        public ReadOnlySpan<byte> Second
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _secondLength == 0 ? ReadOnlySpan<byte>.Empty : _buffer.AsSpan(_secondOffset, _secondLength);
        }

        public void CopyTo(Span<byte> destination)
        {
            if (destination.Length < Length)
                throw new ArgumentException("Destination span is too small for frame data.", nameof(destination));

            First.CopyTo(destination);
            if (_secondLength > 0)
                Second.CopyTo(destination.Slice(_firstLength));
        }

        public void CopyTo(Span<byte> destination, int sourceOffset)
        {
            if ((uint)sourceOffset > (uint)Length)
                throw new ArgumentOutOfRangeException(nameof(sourceOffset));

            int copyLength = Length - sourceOffset;
            if (destination.Length < copyLength)
                throw new ArgumentException("Destination span is too small for frame data.", nameof(destination));
            if (copyLength <= 0)
                return;

            int offset = sourceOffset;
            int written = 0;
            if (offset < _firstLength)
            {
                int firstCopy = _firstLength - offset;
                _buffer.AsSpan(_firstOffset + offset, firstCopy).CopyTo(destination.Slice(written, firstCopy));
                written += firstCopy;
                offset = 0;
            }
            else
            {
                offset -= _firstLength;
            }

            if (written < copyLength)
            {
                int secondCopy = copyLength - written;
                _buffer.AsSpan(_secondOffset + offset, secondCopy).CopyTo(destination.Slice(written, secondCopy));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadByte(int index, out byte value)
        {
            if ((uint)index >= (uint)Length)
            {
                value = 0;
                return false;
            }

            if (index < _firstLength)
                value = _buffer[_firstOffset + index];
            else
                value = _buffer[_secondOffset + (index - _firstLength)];
            return true;
        }
    }
}
