using System;

namespace UltraSim.Server
{
    public readonly record struct AccountPersistenceTiming(
        TimeSpan TotalDuration);
}
