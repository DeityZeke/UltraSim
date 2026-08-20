using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsLatestStateRule<T>
    where T : unmanaged, IComponent<T>
{
    public ServerEcsLatestStateRule(
        Func<Entity, T, SubscriptionKey> scopeSelector,
        Func<Entity, T, ImmutablePayloadBuffer> payloadSelector,
        Func<Entity, T, ulong>? itemKeySelector = null,
        PacketClass packetClass = PacketClass.Realtime)
    {
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state rules only support Realtime or Bulk packet classes.");

        ScopeSelector = scopeSelector ?? throw new ArgumentNullException(nameof(scopeSelector));
        PayloadSelector = payloadSelector ?? throw new ArgumentNullException(nameof(payloadSelector));
        ItemKeySelector = itemKeySelector ?? (static (entity, _) => ServerEcsNetworkItemKeys.GetComponentItemKey<T>(entity));
        PacketClass = packetClass;
    }

    public Func<Entity, T, SubscriptionKey> ScopeSelector { get; }
    public Func<Entity, T, ImmutablePayloadBuffer> PayloadSelector { get; }
    public Func<Entity, T, ulong> ItemKeySelector { get; }
    public PacketClass PacketClass { get; }
}
