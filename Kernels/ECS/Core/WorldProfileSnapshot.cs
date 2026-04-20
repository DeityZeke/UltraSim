namespace UltraSim.ECS;

public readonly record struct StructuralApplyStats(
    int CreateDefaultCount,
    int DestroyCount,
    int AddCount,
    int RemoveCount);

public readonly record struct WorldProfileSnapshot(
    uint Tick,
    StructuralApplyStats PreSystems,
    StructuralApplyStats PostSystems,
    long PreSystemsElapsedTicks,
    long SystemsElapsedTicks,
    long PostSystemsElapsedTicks);
