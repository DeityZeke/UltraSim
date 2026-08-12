namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous visitor with one declared read column and one
/// declared write column.
/// </summary>
/// <remarks>
/// The writable span is a dense-write contract and is conservatively published
/// as changed. Use <see cref="ITrackedReadWriteChunkJob{TRead,TWrite}"/> when only
/// a subset of visited rows mutates.
/// </remarks>
/// <example>Update Position for every entity that also has Velocity.</example>
public interface IReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(in ReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Scheduler-aware topology-selected one-read/one-write chunk job.</summary>
/// <remarks>The writable span is conservatively published as changed.</remarks>
public interface IScheduledReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(
        in SystemJobContext context,
        in ReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Allocation-free topology-selected conditional-write 1R1W chunk job.</summary>
/// <remarks>Mutate through the tracked wrapper so only touched rows are published.</remarks>
public interface ITrackedReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(in TrackedReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Scheduler-aware topology-selected conditional-write 1R1W chunk job.</summary>
/// <remarks>Mutate through the tracked wrapper so only touched rows are published.</remarks>
public interface IScheduledTrackedReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(
        in SystemJobContext context,
        in TrackedReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Compatibility name for the pre-3.0.36 internal query fixtures.</summary>
internal interface IComponentChunkReadWriteVisitor<TRead, TWrite> :
    IReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
}
