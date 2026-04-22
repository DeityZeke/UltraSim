using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerRuntimeNetworkScopes
{
    public static SubscriptionKey OperatorStatus { get; } = new(0x535441545553UL);
    public static SubscriptionKey OperatorSystems { get; } = new(0x53595354454DUL);
    public static SubscriptionKey OperatorPlayers { get; } = new(0x504C41594552UL);
    public static SubscriptionKey OperatorAccounts { get; } = new(0x4143434F554E54UL);
    public static SubscriptionKey OperatorEvents { get; } = new(0x4556454E5453UL);
}
