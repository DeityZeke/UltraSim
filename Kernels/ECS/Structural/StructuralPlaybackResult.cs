using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Deterministic work counts from one consumed structural barrier.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 36)]
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
        int rejectedTransitionCount)
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

    public int AppliedCount => checked(CreatedCount + DestroyedCount + RemovedCount + AddedCount);

    public int RejectedCount => checked(
        RejectedCreateCount + RejectedDestroyCount + RejectedTransitionCount);
}
