namespace UltraSim.Network;

/// <summary>
/// Explicit hook between assembly and send for compression, segmentation,
/// framing, and future encryption.
/// </summary>
public interface IOutboundTransform
{
    OutboundTransformResult Transform(ImmutablePayloadBuffer payload, PacketHeaderFlags headerFlags, in OutboundTransformContext context);
}
