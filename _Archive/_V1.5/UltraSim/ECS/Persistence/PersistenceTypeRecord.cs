using System;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Immutable record of a type registered with the persistence system.
    /// <see cref="PersistenceId"/> is the stable on-disk identity declared via
    /// <see cref="PersistenceIdAttribute"/> — it must never change once assigned,
    /// regardless of renames or namespace moves.
    /// </summary>
    public readonly record struct PersistenceTypeRecord(uint PersistenceId, PersistenceKind Kind, Type ClrType);
}
