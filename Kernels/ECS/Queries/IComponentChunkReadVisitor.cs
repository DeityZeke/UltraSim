namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous visitor for one exact-archetype component column.
/// </summary>
internal interface IComponentChunkReadVisitor<T>
    where T : unmanaged, IComponent<T>
{
    void Execute(in ReadOnlyComponentChunk<T> chunk);
}
