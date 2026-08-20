using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerEcsNetworkScopes
{
    public static SubscriptionKey OperatorWorld { get; } = new(0x454353574F524C44UL);
    public static SubscriptionKey OperatorSystems { get; } = ServerRuntimeNetworkScopes.OperatorSystems;
    public static SubscriptionKey SimulationGlobal { get; } = new(0x53494D474C4F424CUL);
}
