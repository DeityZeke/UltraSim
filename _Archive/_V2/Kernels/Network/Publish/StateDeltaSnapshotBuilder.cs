namespace UltraSim.Network;

/// <summary>
/// Builds incremental latest-state delta datagrams for one connection from items that are newer
/// than the connection's last-sent view.
/// </summary>
public sealed class StateDeltaSnapshotBuilder
{
    private readonly ConnectionLatestStateSentTracker _sentTracker;

    public StateDeltaSnapshotBuilder(ConnectionLatestStateSentTracker sentTracker)
    {
        _sentTracker = sentTracker ?? throw new ArgumentNullException(nameof(sentTracker));
    }

    public int Build(
        ConnectionId connectionId,
        StateBaselineDescriptor baseline,
        ReadOnlySpan<LatestStateUpdate> items,
        Span<OutboundDatagram> destination)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (baseline.BaselineId == 0)
            throw new ArgumentOutOfRangeException(nameof(baseline));
        if (destination.Length < items.Length)
            return 0;

        int written = 0;
        for (int i = 0; i < items.Length; i++)
        {
            LatestStateUpdate item = items[i];
            destination[written++] = new OutboundDatagram(
                connectionId,
                StateDeltaFramePacket.Plane,
                StateDeltaFramePacket.PacketClass,
                StateDeltaFramePacket.Channel,
                PacketCodec.Encode(new StateDeltaFramePacket(
                    baseline.BaselineId,
                    item.ItemKey,
                    item.Sequence,
                    item.Payload)));
        }

        _sentTracker.MarkSent(connectionId, items);
        return written;
    }
}
