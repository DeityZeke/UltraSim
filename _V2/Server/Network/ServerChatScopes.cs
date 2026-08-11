using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerChatScopes
{
    public static SubscriptionKey System { get; } = new(0x43484154535953UL);
}
