namespace UltraSim.ECS;

/// <summary>
/// Aggregate bounded world reservation. Chunk and arena bytes are modeled as native;
/// current entity-slot metadata and the explicitly supplied scheduler reservation
/// are modeled as managed.
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

    public long TotalManagedBytes { get; }

    public long NativeChunkBytes { get; }

    public long TotalNativeBytes { get; }

    public long TotalReservedBytes { get; }

    public bool Equals(WorldCapacityPlan other) =>
        EntityCapacity == other.EntityCapacity &&
        Arenas.Equals(other.Arenas) &&
        ArchetypeCount == other.ArchetypeCount &&
        ExpectedPopulation == other.ExpectedPopulation &&
        RequiredChunkCount == other.RequiredChunkCount &&
        ManagedEntityMetadataBytes == other.ManagedEntityMetadataBytes &&
        ManagedSchedulerBytes == other.ManagedSchedulerBytes &&
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
