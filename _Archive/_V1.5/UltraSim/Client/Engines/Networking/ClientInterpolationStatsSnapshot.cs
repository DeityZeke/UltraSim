namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientInterpolationStatsSnapshot
    {
        public readonly int TrackedEntityCount;
        public readonly int TotalBufferedSamples;
        public readonly int MaxBufferedSamples;
        public readonly int MaxTrackedEntityCount;
        public readonly int LastAppliedSamples;
        public readonly int LastPrunedSamples;
        public readonly long AppliedSamplesTotal;
        public readonly long PrunedSamplesTotal;
        public readonly int LastTrimmedSamples;
        public readonly long TrimmedSamplesTotal;
        public readonly int LastApplyAttempts;
        public readonly int LastApplyMisses;
        public readonly long ApplyAttemptsTotal;
        public readonly long ApplyMissesTotal;
        public readonly int LastStaleBufferRemovals;
        public readonly long StaleBufferRemovalsTotal;
        public readonly long LastApplyElapsedTicks;
        public readonly long MaxApplyElapsedTicks;
        public readonly long ApplyElapsedTicksTotal;
        public readonly int ApplyPassCount;

        public ClientInterpolationStatsSnapshot(
            int trackedEntityCount,
            int totalBufferedSamples,
            int maxBufferedSamples,
            int maxTrackedEntityCount,
            int lastAppliedSamples,
            int lastPrunedSamples,
            long appliedSamplesTotal,
            long prunedSamplesTotal,
            int lastTrimmedSamples,
            long trimmedSamplesTotal,
            int lastApplyAttempts,
            int lastApplyMisses,
            long applyAttemptsTotal,
            long applyMissesTotal,
            int lastStaleBufferRemovals,
            long staleBufferRemovalsTotal,
            long lastApplyElapsedTicks,
            long maxApplyElapsedTicks,
            long applyElapsedTicksTotal,
            int applyPassCount)
        {
            TrackedEntityCount = trackedEntityCount;
            TotalBufferedSamples = totalBufferedSamples;
            MaxBufferedSamples = maxBufferedSamples;
            MaxTrackedEntityCount = maxTrackedEntityCount;
            LastAppliedSamples = lastAppliedSamples;
            LastPrunedSamples = lastPrunedSamples;
            AppliedSamplesTotal = appliedSamplesTotal;
            PrunedSamplesTotal = prunedSamplesTotal;
            LastTrimmedSamples = lastTrimmedSamples;
            TrimmedSamplesTotal = trimmedSamplesTotal;
            LastApplyAttempts = lastApplyAttempts;
            LastApplyMisses = lastApplyMisses;
            ApplyAttemptsTotal = applyAttemptsTotal;
            ApplyMissesTotal = applyMissesTotal;
            LastStaleBufferRemovals = lastStaleBufferRemovals;
            StaleBufferRemovalsTotal = staleBufferRemovalsTotal;
            LastApplyElapsedTicks = lastApplyElapsedTicks;
            MaxApplyElapsedTicks = maxApplyElapsedTicks;
            ApplyElapsedTicksTotal = applyElapsedTicksTotal;
            ApplyPassCount = applyPassCount;
        }
    }
}
