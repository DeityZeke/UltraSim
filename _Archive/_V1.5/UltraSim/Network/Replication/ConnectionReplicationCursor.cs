namespace UltraSim.Network
{
    public readonly struct ConnectionReplicationCursor
    {
        public readonly long LastAckedTick;
        public readonly long LastBuiltTick;
        public readonly int DeferredCount;
        public readonly int CoalescedDeferredCount;

        public ConnectionReplicationCursor(
            long lastAckedTick,
            long lastBuiltTick,
            int deferredCount,
            int coalescedDeferredCount)
        {
            LastAckedTick = lastAckedTick;
            LastBuiltTick = lastBuiltTick;
            DeferredCount = deferredCount;
            CoalescedDeferredCount = coalescedDeferredCount;
        }
    }
}
