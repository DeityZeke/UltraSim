namespace UltraSim.Network;

/// <summary>
/// Transport-level bundling for multiple already-encoded packet envelopes bound to the same
/// connection. This stays below the packet registry so higher layers still see normal
/// packet envelopes after receive-side unpacking.
/// </summary>
internal static class TransportDatagramBundleCodec
{
    public const ushort BundleMarker = ushort.MaxValue;
    private const int HeaderSize = sizeof(ushort) + sizeof(ushort);
    private const int OffsetSize = sizeof(ushort);

    public static bool IsBundle(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
            return false;

        return ReadUInt16(source) == BundleMarker;
    }

    public static bool TryBeginReadBundle(
        ReadOnlySpan<byte> source,
        out ushort entryCount,
        out ReadOnlySpan<byte> offsets,
        out ReadOnlySpan<byte> payloads)
    {
        entryCount = 0;
        offsets = default;
        payloads = default;
        if (source.Length < HeaderSize || ReadUInt16(source) != BundleMarker)
            return false;

        entryCount = ReadUInt16(source.Slice(sizeof(ushort)));
        int offsetsBytes = entryCount * OffsetSize;
        if (source.Length < HeaderSize + offsetsBytes)
        {
            entryCount = 0;
            return false;
        }

        offsets = source.Slice(HeaderSize, offsetsBytes);
        payloads = source.Slice(HeaderSize + offsetsBytes);
        return true;
    }

    public static int MeasureBundle(
        IReadOnlyList<ImmutablePayloadBuffer> payloads,
        int startIndex,
        int maxDatagramBytes,
        out int packedCount)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if ((uint)startIndex >= (uint)payloads.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (maxDatagramBytes < HeaderSize + OffsetSize + 1)
            throw new ArgumentOutOfRangeException(nameof(maxDatagramBytes));

        int payloadBytes = 0;
        packedCount = 0;
        for (int i = startIndex; i < payloads.Count; i++)
        {
            ImmutablePayloadBuffer payload = payloads[i];
            int requiredBytes = HeaderSize + ((packedCount + 1) * OffsetSize) + payloadBytes + payload.Length;
            if (packedCount > 0 && requiredBytes > maxDatagramBytes)
                break;

            if (HeaderSize + OffsetSize + payload.Length > maxDatagramBytes)
                throw new InvalidOperationException("Single envelope exceeds transport datagram capacity.");

            payloadBytes += payload.Length;
            packedCount++;
        }

        if (packedCount <= 0)
            throw new InvalidOperationException("Transport bundle must contain at least one envelope.");

        return HeaderSize + (packedCount * OffsetSize) + payloadBytes;
    }

    public static void WriteBundle(
        IReadOnlyList<ImmutablePayloadBuffer> payloads,
        int startIndex,
        int packedCount,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if ((uint)startIndex >= (uint)payloads.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (packedCount <= 0 || startIndex + packedCount > payloads.Count)
            throw new ArgumentOutOfRangeException(nameof(packedCount));

        int requiredBytes = HeaderSize + (packedCount * OffsetSize);
        for (int i = 0; i < packedCount; i++)
            requiredBytes += payloads[startIndex + i].Length;

        if (destination.Length < requiredBytes)
            throw new ArgumentOutOfRangeException(nameof(destination));

        int payloadBaseOffset = HeaderSize + (packedCount * OffsetSize);
        int offset = 0;
        WriteUInt16(destination, ref offset, BundleMarker);
        WriteUInt16(destination, ref offset, checked((ushort)packedCount));

        int payloadOffset = 0;
        for (int i = 0; i < packedCount; i++)
        {
            ImmutablePayloadBuffer payload = payloads[startIndex + i];
            WriteUInt16(destination, ref offset, checked((ushort)payloadOffset));
            payload.Span.CopyTo(destination.Slice(payloadBaseOffset + payloadOffset, payload.Length));
            payloadOffset += payload.Length;
        }
    }

    public ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _offsets;
        private readonly ReadOnlySpan<byte> _payloads;
        private readonly ushort _entryCount;
        private int _nextIndex;

        public Reader(ReadOnlySpan<byte> source)
        {
            if (!TryBeginReadBundle(source, out ushort entryCount, out ReadOnlySpan<byte> offsets, out ReadOnlySpan<byte> payloads))
                throw new ArgumentOutOfRangeException(nameof(source));

            _entryCount = entryCount;
            _offsets = offsets;
            _payloads = payloads;
            _nextIndex = 0;
        }

        public ushort Count => _entryCount;

        public bool TryReadNext(out ReadOnlySpan<byte> payload)
        {
            payload = default;
            if (_nextIndex >= _entryCount)
                return false;
            int currentStart = ReadUInt16(_offsets.Slice(_nextIndex * OffsetSize));
            int nextStart = _nextIndex + 1 < _entryCount
                ? ReadUInt16(_offsets.Slice((_nextIndex + 1) * OffsetSize))
                : _payloads.Length;
            if ((uint)currentStart > (uint)_payloads.Length || (uint)nextStart > (uint)_payloads.Length || nextStart < currentStart)
                return false;

            payload = _payloads.Slice(currentStart, nextStart - currentStart);
            _nextIndex++;
            return true;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source)
    {
        return (ushort)(source[0] | (source[1] << 8));
    }

    private static void WriteUInt16(Span<byte> destination, ref int offset, ushort value)
    {
        destination[offset++] = (byte)value;
        destination[offset++] = (byte)(value >> 8);
    }
}
