namespace UltraSim.ECS.Persistence;

/// <summary>
/// Immutable record of one type participating in persistence.
/// </summary>
public readonly record struct PersistenceTypeRecord(uint PersistenceId, PersistenceKind Kind, Type ClrType);
