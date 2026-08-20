namespace UltraSim.Network;

/// <summary>
/// Builds a full-state baseline snapshot datagram sequence for one connection:
/// begin, N state frames, then complete.
/// </summary>
public sealed class StateBaselineSnapshotBuilder
{
    public int Build(
        ConnectionId connectionId,
        StateBaselineDescriptor descriptor,
        ReadOnlySpan<LatestStateUpdate> items,
        Span<OutboundDatagram> destination)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (items.Length != descriptor.ItemCount)
            throw new ArgumentOutOfRangeException(nameof(items), "Snapshot item count must match the descriptor item count.");
        if (destination.Length < items.Length + 2)
            return 0;

        int written = 0;
        destination[written++] = new OutboundDatagram(
            connectionId,
            StateBaselineBeginPacket.Plane,
            StateBaselineBeginPacket.PacketClass,
            StateBaselineBeginPacket.Channel,
            PacketCodec.Encode(new StateBaselineBeginPacket(descriptor)));

        for (int i = 0; i < items.Length; i++)
        {
            LatestStateUpdate item = items[i];
            destination[written++] = new OutboundDatagram(
                connectionId,
                StateBaselineFramePacket.Plane,
                StateBaselineFramePacket.PacketClass,
                StateBaselineFramePacket.Channel,
                PacketCodec.Encode(new StateBaselineFramePacket(
                    descriptor.BaselineId,
                    item.ItemKey,
                    item.Sequence,
                    item.Payload)));
        }

        destination[written++] = new OutboundDatagram(
            connectionId,
            StateBaselineCompletePacket.Plane,
            StateBaselineCompletePacket.PacketClass,
            StateBaselineCompletePacket.Channel,
            PacketCodec.Encode(new StateBaselineCompletePacket(descriptor.BaselineId)));

        return written;
    }
}
