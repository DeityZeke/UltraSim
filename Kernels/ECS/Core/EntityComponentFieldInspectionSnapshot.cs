namespace UltraSim.ECS;

public sealed record EntityComponentFieldInspectionSnapshot(
    string Name,
    string TypeName,
    string Value);
