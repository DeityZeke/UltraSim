namespace UltraSim.ECS.Persistence;

/// <summary>
/// Describes one persistent component participating in a persistence signature.
/// </summary>
public readonly record struct PersistenceComponentDescriptor(
    uint PersistenceId,
    ComponentId RuntimeComponentId,
    Type ClrType);
