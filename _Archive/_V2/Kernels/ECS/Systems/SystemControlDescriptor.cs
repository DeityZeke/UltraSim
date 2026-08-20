namespace UltraSim.ECS;

/// <summary>
/// Describes a potential system-facing control for future host/operator surfaces.
/// </summary>
public readonly record struct SystemControlDescriptor(
    string Key,
    string DisplayName,
    SystemControlKind Kind,
    bool ReadOnly = false,
    string? Description = null,
    string? Group = null);
