namespace UltraSim.Network;

public readonly record struct OutboundTransformResult(
    ImmutablePayloadBuffer Payload,
    PacketHeaderFlags HeaderFlags)
{
    public static OutboundTransformResult Unchanged(ImmutablePayloadBuffer payload, PacketHeaderFlags headerFlags) =>
        new(payload, headerFlags);
}
