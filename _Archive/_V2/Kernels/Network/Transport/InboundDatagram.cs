namespace UltraSim.Network;

/// <summary>
/// Inbound transport unit delivered during a poll window.
/// </summary>
public readonly record struct InboundDatagram : IDisposable
{
    private readonly InboundPayloadBuffer? _payloadOwner;
    private readonly ReadOnlyMemory<byte> _payloadMemory;
    private readonly bool _releaseRetainedSlice;

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        ReadOnlyMemory<byte> payload)
        : this(
            connectionId,
            plane,
            packetClass,
            channel,
            DecodeHeader(payload.Span, out bool includesEnvelopeHeader),
            InboundPayloadBuffer.FromShared(payload),
            payload,
            includesEnvelopeHeader,
            releaseRetainedSlice: false)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        InboundPayloadBuffer payload)
        : this(
            connectionId,
            plane,
            packetClass,
            channel,
            DecodeHeader(payload.Span, out bool includesEnvelopeHeader),
            payload,
            payload.Memory,
            includesEnvelopeHeader,
            releaseRetainedSlice: false)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        ReadOnlyMemory<byte> payload)
        : this(
            connectionId,
            plane,
            packetClass,
            channel,
            header,
            InboundPayloadBuffer.FromShared(payload),
            payload,
            includesEnvelopeHeader: true,
            releaseRetainedSlice: false)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        InboundPayloadBuffer payload)
        : this(connectionId, plane, packetClass, channel, header, payload, payload.Memory, includesEnvelopeHeader: true, releaseRetainedSlice: false)
    {
    }

    internal InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        InboundPayloadBuffer payloadOwner,
        ReadOnlyMemory<byte> payloadMemory)
        : this(connectionId, plane, packetClass, channel, header, payloadOwner, payloadMemory, includesEnvelopeHeader: true, releaseRetainedSlice: true)
    {
    }

    private InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        InboundPayloadBuffer payload,
        ReadOnlyMemory<byte> payloadMemory,
        bool includesEnvelopeHeader,
        bool releaseRetainedSlice)
    {
        ConnectionId = connectionId;
        Plane = plane;
        PacketClass = packetClass;
        Channel = channel;
        Header = header;
        _payloadOwner = payload ?? throw new ArgumentNullException(nameof(payload));
        _payloadMemory = payloadMemory;
        IncludesEnvelopeHeader = includesEnvelopeHeader;
        _releaseRetainedSlice = releaseRetainedSlice;
    }

    public ConnectionId ConnectionId { get; }
    public NetworkPlane Plane { get; }
    public PacketClass PacketClass { get; }
    public ChannelType Channel { get; }
    public PacketEnvelopeHeader Header { get; }
    public InboundPayloadBuffer Payload => _releaseRetainedSlice
        ? InboundPayloadBuffer.FromShared(_payloadMemory)
        : _payloadOwner ?? InboundPayloadBuffer.Empty;
    public bool IncludesEnvelopeHeader { get; }
    public ushort PacketId => Header.PacketId;
    public PacketHeaderFlags HeaderFlags => Header.Flags;
    public ushort PayloadLength => Header.PayloadLength;
    public ReadOnlySpan<byte> EnvelopePayloadSpan => IncludesEnvelopeHeader
        ? _payloadMemory.Span.Slice(PacketEnvelopeHeader.SizeInBytes, Header.PayloadLength)
        : _payloadMemory.Span;

    public void Dispose()
    {
        if (_releaseRetainedSlice)
        {
            _payloadOwner?.ReleaseRetainedSlice();
            return;
        }

        _payloadOwner?.Dispose();
    }

    private static PacketEnvelopeHeader DecodeHeader(ReadOnlySpan<byte> payload, out bool includesEnvelopeHeader)
    {
        if (PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out _))
        {
            includesEnvelopeHeader = true;
            return header;
        }

        includesEnvelopeHeader = false;
        return new PacketEnvelopeHeader(0, PacketHeaderFlags.None, checked((ushort)Math.Min(payload.Length, ushort.MaxValue)));
    }
}
