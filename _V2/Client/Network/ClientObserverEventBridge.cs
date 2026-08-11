using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// Narrow client-side bridge for observer-visible ordered event frames.
/// It intentionally keeps payload-specific handling outside the transport/session host.
/// </summary>
public sealed class ClientObserverEventBridge
{
    private readonly ConnectionId _connectionId;
    private readonly ClientReliableEventFrameApplier? _applyEvent;
    private readonly ClientReliableEventFrameSpanApplier? _applyEventSpan;
    private readonly Dictionary<ulong, ImmutablePayloadBuffer> _pendingBySequence;
    private readonly int _maxBufferedEvents;
    private ulong _nextExpectedSequence;
    private ulong _lastAppliedSequence;
    private ulong _pendingAckSequence;

    public ClientObserverEventBridge(
        ConnectionId connectionId,
        ClientReliableEventFrameApplier? applyEvent = null,
        int maxBufferedEvents = 64)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (maxBufferedEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBufferedEvents));

        _connectionId = connectionId;
        _applyEvent = applyEvent;
        _applyEventSpan = null;
        _maxBufferedEvents = maxBufferedEvents;
        _pendingBySequence = new Dictionary<ulong, ImmutablePayloadBuffer>(maxBufferedEvents);
    }

    private ClientObserverEventBridge(
        ConnectionId connectionId,
        ClientReliableEventFrameSpanApplier applyEventSpan,
        int maxBufferedEvents)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (maxBufferedEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBufferedEvents));

        _connectionId = connectionId;
        _applyEvent = null;
        _applyEventSpan = applyEventSpan ?? throw new ArgumentNullException(nameof(applyEventSpan));
        _maxBufferedEvents = maxBufferedEvents;
        _pendingBySequence = new Dictionary<ulong, ImmutablePayloadBuffer>(maxBufferedEvents);
    }

    public static ClientObserverEventBridge CreateSpanFastPath(
        ConnectionId connectionId,
        ClientReliableEventFrameSpanApplier applyEventSpan,
        int maxBufferedEvents = 64)
    {
        return new ClientObserverEventBridge(connectionId, applyEventSpan, maxBufferedEvents);
    }

    public ConnectionId ConnectionId => _connectionId;
    public int LastReceivedEventCount { get; private set; }
    public int LastAppliedEventCount { get; private set; }
    public int BufferedEventCount => _pendingBySequence.Count;
    public ulong LastAppliedSequence => _lastAppliedSequence;
    public ulong LastAcknowledgedSequence => _pendingAckSequence;
    public long DroppedBufferedEventCount { get; private set; }
    public ClientChatEventBridge? ChatEvents { get; set; }
    public ClientUiEventBridge? UiEvents { get; set; }
    public bool AllowSequenceGaps { get; set; }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        BeginPump();

        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId != _connectionId || datagram.Plane != NetworkPlane.State)
                continue;

            if (!TryProcessEnvelope(datagram.Header, datagram.EnvelopePayloadSpan))
                continue;

            processed++;
        }

        return processed;
    }

    public void BeginPump()
    {
        LastReceivedEventCount = 0;
        LastAppliedEventCount = 0;
    }

    public bool TryProcessEnvelope(in PacketEnvelopeHeader header, ReadOnlySpan<byte> payload)
    {
        if (header.PacketId != ReliableEventFramePacket.PacketId)
            return false;

        var reader = new BitReader(payload);
        ulong sequence = reader.ReadUInt64();
        ushort payloadLength = reader.ReadUInt16();
        ReadOnlySpan<byte> eventPayload = reader.ReadBytes(payloadLength);
        OnReliableEventFrame(sequence, eventPayload);
        return true;
    }

    private void OnReliableEventFrame(ulong sequence, ReadOnlySpan<byte> payload)
    {
        LastReceivedEventCount++;

        if (_nextExpectedSequence == 0)
        {
            Apply(sequence, payload);
            _nextExpectedSequence = sequence + 1;
            DrainBufferedContiguous();
            return;
        }

        if (sequence < _nextExpectedSequence)
            return;

        if (sequence == _nextExpectedSequence)
        {
            Apply(sequence, payload);
            _nextExpectedSequence++;
            DrainBufferedContiguous();
            return;
        }

        if (AllowSequenceGaps)
        {
            Apply(sequence, payload);
            _nextExpectedSequence = sequence + 1;
            DrainBufferedContiguous();
            return;
        }

        if (_pendingBySequence.ContainsKey(sequence))
            return;
        if (_pendingBySequence.Count >= _maxBufferedEvents)
        {
            DroppedBufferedEventCount++;
            return;
        }

        _pendingBySequence.Add(sequence, ImmutablePayloadBuffer.CopyFrom(payload));
    }

    public bool TryDequeueAckPacket(out ReliableEventAckPacket packet)
    {
        if (_pendingAckSequence == 0)
        {
            packet = default;
            return false;
        }

        packet = new ReliableEventAckPacket(_pendingAckSequence);
        _pendingAckSequence = 0;
        return true;
    }

    public void ResetToSequence(ulong sequence)
    {
        _pendingBySequence.Clear();
        _lastAppliedSequence = sequence;
        _nextExpectedSequence = sequence + 1;
        _pendingAckSequence = 0;
    }

    private void DrainBufferedContiguous()
    {
        while (_pendingBySequence.Remove(_nextExpectedSequence, out ImmutablePayloadBuffer? payload))
        {
            Apply(_nextExpectedSequence, payload);
            _nextExpectedSequence++;
        }
    }

    private void Apply(ulong sequence, ReadOnlySpan<byte> payload)
    {
        _lastAppliedSequence = sequence;
        _pendingAckSequence = sequence;
        if (ChatEvents?.TryProcessEventPayload(payload) == true)
        {
            LastAppliedEventCount++;
            return;
        }

        if (UiEvents?.TryProcessEventPayload(payload) == true)
        {
            LastAppliedEventCount++;
            return;
        }

        if (_applyEventSpan is not null)
            _applyEventSpan(sequence, payload);
        else
            _applyEvent?.Invoke(sequence, ImmutablePayloadBuffer.CopyFrom(payload));
        LastAppliedEventCount++;
    }

    private void Apply(ulong sequence, ImmutablePayloadBuffer payload)
    {
        _lastAppliedSequence = sequence;
        _pendingAckSequence = sequence;
        if (ChatEvents?.TryProcessEventPayload(payload.Span) == true)
        {
            LastAppliedEventCount++;
            return;
        }

        if (UiEvents?.TryProcessEventPayload(payload.Span) == true)
        {
            LastAppliedEventCount++;
            return;
        }

        if (_applyEventSpan is not null)
            _applyEventSpan(sequence, payload.Span);
        else
            _applyEvent?.Invoke(sequence, payload);
        LastAppliedEventCount++;
    }
}
