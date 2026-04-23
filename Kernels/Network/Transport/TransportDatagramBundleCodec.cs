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
    private const int EntryHeaderSize = sizeof(ushort);

    public static bool IsBundle(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
            return false;

        return ReadUInt16(source) == BundleMarker;
    }

    public static ImmutablePayloadBuffer EncodeBundle(
        IReadOnlyList<ImmutablePayloadBuffer> payloads,
        int startIndex,
        int maxDatagramBytes,
        out int packedCount)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if ((uint)startIndex >= (uint)payloads.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (maxDatagramBytes < HeaderSize + EntryHeaderSize + 1)
            throw new ArgumentOutOfRangeException(nameof(maxDatagramBytes));

        int totalBytes = HeaderSize;
        packedCount = 0;
        for (int i = startIndex; i < payloads.Count; i++)
        {
            ImmutablePayloadBuffer payload = payloads[i];
            int entryBytes = EntryHeaderSize + payload.Length;
            if (packedCount > 0 && totalBytes + entryBytes > maxDatagramBytes)
                break;

            if (entryBytes > maxDatagramBytes - HeaderSize)
                throw new InvalidOperationException("Single envelope exceeds transport datagram capacity.");

            totalBytes += entryBytes;
            packedCount++;
        }

        if (packedCount <= 0)
            throw new InvalidOperationException("Transport bundle must contain at least one envelope.");

        byte[] buffer = new byte[totalBytes];
        int offset = 0;
        WriteUInt16(buffer, ref offset, BundleMarker);
        WriteUInt16(buffer, ref offset, checked((ushort)packedCount));

        for (int i = 0; i < packedCount; i++)
        {
            ImmutablePayloadBuffer payload = payloads[startIndex + i];
            WriteUInt16(buffer, ref offset, checked((ushort)payload.Length));
            payload.Span.CopyTo(buffer.AsSpan(offset, payload.Length));
            offset += payload.Length;
        }

        return ImmutablePayloadBuffer.FromOwned(buffer);
    }

    public ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;
        private ushort _remainingEntries;

        public Reader(ReadOnlySpan<byte> source)
        {
            if (source.Length < HeaderSize || ReadUInt16(source) != BundleMarker)
                throw new ArgumentOutOfRangeException(nameof(source));

            _remainingEntries = ReadUInt16(source.Slice(sizeof(ushort)));
            _remaining = source.Slice(HeaderSize);
        }

        public ushort Count => _remainingEntries;

        public bool TryReadNext(out ReadOnlySpan<byte> payload)
        {
            payload = default;
            if (_remainingEntries == 0)
                return false;
            if (_remaining.Length < EntryHeaderSize)
                return false;

            ushort length = ReadUInt16(_remaining);
            if (_remaining.Length < EntryHeaderSize + length)
                return false;

            payload = _remaining.Slice(EntryHeaderSize, length);
            _remaining = _remaining.Slice(EntryHeaderSize + length);
            _remainingEntries--;
            return true;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source)
    {
        return (ushort)(source[0] | (source[1] << 8));
    }

    private static void WriteUInt16(byte[] destination, ref int offset, ushort value)
    {
        destination[offset++] = (byte)value;
        destination[offset++] = (byte)(value >> 8);
    }
}
