namespace UltraSim.Network
{
    public readonly struct ReconciliationEntry<TCorrection>
    {
        public readonly ushort AuthoritativeSequence;
        public readonly long ServerTick;
        public readonly long QueuedClientTick;
        public readonly ulong MergeKey;
        public readonly bool HasMergeKey;
        public readonly TCorrection Correction;

        public ReconciliationEntry(
            ushort authoritativeSequence,
            long serverTick,
            long queuedClientTick,
            ulong mergeKey,
            bool hasMergeKey,
            in TCorrection correction)
        {
            AuthoritativeSequence = authoritativeSequence;
            ServerTick = serverTick;
            QueuedClientTick = queuedClientTick;
            MergeKey = mergeKey;
            HasMergeKey = hasMergeKey;
            Correction = correction;
        }

        public ReconciliationEntry(
            ushort authoritativeSequence,
            long serverTick,
            long queuedClientTick,
            in TCorrection correction)
            : this(authoritativeSequence, serverTick, queuedClientTick, mergeKey: 0, hasMergeKey: false, in correction)
        {
        }
    }
}
