namespace UltraSim.ECS
{
    public readonly struct EcsOptions
    {
        public readonly bool EnableStatistics;
        public readonly bool EnableDebug;
        public readonly bool EnableDirtyTracking;
        public readonly SystemExecutionMode SystemExecutionMode;
        public readonly bool EnablePhaseStaggering;
        public readonly int MaxNonCriticalStagger;

        public EcsOptions(
            bool enableStatistics,
            bool enableDebug,
            bool enableDirtyTracking = false,
            SystemExecutionMode systemExecutionMode = SystemExecutionMode.Serial,
            bool enablePhaseStaggering = false,
            int maxNonCriticalStagger = 10)
        {
            EnableStatistics = enableStatistics;
            EnableDebug = enableDebug;
            EnableDirtyTracking = enableDirtyTracking;
            SystemExecutionMode = systemExecutionMode;
            EnablePhaseStaggering = enablePhaseStaggering;
            MaxNonCriticalStagger = maxNonCriticalStagger < 0 ? 0 : maxNonCriticalStagger;
        }
    }
}
