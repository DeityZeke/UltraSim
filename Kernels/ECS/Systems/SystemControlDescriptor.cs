namespace UltraSim.ECS;

public readonly record struct SystemControlDescriptor(
    string Key,
    string DisplayName,
    SystemControlKind Kind,
    bool ReadOnly = false,
    string? Description = null,
    string? Group = null);
