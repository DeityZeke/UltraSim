using System;

namespace UltraSim.Network
{
    public readonly struct ReplicationPacket
    {
        public readonly int ConnectionId;
        public readonly long Tick;
        public readonly NetworkEntityId EntityId;
        public readonly ushort StreamId;
        public readonly ReplicationLane Lane;
        public readonly ulong CoalesceKey;
        public readonly ReadOnlyMemory<byte> Payload;

        public ReplicationPacket(
            int connectionId,
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlyMemory<byte> payload)
        {
            ConnectionId = connectionId;
            Tick = tick;
            EntityId = entityId;
            StreamId = streamId;
            Lane = lane;
            CoalesceKey = coalesceKey;
            Payload = payload;
        }
    }
}
