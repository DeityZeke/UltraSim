using UltraSim.Network;

namespace UltraSim.Server.Network;

public readonly record struct ServerNetworkSessionRuntimeOptions(
    int MaxConnections,
    int MaxSnapshotItems,
    int MaxScheduledItems,
    int MaxReplayItems,
    int MaxDatagrams,
    int OrderedEventCapacity,
    int ReplayBufferCapacityPerConnection,
    ReliableEventRetransmitPolicy RetransmitPolicy)
{
    public static readonly ServerNetworkSessionRuntimeOptions Default = new(
        MaxConnections: 64,
        MaxSnapshotItems: 512,
        MaxScheduledItems: 256,
        MaxReplayItems: 64,
        MaxDatagrams: 1024,
        OrderedEventCapacity: 1024,
        ReplayBufferCapacityPerConnection: 256,
        RetransmitPolicy: ReliableEventRetransmitPolicy.Default);
}
