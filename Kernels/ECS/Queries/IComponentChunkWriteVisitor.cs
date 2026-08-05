namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous writer for one exact-archetype component column.
/// </summary>
internal interface IComponentChunkWriteVisitor<T>
    where T : unmanaged, IComponent<T>
{
    void Execute(in WritableComponentChunk<T> chunk);
}
