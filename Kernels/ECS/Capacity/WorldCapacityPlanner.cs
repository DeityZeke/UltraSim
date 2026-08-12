namespace UltraSim.ECS;

/// <summary>Checked, allocation-free preconstruction capacity estimation.</summary>
public static class WorldCapacityPlanner
{
    public static WorldCapacityPlan Plan(
        EntityCapacity entityCapacity,
        ReadOnlySpan<ArchetypeCapacityPlan> archetypes,
        WorldArenaReservations arenas = default,
        long managedSchedulerBytes = 0,
        long managedLifecycleBytes = 0,
        long managedPersistenceIdentityBytes = 0)
    {
        if (entityCapacity.MaximumEntities <= 0)
            throw new ArgumentException("Entity capacity is invalid or default initialized.", nameof(entityCapacity));
        if (managedSchedulerBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(managedSchedulerBytes));
        if (managedLifecycleBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(managedLifecycleBytes));
        if (managedPersistenceIdentityBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(managedPersistenceIdentityBytes));

        checked
        {
            long expectedPopulation = 0;
            long requiredChunkCount = 0;
            long nativeChunkBytes = 0;
            for (int i = 0; i < archetypes.Length; i++)
            {
                ArchetypeCapacityPlan archetype = archetypes[i];
                if (!archetype.Layout.IsValid)
                    throw new ArgumentException("Every archetype capacity plan must contain a valid layout.", nameof(archetypes));

                expectedPopulation += archetype.ExpectedPopulation;
                requiredChunkCount += archetype.RequiredChunkCount;
                nativeChunkBytes += archetype.ReservedChunkBytes;
            }

            if (expectedPopulation > entityCapacity.MaximumEntities)
            {
                throw new ArgumentException(
                    $"Expected archetype population {expectedPopulation} exceeds entity capacity {entityCapacity.MaximumEntities}.",
                    nameof(archetypes));
            }

            long managedEntityBytes = entityCapacity.EstimatedMetadataBytes;
            long managedBytes = managedEntityBytes + managedSchedulerBytes + managedLifecycleBytes +
                managedPersistenceIdentityBytes;
            long nativeBytes = nativeChunkBytes + arenas.TotalBytes;
            return new WorldCapacityPlan(
                entityCapacity,
                arenas,
                archetypes.Length,
                expectedPopulation,
                requiredChunkCount,
                managedEntityBytes,
                managedSchedulerBytes,
                managedLifecycleBytes,
                managedPersistenceIdentityBytes,
                managedBytes,
                nativeChunkBytes,
                nativeBytes,
                managedBytes + nativeBytes);
        }
    }

    /// <summary>
    /// Plans a world using the lifecycle capacity carried by its construction options.
    /// Scheduler bytes remain explicit because exact scheduler reservation is known
    /// only after cold graph registration.
    /// </summary>
    public static WorldCapacityPlan PlanFromOptions(
        EcsWorldOptions options,
        ReadOnlySpan<ArchetypeCapacityPlan> archetypes,
        WorldArenaReservations arenas = default,
        long managedSchedulerBytes = 0)
    {
        if (!options.IsValid)
            throw new ArgumentException("World options are invalid or default initialized.", nameof(options));
        long lifecycleBytes = checked(
            (long)options.LifecycleRecordCapacity *
            System.Runtime.CompilerServices.Unsafe.SizeOf<LifecycleRecord>());
        long persistenceIdentityBytes = options.Persistence.IsEnabled
            ? PersistenceIdentityMap.EstimateReservedBytes(options.EntityCapacity)
            : 0;
        return Plan(
            options.EntityCapacity,
            archetypes,
            arenas,
            managedSchedulerBytes,
            lifecycleBytes,
            persistenceIdentityBytes);
    }
}
