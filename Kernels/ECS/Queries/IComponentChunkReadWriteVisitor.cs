namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous visitor with one declared read column and one
/// declared write column.
/// </summary>
internal interface IComponentChunkReadWriteVisitor<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    void Execute(in ReadWriteComponentChunk<TRead, TWrite> chunk);
}
