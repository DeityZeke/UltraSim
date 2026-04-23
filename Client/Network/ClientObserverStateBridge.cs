using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// Narrow client-side bridge for observer-visible latest-state frames. It keeps transport/session
/// concerns out of the client wrapper while letting callers own payload-specific decode/apply work.
/// </summary>
public sealed class ClientObserverStateBridge
{
    private readonly ConnectionId _connectionId;
    private readonly PacketRegistry<ClientObserverStateBridge> _registry;
    private readonly Dictionary<ulong, ulong> _latestSequenceByItemKey = new();
    private readonly ClientLatestStateFrameApplier? _applyLatestState;

    public ClientObserverStateBridge(ConnectionId connectionId, ClientLatestStateFrameApplier? applyLatestState = null)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _connectionId = connectionId;
        _applyLatestState = applyLatestState;
        _registry = new PacketRegistry<ClientObserverStateBridge>();
        _registry.Register<RealtimeStateFramePacket>(static (ClientObserverStateBridge bridge, in RealtimeStateFramePacket packet) => bridge.OnRealtimeStateFrame(packet));
        _registry.Seal();
    }

    public ConnectionId ConnectionId => _connectionId;
    public int LatestItemCount => _latestSequenceByItemKey.Count;
    public int LastReceivedFrameCount { get; private set; }
    public int LastAppliedFrameCount { get; private set; }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        LastReceivedFrameCount = 0;
        LastAppliedFrameCount = 0;

        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId != _connectionId || datagram.Plane != NetworkPlane.State)
                continue;

            if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                continue;

            if (!_registry.TryDispatch(this, header, payload))
                continue;

            processed++;
        }

        return processed;
    }

    private void OnRealtimeStateFrame(in RealtimeStateFramePacket packet)
    {
        LastReceivedFrameCount++;
        if (_latestSequenceByItemKey.TryGetValue(packet.ItemKey, out ulong previousSequence) &&
            packet.Sequence <= previousSequence)
        {
            return;
        }

        _latestSequenceByItemKey[packet.ItemKey] = packet.Sequence;
        _applyLatestState?.Invoke(packet.ItemKey, packet.Sequence, packet.Payload);
        LastAppliedFrameCount++;
    }
}
