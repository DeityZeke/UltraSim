using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsChunkAoiOrderedEventRule<T>
    where T : unmanaged, IComponent<T>
{
    public ServerEcsChunkAoiOrderedEventRule(
        ServerEcsChunkAoiScopeClassifier scopeClassifier,
        Func<Entity, T, WorldPosition2> positionSelector,
        Func<Entity, T, ImmutablePayloadBuffer> payloadSelector,
        PacketClass packetClass = PacketClass.ReliableEvent)
    {
        if (packetClass is not PacketClass.ReliableEvent and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Chunk-AOI ordered-event rules only support ReliableEvent or Bulk packet classes.");

        ScopeClassifier = scopeClassifier ?? throw new ArgumentNullException(nameof(scopeClassifier));
        PositionSelector = positionSelector ?? throw new ArgumentNullException(nameof(positionSelector));
        PayloadSelector = payloadSelector ?? throw new ArgumentNullException(nameof(payloadSelector));
        PacketClass = packetClass;
    }

    public ServerEcsChunkAoiScopeClassifier ScopeClassifier { get; }
    public Func<Entity, T, WorldPosition2> PositionSelector { get; }
    public Func<Entity, T, ImmutablePayloadBuffer> PayloadSelector { get; }
    public PacketClass PacketClass { get; }
}
