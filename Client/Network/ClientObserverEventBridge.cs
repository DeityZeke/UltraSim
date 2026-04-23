using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// Narrow client-side bridge for observer-visible ordered event frames.
/// It intentionally keeps payload-specific handling outside the transport/session host.
/// </summary>
public sealed class ClientObserverEventBridge
{
    private readonly ConnectionId _connectionId;
    private readonly PacketRegistry<ClientObserverEventBridge> _registry;
    private readonly ClientReliableEventFrameApplier? _applyEvent;
    private ulong _lastAppliedSequence;

    public ClientObserverEventBridge(ConnectionId connectionId, ClientReliableEventFrameApplier? applyEvent = null)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _connectionId = connectionId;
        _applyEvent = applyEvent;
        _registry = new PacketRegistry<ClientObserverEventBridge>();
        _registry.Register<ReliableEventFramePacket>(static (ClientObserverEventBridge bridge, in ReliableEventFramePacket packet) => bridge.OnReliableEventFrame(packet));
        _registry.Seal();
    }

    public ConnectionId ConnectionId => _connectionId;
    public int LastReceivedEventCount { get; private set; }
    public int LastAppliedEventCount { get; private set; }
    public ulong LastAppliedSequence => _lastAppliedSequence;

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        LastReceivedEventCount = 0;
        LastAppliedEventCount = 0;

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

    private void OnReliableEventFrame(in ReliableEventFramePacket packet)
    {
        LastReceivedEventCount++;
        if (_lastAppliedSequence != 0 && packet.Sequence <= _lastAppliedSequence)
            return;

        _lastAppliedSequence = packet.Sequence;
        _applyEvent?.Invoke(packet.Sequence, packet.Payload);
        LastAppliedEventCount++;
    }
}
