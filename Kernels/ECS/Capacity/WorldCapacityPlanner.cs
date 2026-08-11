namespace UltraSim.ECS;

/// <summary>Checked, allocation-free preconstruction capacity estimation.</summary>
public static class WorldCapacityPlanner
{
    public static WorldCapacityPlan Plan(
        EntityCapacity entityCapacity,
        ReadOnlySpan<ArchetypeCapacityPlan> archetypes,
        WorldArenaReservations arenas = default,
        long managedSchedulerBytes = 0)
    {
        if (entityCapacity.MaximumEntities <= 0)
            throw new ArgumentException("Entity capacity is invalid or default initialized.", nameof(entityCapacity));
        if (managedSchedulerBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(managedSchedulerBytes));

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
            long managedBytes = managedEntityBytes + managedSchedulerBytes;
            long nativeBytes = nativeChunkBytes + arenas.TotalBytes;
            return new WorldCapacityPlan(
                entityCapacity,
                arenas,
                archetypes.Length,
                expectedPopulation,
                requiredChunkCount,
                managedEntityBytes,
                managedSchedulerBytes,
                managedBytes,
                nativeChunkBytes,
                nativeBytes,
                managedBytes + nativeBytes);
        }
    }
}
