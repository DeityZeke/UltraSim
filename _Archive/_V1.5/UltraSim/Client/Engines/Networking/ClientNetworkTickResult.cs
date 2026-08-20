namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientNetworkTickResult
    {
        public readonly uint WorldTickBefore;
        public readonly uint WorldTickAfter;
        public readonly int DecodedDatagrams;
        public readonly int QueuedCorrections;
        public readonly int CoalescedCorrections;
        public readonly int AppliedCorrections;
        public readonly int QueueDepthAfterQueue;
        public readonly int QueueDepthAfterApply;
        public readonly long PollElapsedTicks;
        public readonly long QueueElapsedTicks;
        public readonly long ApplyElapsedTicks;
        public readonly long WorldElapsedTicks;
        public readonly long FlushElapsedTicks;

        public ClientNetworkTickResult(
            uint worldTickBefore,
            uint worldTickAfter,
            int decodedDatagrams,
            int queuedCorrections,
            int coalescedCorrections,
            int appliedCorrections,
            int queueDepthAfterQueue,
            int queueDepthAfterApply,
            long pollElapsedTicks,
            long queueElapsedTicks,
            long applyElapsedTicks,
            long worldElapsedTicks,
            long flushElapsedTicks)
        {
            WorldTickBefore = worldTickBefore;
            WorldTickAfter = worldTickAfter;
            DecodedDatagrams = decodedDatagrams;
            QueuedCorrections = queuedCorrections;
            CoalescedCorrections = coalescedCorrections;
            AppliedCorrections = appliedCorrections;
            QueueDepthAfterQueue = queueDepthAfterQueue;
            QueueDepthAfterApply = queueDepthAfterApply;
            PollElapsedTicks = pollElapsedTicks;
            QueueElapsedTicks = queueElapsedTicks;
            ApplyElapsedTicks = applyElapsedTicks;
            WorldElapsedTicks = worldElapsedTicks;
            FlushElapsedTicks = flushElapsedTicks;
        }
    }
}
