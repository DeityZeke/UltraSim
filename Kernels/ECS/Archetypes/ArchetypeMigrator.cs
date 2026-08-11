using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Allocation-free transactional execution of a precomputed archetype transition.</summary>
internal static class ArchetypeMigrator
{
    internal const int MaximumBatchSize = 256;
    internal const int MinimumPlannedCompactionCount = 32;

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

        Span<ChunkRow> sourceRows = stackalloc ChunkRow[MaximumBatchSize];
        if (TryPrepareContiguousSourcePrefix(
                entityPool,
                entities[..reserved],
                source.Id,
                sourceRows[..reserved]))
        {
            capacityExhausted = reserved < entities.Length;
            return MigratePreparedContiguousPrefix(
                entityPool,
                entities[..reserved],
                source,
                destination,
                sourceRows[..reserved],
                destinationRows[..reserved],
                transfers);
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

    /// <summary>
    /// Streams one validated block of an exact whole-source route into its
    /// destination without removing source rows. The route coordinator clears
    /// the complete source store once every block has published in record
    /// order. This keeps scratch bounded while removing the 256-row compaction
    /// horizon from whole-archetype transitions.
    /// </summary>
    internal static void MigratePrevalidatedWholeSourceBlock<TArena>(
        EntityPool entityPool,
        ReadOnlySpan<Entity> entities,
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ReadOnlySpan<ArchetypeColumnTransfer> transfers)
        where TArena : class, IChunkArenaBackend
    {
        ArgumentNullException.ThrowIfNull(entityPool);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (entities.IsEmpty || entities.Length > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(entities));

        Span<ChunkRow> sourceRows = stackalloc ChunkRow[MaximumBatchSize];
        entityPool.CopyValidatedLocatedRows(
            entities,
            source.Id,
            sourceRows[..entities.Length]);

        Span<ChunkRow> destinationRows = stackalloc ChunkRow[MaximumBatchSize];
        int reserved = destination.Store.ReserveDefault(
            entities.Length,
            destinationRows[..entities.Length]);
        if (reserved != entities.Length)
        {
            RollBackAnonymousDestinationTail(
                destination.Store,
                destinationRows,
                first: 0,
                end: reserved);
            ThrowMigrationInvariant(
                "A capacity-preflighted whole-source transition could not reserve its destination block.");
        }

        try
        {
            source.Store.CopyColumnsToReservedRuns(
                sourceRows[..entities.Length],
                destination.Store,
                destinationRows[..entities.Length],
                entities,
                transfers);
        }
        catch
        {
            ClearAndRollBackAnonymousDestinationTail(
                destination.Store,
                destinationRows[..entities.Length],
                first: 0);
            throw;
        }

        entityPool.PublishValidatedLocationReplacements(
            entities,
            source.Id,
            sourceRows[..entities.Length],
            destination.Id,
            destinationRows[..entities.Length]);
    }

    private static bool TryPrepareContiguousSourcePrefix(
        EntityPool entityPool,
        ReadOnlySpan<Entity> entities,
        ArchetypeId sourceArchetypeId,
        Span<ChunkRow> sourceRows)
    {
        int chunkDirection = 0;
        for (int index = 0; index < entities.Length; index++)
        {
            if (!entityPool.TryGetLocation(entities[index], out EntityLocation location) ||
                location.ArchetypeId != sourceArchetypeId)
            {
                return false;
            }

            ChunkRow row = location.ChunkRow;
            sourceRows[index] = row;
            if (index == 0)
                continue;

            ChunkRow previous = sourceRows[index - 1];
            if (row.ChunkIndex == previous.ChunkIndex)
            {
                if (row.Row != previous.Row + 1)
                    return false;
                continue;
            }

            // A new packed run begins at row zero. Requiring one strict chunk-
            // index direction proves that a prior chunk cannot be revisited,
            // without a per-block hash table or quadratic scan. Retained-empty
            // reuse may produce either ascending or descending runs.
            if (row.Row != 0)
                return false;
            int direction = row.ChunkIndex > previous.ChunkIndex ? 1 : -1;
            if (chunkDirection == 0)
            {
                chunkDirection = direction;
            }
            else if (direction != chunkDirection)
            {
                return false;
            }
        }

        return true;
    }

    private static int MigratePreparedContiguousPrefix<TArena>(
        EntityPool entityPool,
        ReadOnlySpan<Entity> entities,
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ReadOnlySpan<ChunkRow> sourceRows,
        ReadOnlySpan<ChunkRow> destinationRows,
        ReadOnlySpan<ArchetypeColumnTransfer> transfers)
        where TArena : class, IChunkArenaBackend
    {
        try
        {
            source.Store.CopyColumnsToReservedRuns(
                sourceRows,
                destination.Store,
                destinationRows,
                entities,
                transfers);
        }
        catch
        {
            ClearAndRollBackAnonymousDestinationTail(
                destination.Store,
                destinationRows,
                first: 0);
            throw;
        }

        if (source.Store.ShouldUsePlannedRemoval(
                sourceRows,
                MinimumPlannedCompactionCount))
        {
            return MigratePreparedCompactedPrefix(
                entityPool,
                entities,
                source,
                destination,
                sourceRows,
                destinationRows);
        }

        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            if (!entityPool.TryGetLocation(entity, out EntityLocation sourceLocation) ||
                sourceLocation.ArchetypeId != source.Id)
            {
                ClearAndRollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    index);
                ThrowMigrationInvariant(
                    "A prepared batch entity changed source before publication.");
            }

            var replacement = new EntityLocation(destination.Id, destinationRows[index]);
            if (!entityPool.TryReplaceLocation(entity, sourceLocation, replacement))
            {
                ClearAndRollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    index);
                ThrowMigrationInvariant(
                    "A prepared batch entity rejected destination publication.");
            }

