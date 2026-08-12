namespace UltraSim.ECS;

/// <summary>
/// Preconstruction capacity estimate from caller-supplied population, arena, and
/// scheduler budgets. This does not claim to be an observed physical-world total;
/// use <see cref="WorldReservedPayloadSnapshot"/> after Seal for mechanically
/// enumerable owned payload.
/// </summary>
public readonly struct WorldCapacityPlan : IEquatable<WorldCapacityPlan>
{
    internal WorldCapacityPlan(
        EntityCapacity entityCapacity,
        WorldArenaReservations arenas,
        int archetypeCount,
        long expectedPopulation,
        long requiredChunkCount,
        long managedEntityMetadataBytes,
        long managedSchedulerBytes,
        long managedLifecycleBytes,
        long managedPersistenceIdentityBytes,
        long totalManagedBytes,
        long nativeChunkBytes,
        long totalNativeBytes,
        long totalReservedBytes)
    {
        EntityCapacity = entityCapacity;
        Arenas = arenas;
        ArchetypeCount = archetypeCount;
        ExpectedPopulation = expectedPopulation;
        RequiredChunkCount = requiredChunkCount;
        ManagedEntityMetadataBytes = managedEntityMetadataBytes;
        ManagedSchedulerBytes = managedSchedulerBytes;
        ManagedLifecycleBytes = managedLifecycleBytes;
        ManagedPersistenceIdentityBytes = managedPersistenceIdentityBytes;
        TotalManagedBytes = totalManagedBytes;
        NativeChunkBytes = nativeChunkBytes;
        TotalNativeBytes = totalNativeBytes;
        TotalReservedBytes = totalReservedBytes;
    }

    public EntityCapacity EntityCapacity { get; }

    public WorldArenaReservations Arenas { get; }

    public int ArchetypeCount { get; }

    public long ExpectedPopulation { get; }

    public long RequiredChunkCount { get; }

    /// <summary>Current generation/state/free-index array payloads; array headers are excluded.</summary>
    public long ManagedEntityMetadataBytes { get; }

    /// <summary>
    /// Explicit scheduler reservation supplied by the caller. Use the exact
    /// <c>EcsWorld.SchedulerReservedBytes</c> value after cold registration when available.
    /// </summary>
    public long ManagedSchedulerBytes { get; }

    /// <summary>Fixed lifecycle-record payload requested before construction.</summary>
    public long ManagedLifecycleBytes { get; }

    /// <summary>Fixed durable-identity side-table arrays when persistence is enabled.</summary>
    public long ManagedPersistenceIdentityBytes { get; }

    public long TotalManagedBytes { get; }

    public long NativeChunkBytes { get; }

    public long TotalNativeBytes { get; }

    public long TotalReservedBytes { get; }

    /// <summary>Unambiguous alias for the preconstruction estimated payload total.</summary>
    public long TotalPlannedPayloadBytes => TotalReservedBytes;

    public bool Equals(WorldCapacityPlan other) =>
        EntityCapacity == other.EntityCapacity &&
        Arenas.Equals(other.Arenas) &&
        ArchetypeCount == other.ArchetypeCount &&
        ExpectedPopulation == other.ExpectedPopulation &&
        RequiredChunkCount == other.RequiredChunkCount &&
        ManagedEntityMetadataBytes == other.ManagedEntityMetadataBytes &&
        ManagedSchedulerBytes == other.ManagedSchedulerBytes &&
        ManagedLifecycleBytes == other.ManagedLifecycleBytes &&
        ManagedPersistenceIdentityBytes == other.ManagedPersistenceIdentityBytes &&
        TotalManagedBytes == other.TotalManagedBytes &&
        NativeChunkBytes == other.NativeChunkBytes &&
        TotalNativeBytes == other.TotalNativeBytes &&
        TotalReservedBytes == other.TotalReservedBytes;

    public override bool Equals(object? obj) => obj is WorldCapacityPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        EntityCapacity,
        Arenas,
        ArchetypeCount,
        ExpectedPopulation,
        RequiredChunkCount,
        TotalReservedBytes);
}
