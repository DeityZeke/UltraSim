using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public delegate bool ServerReplicationCandidateFactory(
        World world,
        in Entity entity,
        long tick,
        ServerNetworkEntityMap entityMap,
        out ReplicationCandidate candidate);

    public delegate void ServerReplicationPacketWriter(
        INetworkTransport transport,
        in ReplicationPacket packet);

    public delegate void ServerReplicationPacketBatchWriter(
        INetworkTransport transport,
        ReplicationPacket[] packets,
        int count);
}