            if (!source.Store.TryRemove(
                    sourceLocation.ChunkRow,
                    entity.Index,
                    out ChunkRowRelocation sourceRelocation))
            {
                if (!entityPool.TryReplaceLocation(entity, replacement, sourceLocation))
                {
                    ThrowMigrationInvariant(
                        "A prepared batch entity location could not be restored after source removal rejection.");
                }
                ClearAndRollBackAnonymousDestinationTail(
                    destination.Store,
                    destinationRows,
                    index);
                ThrowMigrationInvariant(
                    "A prepared batch source row rejected removal.");
            }

            entityPool.ApplyRelocation(source.Id, sourceRelocation);
        }

        return entities.Length;
    }

    private static int MigratePreparedCompactedPrefix<TArena>(
        EntityPool entityPool,
        ReadOnlySpan<Entity> entities,
        ArchetypeStorage<TArena> source,
        ArchetypeStorage<TArena> destination,
        ReadOnlySpan<ChunkRow> sourceRows,
        ReadOnlySpan<ChunkRow> destinationRows)
        where TArena : class, IChunkArenaBackend
    {
        if (!entityPool.TryReplaceLocations(
                entities,
                source.Id,
                sourceRows,
                destination.Id,
                destinationRows))
        {
            ClearAndRollBackAnonymousDestinationTail(
                destination.Store,
                destinationRows,
                first: 0);
            ThrowMigrationInvariant(
                "A planned batch entity changed source before destination publication.");
        }

        Span<ChunkRow> tailRows = stackalloc ChunkRow[MaximumBatchSize];
        Span<byte> removedTail = stackalloc byte[MaximumBatchSize];
        Span<byte> tailRemoved = stackalloc byte[MaximumBatchSize];
        Span<ChunkRowRelocation> relocations =
            stackalloc ChunkRowRelocation[MaximumBatchSize];
        if (!source.Store.TryRemovePlanned(
                sourceRows,
                entities,
                tailRows,
                removedTail,
                tailRemoved,
                relocations,
                out int relocationCount))
        {
            if (!entityPool.TryReplaceLocations(
                    entities,
                    destination.Id,
                    destinationRows,
                    source.Id,
                    sourceRows))
            {
                ThrowMigrationInvariant(
                    "A planned batch location could not be restored after compaction rejection.");
            }
            ClearAndRollBackAnonymousDestinationTail(
                destination.Store,
                destinationRows,
                first: 0);
            ThrowMigrationInvariant("A planned batch source rejected compaction.");
        }

        entityPool.ApplyRelocations(source.Id, relocations[..relocationCount]);
        return entities.Length;
    }

    private static void ClearAndRollBackAnonymousDestinationTail<TArena>(
        ArchetypeChunkStore<TArena> destination,
        ReadOnlySpan<ChunkRow> rows,
        int first)
        where TArena : class, IChunkArenaBackend
    {
        destination.ClearReservedEntityIndices(rows[first..]);
        RollBackAnonymousDestinationTail(destination, rows, first, rows.Length);
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
