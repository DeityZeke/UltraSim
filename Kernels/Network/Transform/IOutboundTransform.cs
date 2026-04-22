namespace UltraSim.Network;

/// <summary>
/// Explicit hook between assembly and send for compression, segmentation,
/// framing, and future encryption.
/// </summary>
public interface IOutboundTransform
{
    ImmutablePayloadBuffer Transform(ImmutablePayloadBuffer payload, in OutboundTransformContext context);
}
