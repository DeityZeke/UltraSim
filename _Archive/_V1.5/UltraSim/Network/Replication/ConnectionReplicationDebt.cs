namespace UltraSim.Network
{
    public readonly struct ConnectionReplicationDebt
    {
        public readonly int ConnectionId;
        public readonly int DeferredCritical;
        public readonly int DeferredState;
        public readonly int DeferredCosmetic;
        public readonly int CoalescedCritical;
        public readonly int CoalescedState;
        public readonly int CoalescedCosmetic;
        public readonly int DeferredTotal;
        public readonly int CoalescedTotal;
        public readonly bool HasOutstanding;
        public readonly long OldestDeferredTick;
        public readonly long OldestDeferredAgeTicks;

        public ConnectionReplicationDebt(
            int connectionId,
            int deferredCritical,
            int deferredState,
            int deferredCosmetic,
            int coalescedCritical,
            int coalescedState,
            int coalescedCosmetic,
            bool hasOutstanding,
            long oldestDeferredTick,
            long oldestDeferredAgeTicks)
        {
            ConnectionId = connectionId;
            DeferredCritical = deferredCritical;
            DeferredState = deferredState;
            DeferredCosmetic = deferredCosmetic;
            CoalescedCritical = coalescedCritical;
            CoalescedState = coalescedState;
            CoalescedCosmetic = coalescedCosmetic;
            DeferredTotal = deferredCritical + deferredState + deferredCosmetic;
            CoalescedTotal = coalescedCritical + coalescedState + coalescedCosmetic;
            HasOutstanding = hasOutstanding;
            OldestDeferredTick = oldestDeferredTick;
            OldestDeferredAgeTicks = oldestDeferredAgeTicks;
        }
    }
}
