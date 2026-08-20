namespace UltraSim.Network
{
    public readonly struct ReplicationCandidate
    {
        public readonly long Tick;
        public readonly NetworkEntityId EntityId;
        public readonly ushort StreamId;
        public readonly ReplicationLane Lane;
        public readonly ulong CoalesceKey;

        public ReplicationCandidate(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey = 0)
        {
            Tick = tick;
            EntityId = entityId;
            StreamId = streamId;
            Lane = lane;
            CoalesceKey = coalesceKey == 0
                ? (((ulong)streamId << 48) ^ entityId.Value)
                : coalesceKey;
        }
    }
}
