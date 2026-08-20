using System;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Describes one persistent component participating in a persistence signature.
    /// Ordered by <see cref="PersistenceId"/> ascending for deterministic save output.
    /// </summary>
    public readonly record struct PersistenceComponentDescriptor(
        uint PersistenceId,
        ComponentId RuntimeComponentId,
        Type ClrType);
}
