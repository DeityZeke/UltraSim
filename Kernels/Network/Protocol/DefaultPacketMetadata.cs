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
        registry.Register<ServerDisconnectPacket>();
        registry.Register<ClientDisconnectPacket>();
        registry.Register<ChatSubmitPacket>();
        registry.Register<ChatMessageEventPacket>();
        registry.Register<UiPanelOpenEventPacket>();
        registry.Register<UiActionRequestPacket>();
        registry.Register<RealtimeStateFramePacket>();
        registry.Register<RealtimeStateBatchPacket>();
        registry.Register<ReliableEventFramePacket>();
        registry.Register<ReliableEventAckPacket>();
        registry.Register<StateBaselineBeginPacket>();
        registry.Register<StateBaselineFramePacket>();
        registry.Register<StateBaselineCompletePacket>();
        registry.Register<StateBaselineAckPacket>();
        registry.Register<StateDeltaFramePacket>();
        registry.Register<MovementInputPacket>();
        registry.Register<MovementStatePacket>();
        registry.Seal();
        return registry;
    }
}
