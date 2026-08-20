namespace UltraSim.ECS;

/// <summary>
/// Aggregate structural-queue work counts for a single structural apply pass.
/// </summary>
public readonly record struct StructuralApplyStats(
    int CreateDefaultCount,
    int PrefabCount,
    int DestroyCount,
    int AddCount,
    int RemoveCount);

/// <summary>
/// Opt-in tick profiling snapshot for a world tick.
/// </summary>
public readonly record struct WorldProfileSnapshot(
    uint Tick,
    StructuralApplyStats PreSystems,
    StructuralApplyStats PostSystems,
    long PreSystemsElapsedTicks,
    long SystemsElapsedTicks,
    long PostSystemsElapsedTicks);
