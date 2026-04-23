namespace UltraSim.Network;

/// <summary>
/// Encodes retained ordered-event replay entries back into normal state-plane datagrams
/// for one connection. This reuses the same published-frame path as first-send traffic
/// instead of inventing a separate retransmit packet shape.
/// </summary>
public sealed class ReliableEventRetransmitBuilder
{
    private readonly OutboundDatagramAssembler _assembler;

    public ReliableEventRetransmitBuilder(OutboundDatagramAssembler? assembler = null)
    {
        _assembler = assembler ?? new OutboundDatagramAssembler();
    }

    public OutboundAssemblyResult Build(
        ConnectionId connectionId,
        ReadOnlySpan<ReliableEventReplayEntry> entries,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (destination.Length == 0 || entries.Length == 0)
            return default;

        int count = 0;
        int compressed = 0;
        int encrypted = 0;

        for (int i = 0; i < entries.Length && count < destination.Length; i++)
        {
            ReliableEventReplayEntry entry = entries[i];
            PublishedFrame frame = SharedFramePublisher.PublishOrderedEvent(
                entry.Sequence,
                entry.Payload,
                entry.PacketClass);

            OutboundAssemblyResult assembly = _assembler.AssemblePublished(
                connectionId,
                frame,
                destination[count..],
                allowCompression,
                allowSegmentation);
            if (assembly.Count == 0)
                break;

            count += assembly.Count;
            compressed += assembly.CompressedCount;
            encrypted += assembly.EncryptedCount;
        }

        return new OutboundAssemblyResult(count, compressed, encrypted);
    }
}
