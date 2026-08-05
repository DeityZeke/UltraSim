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

        if (!entityPool.TryGetLocation(entity, out EntityLocation sourceLocation) ||
            sourceLocation.ArchetypeId != plan.Source.Id ||
            !plan.Source.Store.Contains(sourceLocation.ChunkRow, entity.Index))
        {
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        if (!plan.Destination.Store.TryAdd(entity.Index, out ChunkRow destinationRow))
        {
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        var replacement = new EntityLocation(plan.Destination.Id, destinationRow);
        try
        {
            plan.Source.Store.CopyColumnsTo(
                sourceLocation.ChunkRow,
                plan.Destination.Store,
                destinationRow,
                plan.Transfers);
        }
        catch
        {
            RollBackDestination(plan.Destination.Store, destinationRow, entity.Index);
            throw;
        }

        if (!entityPool.TryReplaceLocation(entity, sourceLocation, replacement))
        {
            RollBackDestination(plan.Destination.Store, destinationRow, entity.Index);
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        if (!plan.Source.Store.TryRemove(
                sourceLocation.ChunkRow,
                entity.Index,
                out ChunkRowRelocation sourceRelocation))
        {
            if (!entityPool.TryReplaceLocation(entity, replacement, sourceLocation))
                ThrowMigrationInvariant("Failed to restore an entity location after source removal rejection.");
            RollBackDestination(plan.Destination.Store, destinationRow, entity.Index);
            destinationLocation = EntityLocation.Invalid;
            return false;
        }

        entityPool.ApplyRelocation(plan.Source.Id, sourceRelocation);
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
