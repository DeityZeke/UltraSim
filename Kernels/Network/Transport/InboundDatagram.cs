namespace UltraSim.Network;

/// <summary>
/// Inbound transport unit delivered during a poll window.
/// </summary>
public readonly record struct InboundDatagram : IDisposable
{
    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        ReadOnlyMemory<byte> payload)
        : this(connectionId, plane, packetClass, channel, DecodeHeader(payload.Span, out bool includesEnvelopeHeader), InboundPayloadBuffer.FromShared(payload), includesEnvelopeHeader)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        InboundPayloadBuffer payload)
        : this(connectionId, plane, packetClass, channel, DecodeHeader(payload.Span, out bool includesEnvelopeHeader), payload, includesEnvelopeHeader)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        ReadOnlyMemory<byte> payload)
        : this(connectionId, plane, packetClass, channel, header, InboundPayloadBuffer.FromShared(payload), includesEnvelopeHeader: true)
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        InboundPayloadBuffer payload)
        : this(connectionId, plane, packetClass, channel, header, payload, includesEnvelopeHeader: true)
    {
    }

    private InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        PacketEnvelopeHeader header,
        InboundPayloadBuffer payload,
        bool includesEnvelopeHeader)
    {
        ConnectionId = connectionId;
        Plane = plane;
        PacketClass = packetClass;
        Channel = channel;
        Header = header;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        IncludesEnvelopeHeader = includesEnvelopeHeader;
    }

    public ConnectionId ConnectionId { get; }
    public NetworkPlane Plane { get; }
    public PacketClass PacketClass { get; }
    public ChannelType Channel { get; }
    public PacketEnvelopeHeader Header { get; }
    public InboundPayloadBuffer Payload { get; }
    public bool IncludesEnvelopeHeader { get; }
    public ushort PacketId => Header.PacketId;
    public PacketHeaderFlags HeaderFlags => Header.Flags;
    public ushort PayloadLength => Header.PayloadLength;
    public ReadOnlySpan<byte> EnvelopePayloadSpan => IncludesEnvelopeHeader
        ? Payload.Span.Slice(PacketEnvelopeHeader.SizeInBytes, Header.PayloadLength)
        : Payload.Span;

    public void Dispose()
    {
        Payload.Dispose();
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
