using System;

namespace UltraSim.Network
{
    public readonly struct EncodedReplicationDelta
    {
        public readonly long Tick;
        public readonly NetworkEntityId EntityId;
        public readonly ushort StreamId;
        public readonly ReplicationLane Lane;
        public readonly ulong CoalesceKey;
        public readonly ulong SentKeyHash;
        public readonly ReadOnlyMemory<byte> Payload;

        public EncodedReplicationDelta(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ulong sentKeyHash,
            ReadOnlyMemory<byte> payload)
        {
            Tick = tick;
            EntityId = entityId;
            StreamId = streamId;
            Lane = lane;
            CoalesceKey = coalesceKey;
            SentKeyHash = sentKeyHash;
            Payload = payload;
        }

        public EncodedReplicationDelta(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlyMemory<byte> payload)
            : this(
                tick,
                entityId,
                streamId,
                lane,
                coalesceKey,
                sentKeyHash: 0UL,
                payload)
        {
        }
    }
}
