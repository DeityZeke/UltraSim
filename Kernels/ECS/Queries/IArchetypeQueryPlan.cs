namespace UltraSim.ECS;

/// <summary>Cold compilation contract for world-owned query descriptors.</summary>
internal interface IArchetypeQueryPlan<TArena>
    where TArena : class, IChunkArenaBackend
{
    void Compile(ArchetypeRegistry<TArena> registry);

    void Release();
}
