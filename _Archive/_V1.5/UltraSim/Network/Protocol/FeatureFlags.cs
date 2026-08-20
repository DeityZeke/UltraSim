using System;

namespace UltraSim.Network
{
    [Flags]
    public enum FeatureFlags : uint
    {
        None = 0,
        Compression = 1 << 0,
        Encryption = 1 << 1,
        HybridUdp = 1 << 2,
        DeltaCompression = 1 << 3,
    }
}
