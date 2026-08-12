namespace UltraSim.ECS;

/// <summary>
/// Allocation-free synchronous public job over one read-only component column
/// in each prepared chunk.
/// </summary>
/// <remarks>Use when ECS composition selects the rows; execution is contiguous and lookup-free.</remarks>
public interface IReadOnlyChunkJob<T>
    where T : unmanaged, IComponent<T>
{
    void Execute(in ReadOnlyComponentChunk<T> chunk);
}

/// <summary>Compatibility name for the pre-3.0.36 internal query fixtures.</summary>
internal interface IComponentChunkReadVisitor<T> : IReadOnlyChunkJob<T>
    where T : unmanaged, IComponent<T>
{
}
