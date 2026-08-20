using System;

namespace UltraSim.Server
{
    public readonly record struct WorldPersistenceSaveTiming(
        TimeSpan TotalDuration,
        TimeSpan StructuralFlushDuration,
        TimeSpan TypeDatabaseWriteDuration,
        TimeSpan SystemsWriteDuration,
        TimeSpan EntitiesWriteDuration,
        TimeSpan ManifestWriteDuration,
        TimeSpan CommitDuration,
        int TypeRecordCount,
        int SystemFileCount,
        int EntityBlockCount,
        int PersistentEntityCount);
}
