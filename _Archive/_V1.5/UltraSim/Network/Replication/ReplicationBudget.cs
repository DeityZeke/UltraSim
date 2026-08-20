using System;

namespace UltraSim.Network
{
    public readonly struct ReplicationBudget
    {
        public readonly int MaxBytesPerTick;
        public readonly int MaxPacketsPerTick;
        public readonly int MaxDeferredPackets;

        public ReplicationBudget(int maxBytesPerTick, int maxPacketsPerTick, int maxDeferredPackets = 1024)
        {
            if (maxBytesPerTick < 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytesPerTick));
            if (maxPacketsPerTick < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPacketsPerTick));
            if (maxDeferredPackets < 0)
                throw new ArgumentOutOfRangeException(nameof(maxDeferredPackets));

            MaxBytesPerTick = maxBytesPerTick;
            MaxPacketsPerTick = maxPacketsPerTick;
            MaxDeferredPackets = maxDeferredPackets;
        }
    }
}
