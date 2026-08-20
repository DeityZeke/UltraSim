using System;

namespace UltraSim.Server
{
    public readonly record struct WorldPersistenceLoadTiming(
        TimeSpan TotalDuration,
        TimeSpan ManifestReadDuration,
        TimeSpan TypeValidationDuration,
        TimeSpan SystemsLoadDuration,
        TimeSpan EntityCreateDuration,
        TimeSpan HydrateDuration,
        TimeSpan FixupDuration,
        int LoadedSystemCount,
        int LoadedEntityCount,
        int MissingOptionalFileCount);
}
