using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Allocation-free transactional execution of a precomputed archetype transition.</summary>
internal static class ArchetypeMigrator
{
    internal static bool TryMigrate<TArena>(
        EntityPool entityPool,
        Entity entity,
        ArchetypeTransitionPlan<TArena> plan,
        out EntityLocation destinationLocation)
        where TArena : class, IChunkArenaBackend
    {
        ArgumentNullException.ThrowIfNull(entityPool);
        ArgumentNullException.ThrowIfNull(plan);

        return TryMigrate(
            entityPool,
            entity,
            plan.Source,
            plan.Destination,
            plan.Transfers,
            out destinationLocation);
    }

    internal static bool TryMigrate<TArena>(
        EntityPool entityPool,
        Entity entity,
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ReadOnlySpan<ArchetypeColumnTransfer> transfers,
        out EntityLocation destinationLocation)
        where TArena : class, IChunkArenaBackend
    {
        ArgumentNullException.ThrowIfNull(entityPool);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!entityPool.TryGetLocation(entity, out EntityLocation sourceLocation) ||
            sourceLocation.ArchetypeId != source.Id ||
            !source.Store.Contains(sourceLocation.ChunkRow, entity.Index))
        {
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        if (!destination.Store.TryAdd(entity.Index, out ChunkRow destinationRow))
        {
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        var replacement = new EntityLocation(destination.Id, destinationRow);
        try
        {
            source.Store.CopyColumnsTo(
                sourceLocation.ChunkRow,
                destination.Store,
                destinationRow,
                transfers);
        }
        catch
        {
            RollBackDestination(destination.Store, destinationRow, entity.Index);
            throw;
        }

        if (!entityPool.TryReplaceLocation(entity, sourceLocation, replacement))
        {
            RollBackDestination(destination.Store, destinationRow, entity.Index);
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        if (!source.Store.TryRemove(
                sourceLocation.ChunkRow,
                entity.Index,
                out ChunkRowRelocation sourceRelocation))
        {
            if (!entityPool.TryReplaceLocation(entity, replacement, sourceLocation))
                ThrowMigrationInvariant("Failed to restore an entity location after source removal rejection.");
            RollBackDestination(destination.Store, destinationRow, entity.Index);
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        entityPool.ApplyRelocation(source.Id, sourceRelocation);
        destinationLocation = replacement;
        return true;
    }

    private static void RollBackDestination<TArena>(
        ArchetypeChunkStore<TArena> destination,
        ChunkRow destinationRow,
        uint entityIndex)
        where TArena : class, IChunkArenaBackend
    {
        if (!destination.TryRemove(destinationRow, entityIndex, out ChunkRowRelocation relocation) ||
            relocation.HasValue)
        {
            ThrowMigrationInvariant("A just-reserved destination row could not be rolled back in place.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMigrationInvariant(string message) =>
        throw new InvalidOperationException(message);
}
