using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsChunkAoiLatestStateRule<T>
    where T : unmanaged, IComponent<T>
{
    public ServerEcsChunkAoiLatestStateRule(
        ServerEcsChunkAoiScopeClassifier scopeClassifier,
        Func<Entity, T, WorldPosition2> positionSelector,
        Func<Entity, T, ImmutablePayloadBuffer> payloadSelector,
        Func<Entity, T, ulong>? itemKeySelector = null,
        ServerEcsChunkAoiRingCadencePolicy? ringCadence = null,
        PacketClass packetClass = PacketClass.Realtime,
        bool recipientPhasing = false)
    {
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Chunk-AOI latest-state rules only support Realtime or Bulk packet classes.");

        ScopeClassifier = scopeClassifier ?? throw new ArgumentNullException(nameof(scopeClassifier));
        PositionSelector = positionSelector ?? throw new ArgumentNullException(nameof(positionSelector));
        PayloadSelector = payloadSelector ?? throw new ArgumentNullException(nameof(payloadSelector));
        ItemKeySelector = itemKeySelector ?? (static (entity, _) => ServerEcsNetworkItemKeys.GetComponentItemKey<T>(entity));
        RingCadence = ringCadence;
        PacketClass = packetClass;
        RecipientPhasing = recipientPhasing;
    }

    public ServerEcsChunkAoiScopeClassifier ScopeClassifier { get; }
    public Func<Entity, T, WorldPosition2> PositionSelector { get; }
    public Func<Entity, T, ImmutablePayloadBuffer> PayloadSelector { get; }
    public Func<Entity, T, ulong> ItemKeySelector { get; }
    public ServerEcsChunkAoiRingCadencePolicy? RingCadence { get; }
    public PacketClass PacketClass { get; }
    public bool RecipientPhasing { get; }
}
