namespace UltraSim.ECS;

public sealed record EntityInspectionSnapshot(
    Entity Entity,
    int ArchetypeComponentCount,
    IReadOnlyList<EntityComponentInspectionSnapshot> Components);
