using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Deterministic work counts from one consumed structural barrier.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 64)]
public readonly struct StructuralPlaybackResult
{
    internal StructuralPlaybackResult(
        uint epoch,
        int commandCount,
        int createdCount,
        int destroyedCount,
        int removedCount,
        int addedCount,
        int rejectedCreateCount,
        int rejectedDestroyCount,
        int rejectedTransitionCount,
        int temporaryHoleCount,
        int reusedTemporaryHoleCount,
        int compactedTemporaryHoleCount,
        int temporaryHoleDonorRelocationCount,
        long reusedTemporaryHoleFixedRowBytes)
    {
        Epoch = epoch;
        CommandCount = commandCount;
        CreatedCount = createdCount;
        DestroyedCount = destroyedCount;
        RemovedCount = removedCount;
        AddedCount = addedCount;
        RejectedCreateCount = rejectedCreateCount;
        RejectedDestroyCount = rejectedDestroyCount;
        RejectedTransitionCount = rejectedTransitionCount;
        TemporaryHoleCount = temporaryHoleCount;
        ReusedTemporaryHoleCount = reusedTemporaryHoleCount;
        CompactedTemporaryHoleCount = compactedTemporaryHoleCount;
        TemporaryHoleDonorRelocationCount = temporaryHoleDonorRelocationCount;
        ReusedTemporaryHoleFixedRowBytes = reusedTemporaryHoleFixedRowBytes;
    }

    public uint Epoch { get; }

    public int CommandCount { get; }

    public int CreatedCount { get; }

    public int DestroyedCount { get; }

    public int RemovedCount { get; }

    public int AddedCount { get; }

    public int RejectedCreateCount { get; }

    public int RejectedDestroyCount { get; }

    public int RejectedTransitionCount { get; }

    /// <summary>Rows temporarily retained after logical destruction.</summary>
    public int TemporaryHoleCount { get; }

    /// <summary>Temporary rows filled by same-archetype creates before packing.</summary>
    public int ReusedTemporaryHoleCount { get; }

    /// <summary>Unreused temporary rows removed during mandatory finalization.</summary>
    public int CompactedTemporaryHoleCount { get; }

    /// <summary>Live donors relocated while finalizing unreused holes.</summary>
    public int TemporaryHoleDonorRelocationCount { get; }

    /// <summary>
    /// Fixed-row bytes represented by successfully reused holes. This is an
    /// exact reuse-volume metric, not a counterfactual donor-copy claim.
    /// </summary>
    public long ReusedTemporaryHoleFixedRowBytes { get; }

    public int AppliedCount => checked(CreatedCount + DestroyedCount + RemovedCount + AddedCount);

    public int RejectedCount => checked(
        RejectedCreateCount + RejectedDestroyCount + RejectedTransitionCount);
}
