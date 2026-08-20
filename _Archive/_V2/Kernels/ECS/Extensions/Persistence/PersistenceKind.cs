namespace UltraSim.ECS.Persistence;

/// <summary>
/// Classifies a type registered with the persistence type registry.
/// </summary>
public enum PersistenceKind : byte
{
    Component = 0,
    System = 1,
}
