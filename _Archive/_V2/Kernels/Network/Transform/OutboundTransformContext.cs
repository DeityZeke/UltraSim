namespace UltraSim.Network;

/// <summary>
/// Metadata supplied to the transform stage.
/// </summary>
public readonly record struct OutboundTransformContext(
    ConnectionId ConnectionId,
    NetworkPlane Plane,
    PacketClass PacketClass,
    ChannelType Channel,
    bool AllowCompression,
    bool AllowSegmentation);
