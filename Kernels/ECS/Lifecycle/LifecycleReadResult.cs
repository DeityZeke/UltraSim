namespace UltraSim.ECS;

/// <summary>Allocation-free result and lag diagnostics for one lifecycle read.</summary>
public readonly struct LifecycleReadResult
{
    internal LifecycleReadResult(
        LifecycleReadStatus status,
        int count,
        ulong remainingCount,
        ulong oldestRetainedSequence,
        ulong nextPublishedSequence)
    {
        Status = status;
        Count = count;
        RemainingCount = remainingCount;
        OldestRetainedSequence = oldestRetainedSequence;
        NextPublishedSequence = nextPublishedSequence;
    }

    /// <summary>Gets the read outcome.</summary>
    public LifecycleReadStatus Status { get; }

    /// <summary>Gets the number of records copied into the caller span.</summary>
    public int Count { get; }

    /// <summary>Gets the retained unread count after a successful partial read.</summary>
    public ulong RemainingCount { get; }

    /// <summary>Gets the oldest sequence still reconstructible from the ring.</summary>
    public ulong OldestRetainedSequence { get; }

    /// <summary>Gets the sequence that will be assigned to the next published record.</summary>
    public ulong NextPublishedSequence { get; }
}
