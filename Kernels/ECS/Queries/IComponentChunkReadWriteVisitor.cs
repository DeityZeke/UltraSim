namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous visitor with one declared read column and one
/// declared write column.
/// </summary>
public interface IReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(in ReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Scheduler-aware one-read/one-write chunk job.</summary>
public interface IScheduledReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(
        in SystemJobContext context,
        in ReadWriteComponentChunk<TRead, TWrite> chunk);
}

/// <summary>Compatibility name for the pre-3.0.36 internal query fixtures.</summary>
internal interface IComponentChunkReadWriteVisitor<TRead, TWrite> :
    IReadWriteChunkJob<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
}
