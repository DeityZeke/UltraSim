using System.Numerics;

namespace UltraSim.Spatial;

/// <summary>Checked sizing shared by fixed-capacity spatial lookup tables.</summary>
public static class SpatialStorageCapacity
{
    /// <summary>Largest positive power of two representable by a signed array length.</summary>
    public const int MaximumPowerOfTwoCapacity = 1 << 30;

    /// <summary>
    /// Returns the smallest power of two that reserves at least two buckets per admitted entry.
    /// Unsupported owner configuration is rejected before any proportional storage is allocated.
    /// </summary>
    public static int DoubleAndRoundUpPowerOfTwo(int maximumEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        if (maximumEntries > int.MaxValue / 2)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries),
                "Twice the admitted entry capacity is not representable.");

        int required = maximumEntries * 2;
        if (required > MaximumPowerOfTwoCapacity)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries),
                "The rounded spatial lookup capacity is not representable.");

        uint capacity = BitOperations.RoundUpToPowerOf2(checked((uint)required));
        if (capacity == 0 || capacity > MaximumPowerOfTwoCapacity)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries),
                "The rounded spatial lookup capacity is not representable.");
        return checked((int)capacity);
    }
}
