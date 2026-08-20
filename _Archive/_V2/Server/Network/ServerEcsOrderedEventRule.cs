using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsOrderedEventRule<T>
    where T : unmanaged, IComponent<T>
{
    public ServerEcsOrderedEventRule(
        Func<Entity, T, SubscriptionKey> scopeSelector,
        Func<Entity, T, ImmutablePayloadBuffer> payloadSelector,
        PacketClass packetClass = PacketClass.ReliableEvent)
    {
        if (packetClass is not PacketClass.ReliableEvent and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Ordered-event rules only support ReliableEvent or Bulk packet classes.");

        ScopeSelector = scopeSelector ?? throw new ArgumentNullException(nameof(scopeSelector));
        PayloadSelector = payloadSelector ?? throw new ArgumentNullException(nameof(payloadSelector));
        PacketClass = packetClass;
    }

    public Func<Entity, T, SubscriptionKey> ScopeSelector { get; }
    public Func<Entity, T, ImmutablePayloadBuffer> PayloadSelector { get; }
    public PacketClass PacketClass { get; }
}
