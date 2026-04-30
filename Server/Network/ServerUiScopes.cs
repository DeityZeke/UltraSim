using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerUiScopes
{
    private const ulong ConnectionScopePrefix = 0x5549_0000_0000_0000UL;

    public static SubscriptionKey ForConnection(ConnectionId connectionId)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        return new SubscriptionKey(ConnectionScopePrefix | (uint)connectionId.Value);
    }
}
