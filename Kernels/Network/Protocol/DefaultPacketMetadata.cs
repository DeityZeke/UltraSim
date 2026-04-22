namespace UltraSim.Network;

/// <summary>
/// Default packet metadata table for the currently implemented kernel packet families.
/// </summary>
public static class DefaultPacketMetadata
{
    public static PacketMetadataRegistry Create()
    {
        var registry = new PacketMetadataRegistry();
        registry.Register<HandshakeHelloPacket>();
        registry.Register<HandshakeResultPacket>();
        registry.Register<PingProbePacket>();
        registry.Register<PongProbePacket>();
        registry.Register<RealtimeStateFramePacket>();
        registry.Register<ReliableEventFramePacket>();
        registry.Register<MovementInputPacket>();
        registry.Register<MovementStatePacket>();
        registry.Seal();
        return registry;
    }
}
