namespace UltraSim.ECS;

public sealed record EntityComponentInspectionSnapshot(
    ComponentId ComponentId,
    string TypeName,
    string DisplayName,
    string ValuePreview,
    IReadOnlyList<EntityComponentFieldInspectionSnapshot> Fields);
