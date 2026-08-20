namespace UltraSim.Network
{
    public readonly struct ClientCorrectionStatsSnapshot
    {
        public readonly long RollbackCount;
        public readonly long ReplayedInputCount;
        public readonly int MaxRollbackInputs;
        public readonly double TotalCorrectionMagnitude;
        public readonly long CorrectionSamples;
        public readonly long ApplyLatencyTicksSum;
        public readonly long MaxApplyLatencyTicks;
        public readonly long ApplyEvents;
        public readonly long QueuedCorrections;
        public readonly long CoalescedCorrections;
        public readonly int MaxQueuedCorrectionsPerTick;
        public readonly int MaxCoalescedCorrectionsPerTick;
        public readonly int MaxQueueDepth;

        public ClientCorrectionStatsSnapshot(
            long rollbackCount,
            long replayedInputCount,
            int maxRollbackInputs,
            double totalCorrectionMagnitude,
            long correctionSamples,
            long applyLatencyTicksSum,
            long maxApplyLatencyTicks,
            long applyEvents,
            long queuedCorrections,
            long coalescedCorrections,
            int maxQueuedCorrectionsPerTick,
            int maxCoalescedCorrectionsPerTick,
            int maxQueueDepth)
        {
            RollbackCount = rollbackCount;
            ReplayedInputCount = replayedInputCount;
            MaxRollbackInputs = maxRollbackInputs;
            TotalCorrectionMagnitude = totalCorrectionMagnitude;
            CorrectionSamples = correctionSamples;
            ApplyLatencyTicksSum = applyLatencyTicksSum;
            MaxApplyLatencyTicks = maxApplyLatencyTicks;
            ApplyEvents = applyEvents;
            QueuedCorrections = queuedCorrections;
            CoalescedCorrections = coalescedCorrections;
            MaxQueuedCorrectionsPerTick = maxQueuedCorrectionsPerTick;
            MaxCoalescedCorrectionsPerTick = maxCoalescedCorrectionsPerTick;
            MaxQueueDepth = maxQueueDepth;
        }

        public double AverageCorrectionMagnitude =>
            CorrectionSamples == 0 ? 0.0 : TotalCorrectionMagnitude / CorrectionSamples;

        public double AverageApplyLatencyTicks =>
            ApplyEvents == 0 ? 0.0 : (double)ApplyLatencyTicksSum / ApplyEvents;
    }
}
