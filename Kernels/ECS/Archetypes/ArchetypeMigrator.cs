using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Allocation-free transactional execution of a precomputed archetype transition.</summary>
internal static class ArchetypeMigrator
{
    internal const int MaximumBatchSize = 256;

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

    /// <summary>
    /// Migrates one record-ordered, same-route block. Invalid, wrong-source, and
    /// duplicate entities are rejected in place; destination capacity accepts a
    /// prefix exactly as repeated scalar migration would. Destination rows are
    /// default-initialized and reserved in ranges, while source removal retains
    /// the scalar packed-donor order.
    /// </summary>
    internal static int MigrateBatch<TArena>(
        EntityPool entityPool,
        ReadOnlySpan<Entity> entities,
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ReadOnlySpan<ArchetypeColumnTransfer> transfers,
        out bool capacityExhausted)
        where TArena : class, IChunkArenaBackend
    {
        ArgumentNullException.ThrowIfNull(entityPool);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (entities.Length > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(entities));
        if (entities.IsEmpty)
        {
            capacityExhausted = false;
            return 0;
        }

        Span<ChunkRow> destinationRows = stackalloc ChunkRow[MaximumBatchSize];
        int reserved = destination.Store.ReserveDefault(
            entities.Length,
            destinationRows[..entities.Length]);
        if (reserved == 0)
        {
            capacityExhausted = true;
            return 0;
        }

        capacityExhausted = false;
        int migrated = 0;
        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            if (!entityPool.TryGetLocation(entity, out EntityLocation sourceLocation) ||
                sourceLocation.ArchetypeId != source.Id)
            {
                continue;
            }

            if (migrated == reserved)
            {
                capacityExhausted = true;
                break;
            }

            ChunkRow destinationRow = destinationRows[migrated];
            var replacement = new EntityLocation(destination.Id, destinationRow);
            try
            {
                source.Store.CopyColumnsToReserved(
                    sourceLocation.ChunkRow,
                    destination.Store,
                    destinationRow,
                    entity.Index,
                    transfers);
            }
            catch
            {
                destination.Store.SetReservedEntityIndex(destinationRow, 0);
                RollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    migrated,
                    reserved);
                throw;
            }

            if (!entityPool.TryReplaceLocation(entity, sourceLocation, replacement))
            {
                destination.Store.SetReservedEntityIndex(destinationRow, 0);
                RollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    migrated,
                    reserved);
                ThrowMigrationInvariant(
                    "A validated batch entity rejected destination publication.");
            }

            if (!source.Store.TryRemove(
                    sourceLocation.ChunkRow,
                    entity.Index,
                    out ChunkRowRelocation sourceRelocation))
            {
                if (!entityPool.TryReplaceLocation(entity, replacement, sourceLocation))
                {
                    ThrowMigrationInvariant(
                        "A batch entity location could not be restored after source removal rejection.");
                }
                destination.Store.SetReservedEntityIndex(destinationRow, 0);
                RollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    migrated,
                    reserved);
                ThrowMigrationInvariant(
                    "A validated batch source row rejected removal.");
            }

            entityPool.ApplyRelocation(source.Id, sourceRelocation);
            migrated++;
        }

        RollBackAnonymousDestinationTail(
            destination.Store,
            destinationRows,
            migrated,
            reserved);
        return migrated;
    }

    private static void RollBackAnonymousDestinationTail<TArena>(
        ArchetypeChunkStore<TArena> destination,
        ReadOnlySpan<ChunkRow> rows,
        int first,
        int end)
        where TArena : class, IChunkArenaBackend
    {
        for (int index = end - 1; index >= first; index--)
        {
            if (!destination.TryRemove(
                    rows[index],
                    expectedEntityIndex: 0,
                    out ChunkRowRelocation relocation) ||
                relocation.HasValue)
            {
                ThrowMigrationInvariant(
                    "A reserved batch destination tail could not be rolled back in place.");
            }
        }
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
