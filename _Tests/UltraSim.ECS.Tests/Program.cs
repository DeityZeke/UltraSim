using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.ECS;

EcsPrimitiveTests.RunAll();

internal static class EcsPrimitiveTests
{
    private static int _passed;

    internal static void RunAll()
    {
        Run(nameof(WorldId_Create_ReturnsUniqueValidIds), WorldId_Create_ReturnsUniqueValidIds);
        Run(nameof(WorldId_Create_IsUniqueUnderConcurrency), WorldId_Create_IsUniqueUnderConcurrency);
        Run(nameof(Entity_Layout_IsExactlyTwelveBytes), Entity_Layout_IsExactlyTwelveBytes);
        Run(nameof(Entity_NullAndValidSemantics_AreExplicit), Entity_NullAndValidSemantics_AreExplicit);
        Run(nameof(ArchetypeId_AndEntityLocation_LayoutsAndNullSemanticsAreExplicit), ArchetypeId_AndEntityLocation_LayoutsAndNullSemanticsAreExplicit);
        Run(nameof(EntityCapacity_RejectsInvalidValues), EntityCapacity_RejectsInvalidValues);
        Run(nameof(EntityCapacity_EstimatesCurrentMetadata), EntityCapacity_EstimatesCurrentMetadata);
        Run(nameof(EntityPool_RejectsInvalidWorldAndDefaultCapacity), EntityPool_RejectsInvalidWorldAndDefaultCapacity);
        Run(nameof(EntityPool_Exhaustion_IsExplicitAndDoesNotGrow), EntityPool_Exhaustion_IsExplicitAndDoesNotGrow);
        Run(nameof(EntityPool_ReusedSlotAdvancesGenerationAndRejectsStaleHandle), EntityPool_ReusedSlotAdvancesGenerationAndRejectsStaleHandle);
        Run(nameof(EntityPool_RejectsForeignWorldBeforeSlotLookup), EntityPool_RejectsForeignWorldBeforeSlotLookup);
        Run(nameof(EntityPool_DuplicateFreeDoesNotCorruptCounts), EntityPool_DuplicateFreeDoesNotCorruptCounts);
        Run(nameof(EntityPool_BulkAndScalarOperationsHaveEquivalentState), EntityPool_BulkAndScalarOperationsHaveEquivalentState);
        Run(nameof(EntityGeneration_WrapBoundaryRetiresSlot), EntityGeneration_WrapBoundaryRetiresSlot);
        Run(nameof(EntityPool_GenerationMaximumRetiresSlotWithoutReuse), EntityPool_GenerationMaximumRetiresSlotWithoutReuse);
        Run(nameof(EntityPool_WarmedAllocateFreeCycle_AllocatesZeroManagedBytes), EntityPool_WarmedAllocateFreeCycle_AllocatesZeroManagedBytes);
        Run(nameof(EntityPool_LocationBindingRejectsStaleForeignAndOverwrite), EntityPool_LocationBindingRejectsStaleForeignAndOverwrite);
        Run(nameof(EntityPool_AppliesRowStoreSwapRelocation), EntityPool_AppliesRowStoreSwapRelocation);
        Run(nameof(EntityPool_RandomizedLocationAndRowStoreModelRemainsConsistent), EntityPool_RandomizedLocationAndRowStoreModelRemainsConsistent);
        Run(nameof(EntityPool_WarmedLocatedChurnAllocatesZeroManagedBytes), EntityPool_WarmedLocatedChurnAllocatesZeroManagedBytes);
        Run(nameof(GeneratedSchema_AssignsStableCanonicalIds), GeneratedSchema_AssignsStableCanonicalIds);
        Run(nameof(GeneratedSchema_EmitsExactLayoutFlagsAndPersistence), GeneratedSchema_EmitsExactLayoutFlagsAndPersistence);
        Run(nameof(GeneratedSchema_FingerprintAndInstancesAreStable), GeneratedSchema_FingerprintAndInstancesAreStable);
        Run(nameof(ComponentSchema_LookupsRejectMissingValues), ComponentSchema_LookupsRejectMissingValues);
        Run(nameof(ComponentSchema_ConcurrentInstancesRemainIndependent), ComponentSchema_ConcurrentInstancesRemainIndependent);
        Run(nameof(ComponentSchema_WarmedLookupsAllocateZeroManagedBytes), ComponentSchema_WarmedLookupsAllocateZeroManagedBytes);
        Run(nameof(ComponentSchema_IndexedLookupsAreCorrectAndAllocateZero), ComponentSchema_IndexedLookupsAreCorrectAndAllocateZero);
        Run(nameof(ComponentSchema_DefensiveConstructionRejectsInvalidInput), ComponentSchema_DefensiveConstructionRejectsInvalidInput);
        Run(nameof(ComponentSchema_RejectsReservedIdSpaceOverflowFirst), ComponentSchema_RejectsReservedIdSpaceOverflowFirst);
        Run(nameof(ChunkLayoutOptions_RejectInvalidAndMisalignedValues), ChunkLayoutOptions_RejectInvalidAndMisalignedValues);
        Run(nameof(ChunkLayoutPlanner_ProducesAlignedNonOverlappingLayout), ChunkLayoutPlanner_ProducesAlignedNonOverlappingLayout);
        Run(nameof(ChunkLayoutPlanner_EnableBitsetsRoundAtSixtyFourRows), ChunkLayoutPlanner_EnableBitsetsRoundAtSixtyFourRows);
        Run(nameof(ChunkLayoutPlanner_RejectsInvalidSignaturesAndInsufficientOutput), ChunkLayoutPlanner_RejectsInvalidSignaturesAndInsufficientOutput);
        Run(nameof(ChunkLayoutPlanner_CapacityIsMaximalAndOneRowFailureIsExplicit), ChunkLayoutPlanner_CapacityIsMaximalAndOneRowFailureIsExplicit);
        Run(nameof(ChunkLayoutPlanner_MatchesBruteForceRandomizedReference), ChunkLayoutPlanner_MatchesBruteForceRandomizedReference);
        Run(nameof(ChunkLayoutPlanner_LargeSignatureHeapSortIsDeterministic), ChunkLayoutPlanner_LargeSignatureHeapSortIsDeterministic);
        Run(nameof(ChunkLayoutPlanner_IsConcurrentAndWarmedAllocationFree), ChunkLayoutPlanner_IsConcurrentAndWarmedAllocationFree);
        Run(nameof(ArchetypeCapacityPlan_AccountsForPopulationBoundaries), ArchetypeCapacityPlan_AccountsForPopulationBoundaries);
        Run(nameof(WorldCapacityPlanner_AggregatesAndRejectsOvercommit), WorldCapacityPlanner_AggregatesAndRejectsOvercommit);
        Run(nameof(CapacityPlanning_CheckedArithmeticRejectsOverflow), CapacityPlanning_CheckedArithmeticRejectsOverflow);
        Run(nameof(ChunkHandle_LayoutAndNullSemanticsAreExplicit), ChunkHandle_LayoutAndNullSemanticsAreExplicit);
        Run(nameof(ChunkArenaId_Create_IsUniqueUnderConcurrency), ChunkArenaId_Create_IsUniqueUnderConcurrency);
        Run(nameof(ChunkArenaCapacity_ValidatesGeometryAndLayoutCompatibility), ChunkArenaCapacity_ValidatesGeometryAndLayoutCompatibility);
        Run(nameof(ChunkArenas_HonorMinimumAndMaximumAlignment), ChunkArenas_HonorMinimumAndMaximumAlignment);
        Run(nameof(NativeChunkArena_ExhaustionReuseClearAndForeignSafety), NativeChunkArena_ExhaustionReuseClearAndForeignSafety);
        Run(nameof(PinnedChunkArena_ExhaustionReuseClearAndForeignSafety), PinnedChunkArena_ExhaustionReuseClearAndForeignSafety);
        Run(nameof(ChunkArenas_TypedStorageAndBytesRemainEquivalent), ChunkArenas_TypedStorageAndBytesRemainEquivalent);
        Run(nameof(ChunkArenas_RejectIncompatiblePlansAndInvalidColumns), ChunkArenas_RejectIncompatiblePlansAndInvalidColumns);
        Run(nameof(ChunkArenas_DisposalAccountingAndUseAfterDispose), ChunkArenas_DisposalAccountingAndUseAfterDispose);
        Run(nameof(ChunkSlotPool_GenerationMaximumRetiresWithoutReuse), ChunkSlotPool_GenerationMaximumRetiresWithoutReuse);
        Run(nameof(ChunkSlotPool_RandomizedLifecycleMatchesSlowModel), ChunkSlotPool_RandomizedLifecycleMatchesSlowModel);
        Run(nameof(ChunkArenas_WarmedLifecycleAndTypedAccessAllocateZero), ChunkArenas_WarmedLifecycleAndTypedAccessAllocateZero);
        Run(nameof(ChunkRow_LayoutAndNullSemanticsAreExplicit), ChunkRow_LayoutAndNullSemanticsAreExplicit);
        Run(nameof(ArchetypeChunkStore_RejectsMalformedOrNonExclusiveInputs), ArchetypeChunkStore_RejectsMalformedOrNonExclusiveInputs);
        Run(nameof(ArchetypeChunkStore_BoundariesAndRetainedEmptyReuse), ArchetypeChunkStore_BoundariesAndRetainedEmptyReuse);
        Run(nameof(ArchetypeChunkStore_SwapRemovePreservesDataAndEnableBits), ArchetypeChunkStore_SwapRemovePreservesDataAndEnableBits);
        Run(nameof(ArchetypeChunkStore_BulkNativePinnedParityAndDefaultState), ArchetypeChunkStore_BulkNativePinnedParityAndDefaultState);
        Run(nameof(ArchetypeChunkStore_RandomizedNativeOperationsMatchReferenceModel), ArchetypeChunkStore_RandomizedNativeOperationsMatchReferenceModel);
        Run(nameof(ArchetypeChunkStore_RandomizedPinnedOperationsMatchReferenceModel), ArchetypeChunkStore_RandomizedPinnedOperationsMatchReferenceModel);
        Run(nameof(ArchetypeChunkStore_WarmedChurnAllocatesZero), ArchetypeChunkStore_WarmedChurnAllocatesZero);
        Run(nameof(ArchetypeChunkStore_DisposalIsOwnedAndIdempotent), ArchetypeChunkStore_DisposalIsOwnedAndIdempotent);

        Console.WriteLine($"All {_passed} ECS Next runtime/primitive tests passed.");
    }

    private static void WorldId_Create_ReturnsUniqueValidIds()
    {
        WorldId first = WorldId.Create();
        WorldId second = WorldId.Create();

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.NotEqual(first, second);
        Assert.True(second.Value > first.Value);
        Assert.False(WorldId.Invalid.IsValid);
    }

    private static void WorldId_Create_IsUniqueUnderConcurrency()
    {
        const int count = 4096;
        var values = new ConcurrentDictionary<uint, byte>(Environment.ProcessorCount, count);

        Parallel.For(0, count, _ =>
        {
            WorldId id = WorldId.Create();
            if (!values.TryAdd(id.Value, 0))
                throw new InvalidOperationException($"Duplicate world ID {id.Value}.");
        });

        Assert.Equal(count, values.Count);
    }

    private static void Entity_Layout_IsExactlyTwelveBytes()
    {
        Assert.Equal(12, Unsafe.SizeOf<Entity>());
        Assert.Equal(12, Marshal.SizeOf<Entity>());
    }

    private static void Entity_NullAndValidSemantics_AreExplicit()
    {
        Assert.False(Entity.Null.IsValid);
        Assert.Equal(WorldId.Invalid, Entity.Null.World);
        Assert.Equal(0u, Entity.Null.Index);
        Assert.Equal(0u, Entity.Null.Generation);

        WorldId worldId = WorldId.Create();
        var entity = new Entity(worldId, 0, EntityGeneration.First);
        Assert.True(entity.IsValid);
        Assert.Equal(worldId, entity.World);
    }

    private static void ArchetypeId_AndEntityLocation_LayoutsAndNullSemanticsAreExplicit()
    {
        Assert.Equal(4, Unsafe.SizeOf<ArchetypeId>());
        Assert.Equal(4, Marshal.SizeOf<ArchetypeId>());
        Assert.Equal(12, Unsafe.SizeOf<EntityLocation>());
        Assert.Equal(12, Marshal.SizeOf<EntityLocation>());
        Assert.False(ArchetypeId.Invalid.IsValid);
        Assert.False(EntityLocation.Invalid.IsValid);

        var archetypeId = new ArchetypeId(7);
        var chunkRow = new ChunkRow(3, 29);
        var location = new EntityLocation(archetypeId, chunkRow);
        Assert.True(archetypeId.IsValid);
        Assert.True(location.IsValid);
        Assert.Equal(7u, location.ArchetypeId.Value);
        Assert.Equal(chunkRow, location.ChunkRow);
        Assert.Throws<ArgumentException>(() => new EntityLocation(ArchetypeId.Invalid, chunkRow));
        Assert.Throws<ArgumentException>(() => new EntityLocation(archetypeId, ChunkRow.Invalid));
    }

    private static void EntityCapacity_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityCapacity(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityCapacity(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityCapacity(Array.MaxLength + 1));
        Assert.Equal(Array.MaxLength, new EntityCapacity(Array.MaxLength).MaximumEntities);
        Assert.Equal(Array.MaxLength, EntityCapacity.MaximumSupportedEntities);
    }

    private static void EntityCapacity_EstimatesCurrentMetadata()
    {
        var capacity = new EntityCapacity(1000);
        Assert.Equal(21000L, capacity.EstimatedMetadataBytes);
    }

    private static void EntityPool_RejectsInvalidWorldAndDefaultCapacity()
    {
        Assert.Throws<ArgumentException>(() => new EntityPool(WorldId.Invalid, new EntityCapacity(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityPool(WorldId.Create(), default));
    }

    private static void EntityPool_Exhaustion_IsExplicitAndDoesNotGrow()
    {
        var capacity = new EntityCapacity(3);
        var pool = new EntityPool(WorldId.Create(), capacity);

        Entity first = pool.Allocate();
        Entity second = pool.Allocate();
        Entity third = pool.Allocate();

        Assert.True(first.IsValid && second.IsValid && third.IsValid);
        Assert.False(pool.TryAllocate(out Entity exhausted));
        Assert.Equal(Entity.Null, exhausted);
        Assert.Throws<EntityCapacityExceededException>(() => pool.Allocate());
        Assert.Equal(3, pool.AliveCount);
        Assert.Equal(3, pool.HighWatermark);
        Assert.Equal(0, pool.AvailableCount);
        Assert.Equal(capacity, pool.Capacity);
    }

    private static void EntityPool_ReusedSlotAdvancesGenerationAndRejectsStaleHandle()
    {
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Entity stale = pool.Allocate();

        Assert.True(pool.TryFree(stale));
        Assert.False(pool.IsAlive(stale));

        Entity replacement = pool.Allocate();
        Assert.Equal(stale.World, replacement.World);
        Assert.Equal(stale.Index, replacement.Index);
        Assert.Equal(stale.Generation + 1, replacement.Generation);
        Assert.False(pool.IsAlive(stale));
        Assert.True(pool.IsAlive(replacement));
    }

    private static void EntityPool_RejectsForeignWorldBeforeSlotLookup()
    {
        var owner = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        var foreign = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Entity ownerEntity = owner.Allocate();
        Entity foreignEntity = foreign.Allocate();
        var malformedForeign = new Entity(foreign.WorldId, uint.MaxValue, ownerEntity.Generation);

        Assert.False(owner.IsAlive(foreignEntity));
        Assert.False(owner.IsAlive(malformedForeign));
        Assert.False(owner.TryFree(foreignEntity));
        Assert.True(owner.IsAlive(ownerEntity));
        Assert.Equal(1, owner.AliveCount);
    }

    private static void EntityPool_DuplicateFreeDoesNotCorruptCounts()
    {
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(2));
        Entity entity = pool.Allocate();

        Assert.True(pool.TryFree(entity));
        Assert.False(pool.TryFree(entity));
        Assert.Equal(0, pool.AliveCount);
        Assert.Equal(2, pool.AvailableCount);

        Entity first = pool.Allocate();
        Entity second = pool.Allocate();
        Assert.NotEqual(first, second);
        Assert.Equal(2, pool.AliveCount);
    }

    private static void EntityPool_BulkAndScalarOperationsHaveEquivalentState()
    {
        const int capacity = 257;
        var scalar = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
        var bulk = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
        var scalarEntities = new Entity[capacity];
        var bulkEntities = new Entity[capacity + 7];

        for (int i = 0; i < scalarEntities.Length; i++)
            scalarEntities[i] = scalar.Allocate();

        int allocated = bulk.Allocate(bulkEntities);
        Assert.Equal(capacity, allocated);
        Assert.Equal(Entity.Null, bulkEntities[capacity]);
        Assert.Equal(scalar.AliveCount, bulk.AliveCount);
        Assert.Equal(scalar.HighWatermark, bulk.HighWatermark);
        Assert.Equal(scalar.AvailableCount, bulk.AvailableCount);

        int scalarFreed = 0;
        for (int i = 0; i < scalarEntities.Length; i += 2)
        {
            if (scalar.TryFree(scalarEntities[i]))
                scalarFreed++;
        }

        var bulkVictims = new Entity[(capacity + 1) / 2];
        for (int source = 0, destination = 0; source < capacity; source += 2)
            bulkVictims[destination++] = bulkEntities[source];

        int bulkFreed = bulk.Free(bulkVictims);
        Assert.Equal(scalarFreed, bulkFreed);
        Assert.Equal(scalar.AliveCount, bulk.AliveCount);
        Assert.Equal(scalar.AvailableCount, bulk.AvailableCount);
    }

    private static void EntityGeneration_WrapBoundaryRetiresSlot()
    {
        Assert.True(EntityGeneration.TryAdvance(1, out uint second));
        Assert.Equal(2u, second);
        Assert.True(EntityGeneration.TryAdvance(uint.MaxValue - 1, out uint maximum));
        Assert.Equal(uint.MaxValue, maximum);
        Assert.False(EntityGeneration.TryAdvance(uint.MaxValue, out uint retired));
        Assert.Equal(0u, retired);
        Assert.False(EntityGeneration.TryAdvance(0, out _));
    }

    private static void EntityPool_GenerationMaximumRetiresSlotWithoutReuse()
    {
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Entity firstGeneration = pool.Allocate();

        FieldInfo generationsField = typeof(EntityPool).GetField(
            "_generations",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EntityPool generation storage was not found.");
        var generations = (uint[])(generationsField.GetValue(pool)
            ?? throw new InvalidOperationException("EntityPool generation storage was null."));
        generations[firstGeneration.Index] = uint.MaxValue;

        var maximumGeneration = new Entity(pool.WorldId, firstGeneration.Index, uint.MaxValue);
        Assert.False(pool.IsAlive(firstGeneration));
        Assert.True(pool.IsAlive(maximumGeneration));
        Assert.True(pool.TryFree(maximumGeneration));

        EntityPool.SlotSnapshot snapshot = pool.GetSlotSnapshot((int)firstGeneration.Index);
        Assert.True(snapshot.IsRetired);
        Assert.False(snapshot.IsAlive);
        Assert.Equal(uint.MaxValue, snapshot.Generation);
        Assert.Equal(1, pool.RetiredCount);
        Assert.Equal(0, pool.AvailableCount);
        Assert.False(pool.TryAllocate(out Entity unavailable));
        Assert.Equal(Entity.Null, unavailable);
    }

    private static void EntityPool_WarmedAllocateFreeCycle_AllocatesZeroManagedBytes()
    {
        const int capacity = 1024;
        const int measuredCycles = 1000;
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
        var entities = new Entity[capacity];
        Assert.Equal(capacity, pool.Allocate(entities));

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(capacity, pool.Free(entities));
            Assert.Equal(capacity, pool.Allocate(entities));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int cycle = 0; cycle < measuredCycles; cycle++)
        {
            checksum += pool.Free(entities);
            checksum += pool.Allocate(entities);
            checksum += entities[cycle & (capacity - 1)].Generation;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
        Assert.Equal(capacity, pool.AliveCount);
    }

    private static void EntityPool_LocationBindingRejectsStaleForeignAndOverwrite()
    {
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(2));
        var foreign = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Entity entity = pool.Allocate();
        Entity foreignEntity = foreign.Allocate();
        var first = new EntityLocation(new ArchetypeId(1), new ChunkRow(2, 3));
        var second = new EntityLocation(new ArchetypeId(2), new ChunkRow(4, 5));

        Assert.False(pool.TryGetLocation(entity, out EntityLocation initiallyUnbound));
        Assert.Equal(EntityLocation.Invalid, initiallyUnbound);
        Assert.True(pool.TryBindLocation(entity, first));
        Assert.Equal(1, pool.LocatedCount);
        Assert.True(pool.TryGetLocation(entity, out EntityLocation actual));
        Assert.Equal(first, actual);
        Assert.False(pool.TryBindLocation(entity, second));
        Assert.False(pool.TryBindLocation(foreignEntity, second));
        Assert.False(pool.TryUnbindLocation(foreignEntity, first));
        Assert.False(pool.TryUnbindLocation(entity, second));
        Assert.False(pool.TryFree(entity));
        Assert.True(pool.IsAlive(entity));

        Assert.True(pool.TryUnbindLocation(entity, first));
        Assert.Equal(0, pool.LocatedCount);
        Assert.False(pool.TryGetLocation(entity, out EntityLocation afterUnbind));
        Assert.Equal(EntityLocation.Invalid, afterUnbind);
        Assert.True(pool.TryFree(entity));
        Assert.False(pool.TryBindLocation(entity, first));
        Assert.False(pool.TryGetLocation(entity, out _));

        Entity replacement = pool.Allocate();
        Assert.Equal(entity.Index, replacement.Index);
        Assert.False(pool.TryGetLocation(replacement, out _));
        Assert.True(pool.TryBindLocation(replacement, second));
        Assert.True(pool.TryUnbindLocation(replacement, second));
        Assert.True(pool.TryFree(replacement));

        Assert.Throws<ArgumentException>(() => pool.TryBindLocation(foreignEntity, EntityLocation.Invalid));
        Assert.Throws<ArgumentException>(() => pool.TryUnbindLocation(foreignEntity, EntityLocation.Invalid));
    }

    private static void EntityPool_AppliesRowStoreSwapRelocation()
    {
        using ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan layout,
            out _);
        int count = layout.RowCapacity + 3;
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(count));
        var archetypeId = new ArchetypeId(1);
        var entities = new Entity[count];

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = pool.Allocate();
            Assert.True(store.TryAdd(entity.Index, out ChunkRow row));
            Assert.True(pool.TryBindLocation(entity, new EntityLocation(archetypeId, row)));
            entities[i] = entity;
        }

        Entity victim = entities[0];
        Entity donor = entities[^1];
        Assert.True(pool.TryGetLocation(victim, out EntityLocation victimLocation));
        Assert.True(pool.TryGetLocation(donor, out EntityLocation donorLocation));
        Assert.True(store.TryRemove(victimLocation.ChunkRow, victim.Index, out ChunkRowRelocation relocation));
        Assert.True(relocation.HasValue);
        Assert.Equal(donor.Index, relocation.EntityIndex);
        Assert.Equal(donorLocation.ChunkRow, relocation.Source);
        Assert.Equal(victimLocation.ChunkRow, relocation.Destination);

        Assert.Throws<InvalidOperationException>(() =>
            pool.ApplyRelocation(new ArchetypeId(2), relocation));
        Assert.True(pool.TryUnbindLocation(victim, victimLocation));
        pool.ApplyRelocation(archetypeId, relocation);
        Assert.True(pool.TryGetLocation(donor, out EntityLocation movedDonor));
        Assert.Equal(archetypeId, movedDonor.ArchetypeId);
        Assert.Equal(victimLocation.ChunkRow, movedDonor.ChunkRow);
        Assert.Equal(donor.Index, store.GetEntityIndex(movedDonor.ChunkRow));
        Assert.True(pool.TryFree(victim));
        Assert.Equal(count - 1, pool.AliveCount);
        Assert.Equal(count - 1, pool.LocatedCount);

        pool.ApplyRelocation(archetypeId, default);
        Assert.Throws<InvalidOperationException>(() =>
            pool.ApplyRelocation(archetypeId, new ChunkRowRelocation(uint.MaxValue, donorLocation.ChunkRow, victimLocation.ChunkRow)));
        store.ValidateInvariants();
    }

    private static void EntityPool_RandomizedLocationAndRowStoreModelRemainsConsistent()
    {
        const int operations = 30_000;
        using ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 3,
            out ChunkLayoutPlan layout,
            out _);
        int capacity = checked(layout.RowCapacity * 3);
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
        var archetypeId = new ArchetypeId(1);
        var live = new Entity[capacity];
        var random = new Random(unchecked((int)0xE171710C));
        int liveCount = 0;

        for (int operation = 0; operation < operations; operation++)
        {
            bool add = liveCount == 0 || (liveCount < capacity && random.Next(100) < 57);
            if (add)
            {
                Entity entity = pool.Allocate();
                Assert.True(store.TryAdd(entity.Index, out ChunkRow row));
                Assert.True(pool.TryBindLocation(entity, new EntityLocation(archetypeId, row)));
                live[liveCount++] = entity;
            }
            else
            {
                int victimIndex = random.Next(liveCount);
                Entity victim = live[victimIndex];
                Assert.True(pool.TryGetLocation(victim, out EntityLocation location));
                Assert.True(store.TryRemove(location.ChunkRow, victim.Index, out ChunkRowRelocation relocation));
                Assert.True(pool.TryUnbindLocation(victim, location));
                pool.ApplyRelocation(archetypeId, relocation);
                Assert.True(pool.TryFree(victim));
                Assert.False(pool.TryGetLocation(victim, out _));
                live[victimIndex] = live[--liveCount];
            }

            if ((operation & 255) == 0)
            {
                Assert.Equal(liveCount, pool.AliveCount);
                Assert.Equal(liveCount, pool.LocatedCount);
                Assert.Equal(liveCount, store.Count);
                for (int i = 0; i < liveCount; i++)
                {
                    Entity entity = live[i];
                    Assert.True(pool.TryGetLocation(entity, out EntityLocation location));
                    Assert.Equal(archetypeId, location.ArchetypeId);
                    Assert.Equal(entity.Index, store.GetEntityIndex(location.ChunkRow));
                }
                store.ValidateInvariants();
            }
        }

        Assert.Equal(liveCount, pool.AliveCount);
        Assert.Equal(liveCount, pool.LocatedCount);
        Assert.Equal(liveCount, store.Count);
        store.ValidateInvariants();
    }

    private static void EntityPool_WarmedLocatedChurnAllocatesZeroManagedBytes()
    {
        const int operations = 100_000;
        using ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan layout,
            out _);
        int capacity = checked(layout.RowCapacity + 17);
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
        var archetypeId = new ArchetypeId(1);
        var live = new Entity[capacity];

        for (int i = 0; i < live.Length; i++)
        {
            Entity entity = pool.Allocate();
            Assert.True(store.TryAdd(entity.Index, out ChunkRow row));
            Assert.True(pool.TryBindLocation(entity, new EntityLocation(archetypeId, row)));
            live[i] = entity;
        }

        for (int i = 0; i < 32; i++)
            LocatedChurnOnce(pool, store, archetypeId, live, i % live.Length);

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < operations; i++)
        {
            int victimIndex = i % live.Length;
            LocatedChurnOnce(pool, store, archetypeId, live, victimIndex);
            checksum += live[victimIndex].Generation;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
        Assert.Equal(capacity, pool.AliveCount);
        Assert.Equal(capacity, pool.LocatedCount);
        store.ValidateInvariants();
    }

    private static void GeneratedSchema_AssignsStableCanonicalIds()
    {
        Assert.Equal(new ComponentId(0), UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.Position>());
        Assert.Equal(new ComponentId(1), UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.Velocity>());
        Assert.Equal(new ComponentId(2), UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.DisabledTag>());
        Assert.Equal(ComponentId.Invalid, UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.NotInSchema>());

        ComponentSchema schema = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();
        Assert.True(schema.TryGetComponentId("core.position", out ComponentId position));
        Assert.True(schema.TryGetComponentId(StableHash64.Compute("core.velocity"), out ComponentId velocity));
        Assert.Equal(new ComponentId(0), position);
        Assert.Equal(new ComponentId(1), velocity);
    }

    private static void GeneratedSchema_EmitsExactLayoutFlagsAndPersistence()
    {
        Assert.Equal(2, Unsafe.SizeOf<ComponentId>());
        Assert.Equal(16, Unsafe.SizeOf<ComponentMetadata>());

        ComponentSchema schema = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();
        ref readonly ComponentMetadata position = ref schema.GetMetadata(new ComponentId(0));
        ref readonly ComponentMetadata velocity = ref schema.GetMetadata(new ComponentId(1));
        ref readonly ComponentMetadata disabled = ref schema.GetMetadata(new ComponentId(2));

        Assert.Equal(8, position.Size);
        Assert.Equal((byte)4, position.Alignment);
        Assert.Equal((ushort)1, position.Version);
        Assert.Equal(ComponentFlags.None, position.Flags);
        Assert.Equal(0u, position.PersistenceId);

        Assert.Equal(16, velocity.Size);
        Assert.Equal((byte)8, velocity.Alignment);
        Assert.Equal((ushort)2, velocity.Version);
        Assert.True(velocity.IsEnableable);
        Assert.True(velocity.IsPersistent);
        Assert.Equal(10u, velocity.PersistenceId);

        Assert.Equal(0, disabled.Size);
        Assert.Equal((byte)1, disabled.Alignment);
        Assert.True(disabled.IsTag);
        Assert.False(disabled.IsPersistent);
    }

    private static void GeneratedSchema_FingerprintAndInstancesAreStable()
    {
        SchemaId generated = UltraSim.ECS.Tests.RuntimeTestSchema.GeneratedSchemaId;
        ComponentSchema first = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();
        ComponentSchema second = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();

        Assert.True(generated.IsValid);
        Assert.Equal(generated, first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(3, first.ComponentCount);
        Assert.Equal("core.position", first.GetCanonicalKey(new ComponentId(0)));
        Assert.Equal("core.velocity", first.GetCanonicalKey(new ComponentId(1)));
        Assert.Equal("gameplay.disabled", first.GetCanonicalKey(new ComponentId(2)));
        Assert.False(ReferenceEquals(first, second));
    }

    private static void ComponentSchema_LookupsRejectMissingValues()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();

        Assert.False(schema.TryGetComponentId<UltraSim.ECS.Tests.NotInSchema>(out ComponentId missingType));
        Assert.False(schema.TryGetComponentId("missing.component", out ComponentId missingKey));
        Assert.False(schema.TryGetComponentId(0UL, out ComponentId missingHash));
        Assert.False(schema.TryGetMetadata(ComponentId.Invalid, out _));
        Assert.False(schema.TryGetMetadata(new ComponentId(100), out _));
        Assert.Equal(ComponentId.Invalid, missingType);
        Assert.Equal(ComponentId.Invalid, missingKey);
        Assert.Equal(ComponentId.Invalid, missingHash);
        Assert.Throws<ArgumentOutOfRangeException>(() => schema.GetMetadata(ComponentId.Invalid));
    }

    private static void ComponentSchema_ConcurrentInstancesRemainIndependent()
    {
        const int count = 1024;
        var ids = new ConcurrentBag<SchemaId>();
        Parallel.For(0, count, _ =>
        {
            ComponentSchema schema = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();
            Assert.True(schema.TryGetComponentId<UltraSim.ECS.Tests.Velocity>(out ComponentId velocity));
            Assert.Equal(new ComponentId(1), velocity);
            ids.Add(schema.Id);
        });

        Assert.Equal(count, ids.Count);
        foreach (SchemaId id in ids)
            Assert.Equal(UltraSim.ECS.Tests.RuntimeTestSchema.GeneratedSchemaId, id);
    }

    private static void ComponentSchema_WarmedLookupsAllocateZeroManagedBytes()
    {
        const int operations = 1_000_000;
        ComponentSchema schema = UltraSim.ECS.Tests.RuntimeTestSchema.CreateSchema();
        ComponentId velocityId = UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.Velocity>();

        _ = schema.GetMetadata(velocityId);
        _ = schema.TryGetComponentId<UltraSim.ECS.Tests.Velocity>(out _);
        _ = schema.TryGetComponentId("core.velocity", out _);
        _ = schema.TryGetComponentId(StableHash64.Compute("core.velocity"), out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            ComponentId generated = UltraSim.ECS.Tests.RuntimeTestSchema.GetComponentId<UltraSim.ECS.Tests.Velocity>();
            schema.TryGetComponentId<UltraSim.ECS.Tests.Velocity>(out ComponentId byType);
            schema.TryGetComponentId("core.velocity", out ComponentId byKey);
            schema.TryGetComponentId(StableHash64.Compute("core.velocity"), out ComponentId byHash);
            checksum += generated.Value + byType.Value + byKey.Value + byHash.Value;
            checksum += schema.GetMetadata(generated).Size;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
    }

    private static void ComponentSchema_IndexedLookupsAreCorrectAndAllocateZero()
    {
        const int operations = 1_000_000;
        ComponentSchema schema = UltraSim.ECS.Tests.IndexedTestSchema.CreateSchema();
        ulong lastHash = StableHash64.Compute("index.11");

        Assert.Equal(20, schema.ComponentCount);
        Assert.True(schema.TryGetComponentId<UltraSim.ECS.Tests.Indexed00>(out ComponentId first));
        Assert.True(schema.TryGetComponentId<UltraSim.ECS.Tests.Indexed05>(out ComponentId middle));
        Assert.True(schema.TryGetComponentId<UltraSim.ECS.Tests.Indexed11>(out ComponentId last));
        Assert.True(schema.TryGetComponentId("index.11", out ComponentId byKey));
        Assert.True(schema.TryGetComponentId(lastHash, out ComponentId byHash));
        Assert.True(schema.TryGetComponentIdByPersistenceId(105, out ComponentId byPersistence));
        Assert.False(schema.TryGetComponentIdByPersistenceId(0, out _));
        Assert.False(schema.TryGetComponentIdByPersistenceId(999, out _));
        Assert.False(schema.TryGetComponentId("index.\u2603", out _));
        Assert.Equal(new ComponentId(0), first);
        Assert.Equal(new ComponentId(5), middle);
        Assert.Equal(new ComponentId(11), last);
        Assert.Equal(last, byKey);
        Assert.Equal(last, byHash);
        Assert.Equal(middle, byPersistence);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            schema.TryGetComponentId<UltraSim.ECS.Tests.Indexed11>(out ComponentId typeId);
            schema.TryGetComponentId(lastHash, out ComponentId hashId);
            schema.TryGetComponentIdByPersistenceId(111, out ComponentId persistenceId);
            checksum += typeId.Value + hashId.Value + persistenceId.Value;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
    }

    private static void ComponentSchema_DefensiveConstructionRejectsInvalidInput()
    {
        SchemaId id = new(1, 0);
        ComponentRegistration a = ComponentRegistration.Create<UltraSim.ECS.Tests.Position>(
            new ComponentId(0),
            "a",
            StableHash64.Compute("a"),
            1,
            ComponentFlags.None,
            0);
        ComponentRegistration duplicateType = ComponentRegistration.Create<UltraSim.ECS.Tests.Position>(
            new ComponentId(1),
            "b",
            StableHash64.Compute("b"),
            1,
            ComponentFlags.None,
            0);
        ComponentRegistration persistentA = ComponentRegistration.Create<UltraSim.ECS.Tests.Position>(
            new ComponentId(0),
            "a",
            StableHash64.Compute("a"),
            1,
            ComponentFlags.Persistent,
            7);
        ComponentRegistration persistentB = ComponentRegistration.Create<UltraSim.ECS.Tests.Velocity>(
            new ComponentId(1),
            "b",
            StableHash64.Compute("b"),
            1,
            ComponentFlags.Persistent,
            7);
        ComponentRegistration badHash = ComponentRegistration.Create<UltraSim.ECS.Tests.Position>(
            new ComponentId(0),
            "a",
            123,
            1,
            ComponentFlags.None,
            0);
        ComponentRegistration unknownFlags = ComponentRegistration.Create<UltraSim.ECS.Tests.Position>(
            new ComponentId(0),
            "a",
            StableHash64.Compute("a"),
            1,
            (ComponentFlags)0x80,
            0);

        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(SchemaId.Invalid, Array.Empty<ComponentRegistration>()));
        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(id, new[] { a, duplicateType }));
        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(id, new[] { persistentA, persistentB }));
        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(id, new[] { badHash }));
        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(id, new[] { unknownFlags }));
        Assert.Throws<ArgumentException>(() => ComponentSchema.Create(id, new[] { duplicateType, a }));

        ComponentSchema empty = ComponentSchema.Create(id, Array.Empty<ComponentRegistration>());
        Assert.Equal(0, empty.ComponentCount);
    }

    private static void ComponentSchema_RejectsReservedIdSpaceOverflowFirst()
    {
        var registrations = new ComponentRegistration[ComponentSchema.MaximumComponentCount + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ComponentSchema.Create(new SchemaId(1, 0), registrations));
    }

    private static void ChunkLayoutOptions_RejectInvalidAndMisalignedValues()
    {
        Assert.False(default(ChunkLayoutOptions).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, reservedHeaderBytes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, reservedHeaderBytes: 1025));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, entityIndexSizeBytes: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, entityIndexSizeBytes: 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, minimumColumnAlignmentBytes: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(16384, minimumColumnAlignmentBytes: 8192));
        Assert.Throws<ArgumentException>(() => new ChunkLayoutOptions(1000, minimumColumnAlignmentBytes: 64));
        Assert.Throws<ArgumentException>(() => new ChunkLayoutOptions(1004, minimumColumnAlignmentBytes: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, maximumRows: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLayoutOptions(1024, maximumRows: ushort.MaxValue + 1));
        Assert.Equal(16 * 1024, ChunkLayoutOptions.KiB16.ChunkSizeBytes);
        Assert.Equal(32 * 1024, ChunkLayoutOptions.KiB32.ChunkSizeBytes);
        Assert.Equal(64 * 1024, ChunkLayoutOptions.KiB64.ChunkSizeBytes);
    }

    private static void ChunkLayoutPlanner_ProducesAlignedNonOverlappingLayout()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        var columns = new ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB16, columns);

        Assert.True(plan.IsValid);
        Assert.Equal(signature.Length, plan.SignatureComponentCount);
        Assert.Equal(4, plan.DataColumnCount);
        Assert.Equal(1, plan.TagCount);
        Assert.Equal(2, plan.EnableableCount);
        Assert.Equal(64, plan.RequiredChunkAlignmentBytes);
        Assert.Equal(0, plan.EntityIndexOffsetBytes % 64);
        Assert.Equal(plan.RowCapacity * sizeof(uint), plan.EntityIndexDataBytes);
        Assert.Equal(
            plan.Options.ChunkSizeBytes,
            plan.Options.ReservedHeaderBytes +
            plan.EntityIndexDataBytes +
            plan.ComponentDataBytes +
            plan.EnableBitsetBytes +
            plan.AlignmentPaddingBytes +
            plan.TailUnusedBytes);

        Assert.Equal(UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>(), columns[0].ComponentId);
        Assert.Equal(UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>(), columns[1].ComponentId);
        Assert.Equal(UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutInt>(), columns[2].ComponentId);
        Assert.Equal(UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutByte>(), columns[3].ComponentId);
        Assert.Equal(UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>(), columns[4].ComponentId);

        int previousEnd = plan.EntityIndexOffsetBytes + plan.EntityIndexDataBytes;
        int finalDataEnd = previousEnd;
        for (int i = 0; i < columns.Length; i++)
        {
            ComponentColumnLayout column = columns[i];
            if (column.HasData)
            {
                Assert.Equal(0, column.DataOffsetBytes % column.EffectiveAlignmentBytes);
                Assert.True(column.DataOffsetBytes >= previousEnd);
                Assert.Equal(plan.RowCapacity * column.ElementSizeBytes, column.DataBytes);
                previousEnd = column.DataOffsetBytes + column.DataBytes;
                finalDataEnd = previousEnd;
            }
            else
            {
                Assert.Equal(ComponentColumnLayout.NoOffset, column.DataOffsetBytes);
                Assert.Equal(0, column.DataBytes);
            }
        }

        int expectedEnableBytes = ChunkLayoutPlanner.CalculateEnableBitsetBytes(plan.RowCapacity, 1);
        for (int i = 0; i < columns.Length; i++)
        {
            ComponentColumnLayout column = columns[i];
            if (!column.HasEnableBits)
                continue;

            Assert.True(column.EnableBitsOffsetBytes >= finalDataEnd);
            Assert.Equal(0, column.EnableBitsOffsetBytes % sizeof(ulong));
            Assert.Equal(expectedEnableBytes, column.EnableBitsBytes);
        }
    }

    private static void ChunkLayoutPlanner_EnableBitsetsRoundAtSixtyFourRows()
    {
        Assert.Equal(0, ChunkLayoutPlanner.CalculateEnableBitsetBytes(0, 1));
        Assert.Equal(8, ChunkLayoutPlanner.CalculateEnableBitsetBytes(1, 1));
        Assert.Equal(8, ChunkLayoutPlanner.CalculateEnableBitsetBytes(63, 1));
        Assert.Equal(8, ChunkLayoutPlanner.CalculateEnableBitsetBytes(64, 1));
        Assert.Equal(16, ChunkLayoutPlanner.CalculateEnableBitsetBytes(65, 1));
        Assert.Equal(32, ChunkLayoutPlanner.CalculateEnableBitsetBytes(65, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkLayoutPlanner.CalculateEnableBitsetBytes(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkLayoutPlanner.CalculateEnableBitsetBytes(1, -1));
    }

    private static void ChunkLayoutPlanner_RejectsInvalidSignaturesAndInsufficientOutput()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId first = UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId second = UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        var columns = new ComponentColumnLayout[2];

        Assert.Throws<ArgumentException>(() => ChunkLayoutPlanner.Plan(schema, new[] { first }, default, columns));
        Assert.Throws<ArgumentException>(() => ChunkLayoutPlanner.Plan(schema, new[] { first, second }, ChunkLayoutOptions.KiB16, new ComponentColumnLayout[1]));
        Assert.Throws<ArgumentException>(() => ChunkLayoutPlanner.Plan(schema, new[] { first, first }, ChunkLayoutOptions.KiB16, columns));
        Assert.Throws<ArgumentException>(() => ChunkLayoutPlanner.Plan(schema, new[] { second, first }, ChunkLayoutOptions.KiB16, columns));
        Assert.Throws<ArgumentException>(() => ChunkLayoutPlanner.Plan(schema, new[] { ComponentId.Invalid }, ChunkLayoutOptions.KiB16, columns));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkLayoutPlanner.Plan(schema, new[] { new ComponentId(100) }, ChunkLayoutOptions.KiB16, columns));
    }

    private static void ChunkLayoutPlanner_CapacityIsMaximalAndOneRowFailureIsExplicit()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        var columns = new ComponentColumnLayout[signature.Length];
        ChunkLayoutOptions options = ChunkLayoutOptions.KiB16;
        ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(schema, signature, options, columns);

        Assert.True(plan.RowCapacity < options.MaximumRows);
        Assert.True(plan.BytesUsed <= options.ChunkSizeBytes);
        Assert.True(ChunkLayoutPlanner.CalculateRequiredBytes(options, columns, plan.RowCapacity + 1) > options.ChunkSizeBytes);

        var noPayload = new ChunkLayoutOptions(
            64,
            reservedHeaderBytes: 64,
            minimumColumnAlignmentBytes: 64);
        Assert.False(ChunkLayoutPlanner.TryPlan(schema, signature, noPayload, columns, out ChunkLayoutPlan unavailable));
        Assert.False(unavailable.IsValid);
        Assert.Throws<InvalidOperationException>(() => ChunkLayoutPlanner.Plan(schema, signature, noPayload, columns));
    }

    private static void ChunkLayoutPlanner_MatchesBruteForceRandomizedReference()
    {
        const int cases = 512;
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] all = CreateLayoutSignature();
        var signature = new ComponentId[all.Length];
        var columns = new ComponentColumnLayout[all.Length];
        var random = new Random(0x51A7C0DE);
        int[] chunkSizes = [4 * 1024, 8 * 1024, 16 * 1024, 32 * 1024, 64 * 1024];
        int[] alignments = [1, 8, 16, 64, 128];

        for (int testCase = 0; testCase < cases; testCase++)
        {
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (random.Next(2) != 0)
                    signature[count++] = all[i];
            }

            int chunkSize = chunkSizes[random.Next(chunkSizes.Length)];
            int alignment = alignments[random.Next(alignments.Length)];
            int maximumRows = random.Next(1, 1025);
            var options = new ChunkLayoutOptions(
                chunkSize,
                reservedHeaderBytes: random.Next(0, 513),
                minimumColumnAlignmentBytes: alignment,
                maximumRows: maximumRows);
            ChunkLayoutPlan actual = ChunkLayoutPlanner.Plan(
                schema,
                signature.AsSpan(0, count),
                options,
                columns.AsSpan(0, count));

            int expectedRows = 0;
            for (int rows = 1; rows <= maximumRows; rows++)
            {
                if (ReferenceRequiredBytes(options, columns.AsSpan(0, count), rows) > chunkSize)
                    break;
                expectedRows = rows;
            }

            Assert.Equal(expectedRows, actual.RowCapacity);
        }
    }

    private static void ChunkLayoutPlanner_IsConcurrentAndWarmedAllocationFree()
    {
        const int operations = 100_000;
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        var columns = new ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan expected = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);

        Parallel.For(0, 1024, _ =>
        {
            var localColumns = new ComponentColumnLayout[signature.Length];
            ChunkLayoutPlan concurrent = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, localColumns);
            Assert.Equal(expected, concurrent);
        });

        for (int i = 0; i < 8; i++)
            _ = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < operations; i++)
            checksum += ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns).RowCapacity;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
    }

    private static void ChunkLayoutPlanner_LargeSignatureHeapSortIsDeterministic()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.IndexedTestSchema.CreateSchema();
        var signature = new ComponentId[schema.ComponentCount];
        var columns = new ComponentColumnLayout[schema.ComponentCount];
        for (ushort i = 0; i < signature.Length; i++)
            signature[i] = new ComponentId(i);

        ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB16, columns);

        Assert.True(plan.IsValid);
        Assert.Equal(20, plan.DataColumnCount);
        for (ushort i = 0; i < columns.Length; i++)
            Assert.Equal(new ComponentId(i), columns[i].ComponentId);
    }

    private static void ArchetypeCapacityPlan_AccountsForPopulationBoundaries()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        var columns = new ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB16, columns);

        var empty = new ArchetypeCapacityPlan(layout, 0);
        var before = new ArchetypeCapacityPlan(layout, layout.RowCapacity - 1);
        var exact = new ArchetypeCapacityPlan(layout, layout.RowCapacity);
        var after = new ArchetypeCapacityPlan(layout, layout.RowCapacity + 1);

        Assert.Equal(0, empty.RequiredChunkCount);
        Assert.Equal(1, before.RequiredChunkCount);
        Assert.Equal(1L, before.UnusedRowSlots);
        Assert.Equal(1, exact.RequiredChunkCount);
        Assert.Equal(0L, exact.UnusedRowSlots);
        Assert.Equal(2, after.RequiredChunkCount);
        Assert.Equal((long)layout.RowCapacity - 1, after.UnusedRowSlots);
        Assert.Equal(after.UnusedRowSlots * layout.FixedRowBytes, after.PartialChunkUnusedRowBytes);
        Assert.Equal(
            after.ReservedChunkBytes,
            after.ReservedHeaderBytes +
            after.ReservedFixedRowBytes +
            after.ReservedEnableBitsetBytes +
            after.AlignmentPaddingBytes +
            after.TailUnusedBytes);
        Assert.True(after.UnusedEnableBits >= after.UnusedRowSlots * layout.EnableableCount);
    }

    private static void WorldCapacityPlanner_AggregatesAndRejectsOvercommit()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        var columns = new ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB16, columns);
        var first = new ArchetypeCapacityPlan(layout, 100);
        var second = new ArchetypeCapacityPlan(layout, 200);
        var arenas = new WorldArenaReservations(1024, 2048, 4096);
        WorldCapacityPlan world = WorldCapacityPlanner.Plan(
            new EntityCapacity(500),
            new[] { first, second },
            arenas);

        Assert.Equal(2, world.ArchetypeCount);
        Assert.Equal(300L, world.ExpectedPopulation);
        Assert.Equal((long)first.RequiredChunkCount + second.RequiredChunkCount, world.RequiredChunkCount);
        Assert.Equal(first.ReservedChunkBytes + second.ReservedChunkBytes, world.NativeChunkBytes);
        Assert.Equal(world.NativeChunkBytes + arenas.TotalBytes, world.TotalNativeBytes);
        Assert.Equal(world.ManagedEntityMetadataBytes + world.TotalNativeBytes, world.TotalReservedBytes);
        Assert.Throws<ArgumentException>(() => WorldCapacityPlanner.Plan(
            new EntityCapacity(299),
            new[] { first, second },
            arenas));
        Assert.Throws<ArgumentException>(() => WorldCapacityPlanner.Plan(
            default,
            new[] { first },
            arenas));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldArenaReservations(-1, 0, 0));
    }

    private static void CapacityPlanning_CheckedArithmeticRejectsOverflow()
    {
        Assert.Throws<OverflowException>(() => new WorldArenaReservations(long.MaxValue, 1, 0));

        int maximumAlignedChunk = int.MaxValue & ~63;
        var options = new ChunkLayoutOptions(
            maximumAlignedChunk,
            reservedHeaderBytes: 0,
            entityIndexSizeBytes: 1,
            minimumColumnAlignmentBytes: 64,
            maximumRows: 2);
        var oversized = new ComponentColumnLayout(
            new ComponentId(0),
            ComponentFlags.None,
            int.MaxValue,
            64);
        ComponentColumnLayout[] columns = [oversized];
        Assert.Equal(long.MaxValue, ChunkLayoutPlanner.CalculateRequiredBytes(options, columns, 2));
    }

    private static void ChunkHandle_LayoutAndNullSemanticsAreExplicit()
    {
        Assert.Equal(12, Unsafe.SizeOf<ChunkHandle>());
        Assert.Equal(12, Marshal.SizeOf<ChunkHandle>());
        Assert.False(ChunkHandle.Null.IsValid);
        Assert.Equal(ChunkArenaId.Invalid, ChunkHandle.Null.ArenaId);
        Assert.Equal(0u, ChunkHandle.Null.Index);
        Assert.Equal(0u, ChunkHandle.Null.Generation);
    }

    private static void ChunkArenaId_Create_IsUniqueUnderConcurrency()
    {
        const int count = 4096;
        var values = new ConcurrentDictionary<uint, byte>(Environment.ProcessorCount, count);

        Parallel.For(0, count, _ =>
        {
            ChunkArenaId id = ChunkArenaId.Create();
            if (!id.IsValid || !values.TryAdd(id.Value, 0))
                throw new InvalidOperationException($"Invalid or duplicate chunk arena ID {id.Value}.");
        });

        Assert.Equal(count, values.Count);
    }

    private static void ChunkArenaCapacity_ValidatesGeometryAndLayoutCompatibility()
    {
        Assert.False(default(ChunkArenaCapacity).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkArenaCapacity(0, 64, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkArenaCapacity(1024, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkArenaCapacity(8192, 8192, 1));
        Assert.Throws<ArgumentException>(() => new ChunkArenaCapacity(32, 64, 1));
        Assert.Throws<ArgumentException>(() => new ChunkArenaCapacity(1000, 64, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkArenaCapacity(1024, 64, 0));

        CreateLayoutStoragePlan(ChunkLayoutOptions.KiB16, out _, out ChunkLayoutPlan layout, out _);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, 17);
        Assert.True(capacity.IsValid);
        Assert.True(capacity.Supports(layout));
        Assert.Equal(16 * 1024, capacity.ChunkSizeBytes);
        Assert.Equal(64, capacity.ChunkAlignmentBytes);
        Assert.Equal(17, capacity.MaximumChunks);
        Assert.Equal(17L * 16 * 1024, capacity.TotalPayloadBytes);
        Assert.Equal(17L * 9, capacity.EstimatedSlotMetadataBytes);

        CreateLayoutStoragePlan(ChunkLayoutOptions.KiB32, out _, out ChunkLayoutPlan otherLayout, out _);
        Assert.False(capacity.Supports(otherLayout));

        var oversizedPinned = new ChunkArenaCapacity(1024 * 1024, 64, 3000);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PinnedChunkArena(oversizedPinned));
    }

    private static void ChunkArenas_HonorMinimumAndMaximumAlignment()
    {
        int[] alignments = [1, 4096];
        foreach (int alignment in alignments)
        {
            var capacity = new ChunkArenaCapacity(4096, alignment, 2);
            using var native = new NativeChunkArena(capacity);
            using var pinned = new PinnedChunkArena(capacity);

            Assert.Equal(0UL, (ulong)(native.BaseAddress % (nuint)alignment));
            Assert.Equal(0UL, (ulong)(pinned.BaseAddress % (nuint)alignment));
            for (int i = 0; i < capacity.MaximumChunks; i++)
            {
                ChunkHandle nativeChunk = native.Acquire();
                ChunkHandle pinnedChunk = pinned.Acquire();
                Assert.Equal(0UL, (ulong)((native.BaseAddress + ((nuint)nativeChunk.Index * 4096)) % (nuint)alignment));
                Assert.Equal(0UL, (ulong)((pinned.BaseAddress + ((nuint)pinnedChunk.Index * 4096)) % (nuint)alignment));
            }
        }
    }

    private static void NativeChunkArena_ExhaustionReuseClearAndForeignSafety()
    {
        var capacity = new ChunkArenaCapacity(1024, 64, 2);
        using var arena = new NativeChunkArena(capacity);
        using var foreignArena = new NativeChunkArena(new ChunkArenaCapacity(1024, 64, 1));

        Assert.Equal(0UL, (ulong)(arena.BaseAddress % 64));
        ChunkHandle first = arena.Acquire();
        ChunkHandle second = arena.Acquire();
        Assert.True(first.IsValid && second.IsValid);
        Assert.False(arena.TryAcquire(out ChunkHandle unavailable));
        Assert.Equal(ChunkHandle.Null, unavailable);
        Assert.Throws<InvalidOperationException>(() => arena.Acquire());
        Assert.Equal(0UL, (ulong)((arena.BaseAddress + ((nuint)first.Index * 1024)) % 64));
        Assert.Equal(0UL, (ulong)((arena.BaseAddress + ((nuint)second.Index * 1024)) % 64));

        arena.GetChunkBytes(first)[0] = 0xA5;
        Assert.True(arena.TryRelease(first));
        Assert.False(arena.TryRelease(first));
        Assert.False(arena.IsAlive(first));
        Assert.Throws<ArgumentException>(() => arena.GetChunkBytes(first));

        ChunkHandle replacement = arena.Acquire();
        Assert.Equal(first.Index, replacement.Index);
        Assert.Equal(first.Generation + 1, replacement.Generation);
        Assert.Equal((byte)0, arena.GetChunkBytes(replacement)[0]);

        ChunkHandle foreign = foreignArena.Acquire();
        Assert.False(arena.IsAlive(foreign));
        Assert.False(arena.TryRelease(foreign));
        Assert.Throws<ArgumentException>(() => arena.GetChunkBytes(foreign));
        var injected = new ChunkHandle(ChunkArenaId.Create(), replacement.Index, replacement.Generation);
        Assert.Throws<ArgumentException>(() => arena.GetChunkBytes(injected));

        ChunkArenaSnapshot snapshot = arena.GetSnapshot();
        Assert.Equal(ChunkArenaMemoryKind.NativeAligned, snapshot.MemoryKind);
        Assert.Equal(2, snapshot.AliveCount);
        Assert.Equal(2, snapshot.HighWatermark);
        Assert.Equal(capacity.TotalPayloadBytes, snapshot.PayloadBytesInUse);
    }

    private static void PinnedChunkArena_ExhaustionReuseClearAndForeignSafety()
    {
        var capacity = new ChunkArenaCapacity(1024, 64, 1);
        using var arena = new PinnedChunkArena(capacity);
        using var foreignArena = new PinnedChunkArena(capacity);

        Assert.Equal(0UL, (ulong)(arena.BaseAddress % 64));
        ChunkHandle original = arena.Acquire();
        arena.GetChunkBytes(original).Fill(0x7B);
        Assert.False(arena.TryAcquire(out ChunkHandle unavailable));
        Assert.Equal(ChunkHandle.Null, unavailable);
        Assert.True(arena.TryRelease(original));
        Assert.False(arena.TryRelease(original));

        ChunkHandle replacement = arena.Acquire();
        Assert.Equal(original.Index, replacement.Index);
        Assert.Equal(original.Generation + 1, replacement.Generation);
        Assert.True(IsAllZero(arena.GetChunkBytes(replacement)));

        ChunkHandle foreign = foreignArena.Acquire();
        Assert.False(arena.IsAlive(foreign));
        Assert.False(arena.TryRelease(foreign));
        Assert.Throws<ArgumentException>(() => arena.GetChunkBytes(foreign));

        ChunkArenaSnapshot snapshot = arena.GetSnapshot();
        Assert.Equal(ChunkArenaMemoryKind.PinnedManaged, snapshot.MemoryKind);
        Assert.Equal(1, snapshot.AliveCount);
        Assert.Equal(capacity.TotalPayloadBytes, snapshot.PayloadBytesInUse);
    }

    private static void ChunkArenas_TypedStorageAndBytesRemainEquivalent()
    {
        CreateLayoutStoragePlan(
            ChunkLayoutOptions.KiB16,
            out _,
            out ChunkLayoutPlan layout,
            out ComponentColumnLayout[] columns);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, 2);
        using var native = new NativeChunkArena(capacity);
        using var pinned = new PinnedChunkArena(capacity);
        ChunkHandle nativeChunk = native.Acquire();
        ChunkHandle pinnedChunk = pinned.Acquire();

        ComponentColumnLayout wideColumn = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout enableColumn = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>());
        ComponentColumnLayout tagColumn = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>());

        Span<uint> nativeEntities = native.GetEntityIndices(nativeChunk, layout);
        Span<uint> pinnedEntities = pinned.GetEntityIndices(pinnedChunk, layout);
        Span<UltraSim.ECS.Tests.LayoutWide> nativeWide = native.GetColumn<UltraSim.ECS.Tests.LayoutWide>(nativeChunk, layout, wideColumn);
        Span<UltraSim.ECS.Tests.LayoutWide> pinnedWide = pinned.GetColumn<UltraSim.ECS.Tests.LayoutWide>(pinnedChunk, layout, wideColumn);
        Span<ulong> nativeEnable = native.GetEnableWords(nativeChunk, layout, enableColumn);
        Span<ulong> pinnedEnable = pinned.GetEnableWords(pinnedChunk, layout, enableColumn);
        Span<ulong> nativeTagEnable = native.GetEnableWords(nativeChunk, layout, tagColumn);
        Span<ulong> pinnedTagEnable = pinned.GetEnableWords(pinnedChunk, layout, tagColumn);

        Assert.Equal(layout.RowCapacity, nativeEntities.Length);
        Assert.Equal(layout.RowCapacity, nativeWide.Length);
        Assert.Equal(ChunkLayoutPlanner.CalculateEnableBitsetBytes(layout.RowCapacity, 1) / sizeof(ulong), nativeEnable.Length);
        Assert.Equal(0UL, (ulong)((native.BaseAddress + ((nuint)nativeChunk.Index * (nuint)capacity.ChunkSizeBytes) + (nuint)wideColumn.DataOffsetBytes) % (nuint)wideColumn.EffectiveAlignmentBytes));
        Assert.Equal(0UL, (ulong)((pinned.BaseAddress + ((nuint)pinnedChunk.Index * (nuint)capacity.ChunkSizeBytes) + (nuint)wideColumn.DataOffsetBytes) % (nuint)wideColumn.EffectiveAlignmentBytes));

        for (int i = 0; i < layout.RowCapacity; i++)
        {
            nativeEntities[i] = pinnedEntities[i] = (uint)(i + 1);
            var value = new UltraSim.ECS.Tests.LayoutWide
            {
                A = i,
                B = i * 2L,
                C = i * 3L,
                D = i * 4L,
            };
            nativeWide[i] = pinnedWide[i] = value;
        }
        nativeEnable[0] = pinnedEnable[0] = 0x5555_5555_5555_5555UL;
        nativeTagEnable[0] = pinnedTagEnable[0] = 0xAAAA_AAAA_AAAA_AAAAUL;

        Assert.True(native.GetChunkBytes(nativeChunk).SequenceEqual(pinned.GetChunkBytes(pinnedChunk)));
        Assert.Throws<ArgumentException>(() => native.GetColumn<UltraSim.ECS.Tests.LayoutTag>(nativeChunk, layout, tagColumn));
        Assert.Throws<ArgumentException>(() => native.GetColumn<int>(nativeChunk, layout, wideColumn));

        Assert.True(native.TryRelease(nativeChunk));
        Assert.True(pinned.TryRelease(pinnedChunk));
        ChunkHandle clearedNative = native.Acquire();
        ChunkHandle clearedPinned = pinned.Acquire();
        Assert.True(IsAllZero(native.GetChunkBytes(clearedNative)));
        Assert.True(IsAllZero(pinned.GetChunkBytes(clearedPinned)));
    }

    private static void ChunkArenas_RejectIncompatiblePlansAndInvalidColumns()
    {
        CreateLayoutStoragePlan(ChunkLayoutOptions.KiB16, out _, out ChunkLayoutPlan layout16, out ComponentColumnLayout[] columns16);
        CreateLayoutStoragePlan(ChunkLayoutOptions.KiB32, out _, out ChunkLayoutPlan layout32, out _);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout16, 1);
        using var native = new NativeChunkArena(capacity);
        using var pinned = new PinnedChunkArena(capacity);
        ChunkHandle nativeChunk = native.Acquire();
        ChunkHandle pinnedChunk = pinned.Acquire();
        ComponentColumnLayout wide = FindColumn(
            columns16,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());

        Assert.Throws<ArgumentException>(() => native.GetEntityIndices(nativeChunk, layout32));
        Assert.Throws<ArgumentException>(() => pinned.GetEntityIndices(pinnedChunk, layout32));
        var malformed = new ComponentColumnLayout(
            wide.ComponentId,
            wide.Flags,
            wide.ElementSizeBytes,
            wide.EffectiveAlignmentBytes,
            dataOffsetBytes: capacity.ChunkSizeBytes,
            dataBytes: wide.DataBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => native.GetColumn<UltraSim.ECS.Tests.LayoutWide>(nativeChunk, layout16, malformed));
        Assert.Throws<ArgumentOutOfRangeException>(() => pinned.GetColumn<UltraSim.ECS.Tests.LayoutWide>(pinnedChunk, layout16, malformed));
    }

    private static void ChunkArenas_DisposalAccountingAndUseAfterDispose()
    {
        var capacity = new ChunkArenaCapacity(4096, 64, 4);
        var native = new NativeChunkArena(capacity);
        var pinned = new PinnedChunkArena(capacity);
        ChunkHandle nativeChunk = native.Acquire();
        ChunkHandle pinnedChunk = pinned.Acquire();

        native.Dispose();
        native.Dispose();
        pinned.Dispose();
        pinned.Dispose();

        ChunkArenaSnapshot nativeSnapshot = native.GetSnapshot();
        ChunkArenaSnapshot pinnedSnapshot = pinned.GetSnapshot();
        Assert.True(nativeSnapshot.IsDisposed);
        Assert.True(pinnedSnapshot.IsDisposed);
        Assert.Equal(0, nativeSnapshot.AvailableCount);
        Assert.Equal(0, pinnedSnapshot.AvailableCount);
        Assert.Equal(0L, nativeSnapshot.PayloadBytesInUse);
        Assert.Equal(0L, pinnedSnapshot.PayloadBytesInUse);
        Assert.Equal(capacity.TotalPayloadBytes, nativeSnapshot.ReleasedPayloadBytes);
        Assert.Equal(capacity.TotalPayloadBytes, pinnedSnapshot.ReleasedPayloadBytes);
        Assert.Throws<ObjectDisposedException>(() => native.GetChunkBytes(nativeChunk));
        Assert.Throws<ObjectDisposedException>(() => pinned.GetChunkBytes(pinnedChunk));
        Assert.Throws<ObjectDisposedException>(() => native.Acquire());
        Assert.Throws<ObjectDisposedException>(() => pinned.Acquire());
    }

    private static void ChunkSlotPool_GenerationMaximumRetiresWithoutReuse()
    {
        var pool = new ChunkSlotPool(ChunkArenaId.Create(), 1);
        ChunkHandle first = pool.Acquire();
        FieldInfo generationsField = typeof(ChunkSlotPool).GetField(
            "_generations",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Chunk generation storage was not found.");
        var generations = (uint[])(generationsField.GetValue(pool)
            ?? throw new InvalidOperationException("Chunk generation storage was null."));
        generations[first.Index] = uint.MaxValue;

        var maximum = new ChunkHandle(pool.ArenaId, first.Index, uint.MaxValue);
        Assert.False(pool.IsAlive(first));
        Assert.True(pool.IsAlive(maximum));
        Assert.True(pool.TryRelease(maximum));
        ChunkSlotPool.SlotSnapshot snapshot = pool.GetSlotSnapshot((int)first.Index);
        Assert.True(snapshot.IsRetired);
        Assert.False(snapshot.IsAlive);
        Assert.Equal(uint.MaxValue, snapshot.Generation);
        Assert.Equal(1, pool.RetiredCount);
        Assert.Equal(0, pool.AvailableCount);
        Assert.False(pool.TryAcquire(out ChunkHandle unavailable));
        Assert.Equal(ChunkHandle.Null, unavailable);
    }

    private static void ChunkSlotPool_RandomizedLifecycleMatchesSlowModel()
    {
        const int capacity = 257;
        const int operations = 100_000;
        var pool = new ChunkSlotPool(ChunkArenaId.Create(), capacity);
        var live = new List<ChunkHandle>(capacity);
        var random = new Random(unchecked((int)0xC4A55EED));

        for (int operation = 0; operation < operations; operation++)
        {
            bool acquire = live.Count == 0 || (live.Count < capacity && random.Next(100) < 57);
            if (acquire)
            {
                Assert.True(pool.TryAcquire(out ChunkHandle handle));
                Assert.True(pool.IsAlive(handle));
                live.Add(handle);
            }
            else
            {
                int victimIndex = random.Next(live.Count);
                ChunkHandle victim = live[victimIndex];
                live[victimIndex] = live[^1];
                live.RemoveAt(live.Count - 1);
                Assert.True(pool.TryRelease(victim));
                Assert.False(pool.IsAlive(victim));
                Assert.False(pool.TryRelease(victim));
            }

            if ((operation & 1023) == 0)
            {
                Assert.Equal(live.Count, pool.AliveCount);
                Assert.Equal(capacity - live.Count, pool.AvailableCount);
                foreach (ChunkHandle handle in live)
                    Assert.True(pool.IsAlive(handle));
            }
        }

        Assert.Equal(live.Count, pool.AliveCount);
    }

    private static void ChunkArenas_WarmedLifecycleAndTypedAccessAllocateZero()
    {
        const int lifecycleOperations = 100_000;
        const int accessOperations = 1_000_000;
        CreateLayoutStoragePlan(
            ChunkLayoutOptions.KiB16,
            out _,
            out ChunkLayoutPlan layout,
            out ComponentColumnLayout[] columns);
        ComponentColumnLayout wide = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, 1);
        using var native = new NativeChunkArena(capacity);
        using var pinned = new PinnedChunkArena(capacity);

        ChunkHandle nativeChunk = native.Acquire();
        ChunkHandle pinnedChunk = pinned.Acquire();
        for (int i = 0; i < 8; i++)
        {
            Assert.True(native.TryRelease(nativeChunk));
            nativeChunk = native.Acquire();
            Assert.True(pinned.TryRelease(pinnedChunk));
            pinnedChunk = pinned.Acquire();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < lifecycleOperations; i++)
        {
            native.TryRelease(nativeChunk);
            native.TryAcquire(out nativeChunk);
            pinned.TryRelease(pinnedChunk);
            pinned.TryAcquire(out pinnedChunk);
            checksum += nativeChunk.Generation + pinnedChunk.Generation;
        }
        for (int i = 0; i < accessOperations; i++)
        {
            Span<UltraSim.ECS.Tests.LayoutWide> nativeWide = native.GetColumn<UltraSim.ECS.Tests.LayoutWide>(nativeChunk, layout, wide);
            Span<UltraSim.ECS.Tests.LayoutWide> pinnedWide = pinned.GetColumn<UltraSim.ECS.Tests.LayoutWide>(pinnedChunk, layout, wide);
            nativeWide[0].A = i;
            pinnedWide[0].A = i;
            checksum += nativeWide[0].A + pinnedWide[0].A;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
    }

    private static void ChunkRow_LayoutAndNullSemanticsAreExplicit()
    {
        Assert.Equal(8, Unsafe.SizeOf<ChunkRow>());
        Assert.Equal(8, Marshal.SizeOf<ChunkRow>());
        Assert.False(ChunkRow.Invalid.IsValid);
        Assert.Equal(-1, ChunkRow.Invalid.Row);

        var first = new ChunkRow(0, 0);
        var later = new ChunkRow(17, 255);
        Assert.True(first.IsValid);
        Assert.Equal(0u, first.ChunkIndex);
        Assert.Equal(0, first.Row);
        Assert.Equal(17u, later.ChunkIndex);
        Assert.Equal(255, later.Row);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkRow(0, -1));
    }

    private static void ArchetypeChunkStore_RejectsMalformedOrNonExclusiveInputs()
    {
        CreateLayoutStoragePlan(
            ChunkLayoutOptions.KiB16,
            out _,
            out ChunkLayoutPlan layout,
            out ComponentColumnLayout[] columns);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, 2);

        using (var occupied = new NativeChunkArena(capacity))
        {
            _ = occupied.Acquire();
            Assert.Throws<ArgumentException>(() =>
                new ArchetypeChunkStore<NativeChunkArena>(occupied, layout, columns));
        }

        using (var wrongSize = new NativeChunkArena(ChunkArenaCapacity.FromLayout(layout, 1)))
        {
            Assert.Throws<ArgumentException>(() =>
                new ArchetypeChunkStore<NativeChunkArena>(wrongSize, layout, columns.AsSpan(0, columns.Length - 1)));
        }

        ComponentColumnLayout[] malformed = columns.ToArray();
        int wideIndex = FindColumnIndex(
            malformed,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout wide = malformed[wideIndex];
        malformed[wideIndex] = new ComponentColumnLayout(
            wide.ComponentId,
            wide.Flags,
            wide.ElementSizeBytes,
            wide.EffectiveAlignmentBytes,
            dataOffsetBytes: layout.EntityIndexOffsetBytes,
            dataBytes: wide.DataBytes,
            enableBitsOffsetBytes: wide.EnableBitsOffsetBytes,
            enableBitsBytes: wide.EnableBitsBytes);
        using (var malformedArena = new NativeChunkArena(capacity))
        {
            Assert.Throws<ArgumentException>(() =>
                new ArchetypeChunkStore<NativeChunkArena>(malformedArena, layout, malformed));
        }

        var disposed = new NativeChunkArena(capacity);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            new ArchetypeChunkStore<NativeChunkArena>(disposed, layout, columns));

        using var store = new ArchetypeChunkStore<NativeChunkArena>(new NativeChunkArena(capacity), layout, columns);
        Assert.True(store.TryAdd(1, out ChunkRow location));
        ComponentColumnLayout realWide = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        var forged = new ComponentColumnLayout(
            new ComponentId((ushort)(realWide.ComponentId.Value + 1)),
            realWide.Flags,
            realWide.ElementSizeBytes,
            realWide.EffectiveAlignmentBytes,
            realWide.DataOffsetBytes,
            realWide.DataBytes,
            realWide.EnableBitsOffsetBytes,
            realWide.EnableBitsBytes);
        Assert.Throws<ArgumentException>(() => store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(location, forged));
    }

    private static void ArchetypeChunkStore_BoundariesAndRetainedEmptyReuse()
    {
        using ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan layout,
            out _);
        int rowCapacity = layout.RowCapacity;
        var locations = new ChunkRow[rowCapacity * 2];

        for (int i = 0; i < rowCapacity; i++)
            Assert.True(store.TryAdd((uint)(i + 1), out locations[i]));
        ArchetypeChunkSnapshot oneFull = store.GetSnapshot();
        Assert.Equal(rowCapacity, oneFull.EntityCount);
        Assert.Equal(28, oneFull.ChunkRecordSizeBytes);
        Assert.Equal(56L, oneFull.ChunkMetadataBytes);
        Assert.Equal(1, oneFull.FullChunkCount);
        Assert.Equal(0, oneFull.PartialChunkCount);
        Assert.Equal(1, oneFull.AcquiredChunkCount);

        Assert.True(store.TryAdd((uint)(rowCapacity + 1), out locations[rowCapacity]));
        ArchetypeChunkSnapshot withPartial = store.GetSnapshot();
        Assert.Equal(1, withPartial.FullChunkCount);
        Assert.Equal(1, withPartial.PartialChunkCount);
        Assert.Equal(2, withPartial.AcquiredChunkCount);

        for (int i = rowCapacity + 1; i < locations.Length; i++)
            Assert.True(store.TryAdd((uint)(i + 1), out locations[i]));
        Assert.False(store.TryAdd(uint.MaxValue, out ChunkRow unavailable));
        Assert.Equal(ChunkRow.Invalid, unavailable);
        ArchetypeChunkSnapshot full = store.GetSnapshot();
        Assert.Equal(2, full.FullChunkCount);
        Assert.Equal(0, full.PartialChunkCount);
        Assert.Equal(0, full.UnallocatedChunkCount);

        ChunkRow last = locations[^1];
        uint lastEntity = (uint)locations.Length;
        Assert.True(store.TryRemove(last, lastEntity, out ChunkRowRelocation noMove));
        Assert.False(noMove.HasValue);
        Assert.Equal(1, store.GetSnapshot().PartialChunkCount);

        ChunkRow first = locations[0];
        Assert.True(store.TryRemove(first, 1, out ChunkRowRelocation crossChunk));
        Assert.True(crossChunk.HasValue);
        Assert.Equal((uint)(locations.Length - 1), crossChunk.EntityIndex);
        Assert.Equal(first, crossChunk.Destination);
        Assert.False(store.TryRemove(first, 1, out _));

        uint partialChunkIndex = crossChunk.Source.ChunkIndex;
        while (store.GetRowCount(partialChunkIndex) != 0)
        {
            int row = store.GetRowCount(partialChunkIndex) - 1;
            var tail = new ChunkRow(partialChunkIndex, row);
            uint entityIndex = store.GetEntityIndex(tail);
            Assert.True(store.TryRemove(tail, entityIndex, out _));
        }

        ArchetypeChunkSnapshot retainedEmpty = store.GetSnapshot();
        Assert.Equal(1, retainedEmpty.FullChunkCount);
        Assert.Equal(0, retainedEmpty.PartialChunkCount);
        Assert.Equal(1, retainedEmpty.EmptyChunkCount);
        Assert.Equal(2, retainedEmpty.AcquiredChunkCount);
        ChunkHandle retainedHandle = store.GetChunkHandle(partialChunkIndex);

        Assert.True(store.TryAdd(900_001, out ChunkRow reused));
        Assert.Equal(partialChunkIndex, reused.ChunkIndex);
        Assert.Equal(retainedHandle, store.GetChunkHandle(partialChunkIndex));
        Assert.Equal(2, store.GetSnapshot().AcquiredChunkCount);
        store.ValidateInvariants();
    }

    private static void ArchetypeChunkStore_SwapRemovePreservesDataAndEnableBits()
    {
        using ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan layout,
            out ComponentColumnLayout[] columns);
        ComponentColumnLayout wide = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout enable = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>());
        ComponentColumnLayout tag = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>());

        int count = layout.RowCapacity + 3;
        var entityIndices = new uint[count];
        var locations = new ChunkRow[count];
        for (int i = 0; i < count; i++)
            entityIndices[i] = (uint)(1000 + i);
        Assert.Equal(count, store.Add(entityIndices, locations));

        ChunkRow donorLocation = locations[^1];
        uint donorEntity = entityIndices[^1];
        ref UltraSim.ECS.Tests.LayoutWide donorWide = ref store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(donorLocation, wide);
        donorWide.A = 111;
        donorWide.B = 222;
        donorWide.C = 333;
        donorWide.D = 444;
        store.SetEnabled(donorLocation, enable, enabled: false);
        store.SetEnabled(donorLocation, tag, enabled: false);

        ChunkRow targetLocation = locations[0];
        Assert.True(store.TryRemove(targetLocation, entityIndices[0], out ChunkRowRelocation crossChunk));
        Assert.True(crossChunk.HasValue);
        Assert.Equal(donorEntity, crossChunk.EntityIndex);
        Assert.Equal(donorLocation, crossChunk.Source);
        Assert.Equal(targetLocation, crossChunk.Destination);
        Assert.Equal(donorEntity, store.GetEntityIndex(targetLocation));
        ref UltraSim.ECS.Tests.LayoutWide movedWide = ref store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(targetLocation, wide);
        Assert.Equal(111L, movedWide.A);
        Assert.Equal(222L, movedWide.B);
        Assert.Equal(333L, movedWide.C);
        Assert.Equal(444L, movedWide.D);
        Assert.False(store.IsEnabled(targetLocation, enable));
        Assert.False(store.IsEnabled(targetLocation, tag));
        Assert.False(store.TryRemove(targetLocation, entityIndices[0], out _));

        ChunkRow withinTarget = locations[layout.RowCapacity];
        uint withinTargetEntity = entityIndices[layout.RowCapacity];
        ChunkRow withinDonor = locations[layout.RowCapacity + 1];
        uint withinDonorEntity = entityIndices[layout.RowCapacity + 1];
        ref UltraSim.ECS.Tests.LayoutWide withinDonorWide = ref store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(withinDonor, wide);
        withinDonorWide.A = 777;
        store.SetEnabled(withinDonor, enable, enabled: false);
        Assert.True(store.TryRemove(withinTarget, withinTargetEntity, out ChunkRowRelocation withinChunk));
        Assert.True(withinChunk.HasValue);
        Assert.Equal(withinDonorEntity, withinChunk.EntityIndex);
        Assert.Equal(withinDonor, withinChunk.Source);
        Assert.Equal(withinTarget, withinChunk.Destination);
        Assert.Equal(777L, store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(withinTarget, wide).A);
        Assert.False(store.IsEnabled(withinTarget, enable));
        store.ValidateInvariants();
    }

    private static void ArchetypeChunkStore_BulkNativePinnedParityAndDefaultState()
    {
        using ArchetypeChunkStore<NativeChunkArena> native = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan nativeLayout,
            out ComponentColumnLayout[] nativeColumns);
        using ArchetypeChunkStore<PinnedChunkArena> pinned = CreateArchetypeStore<PinnedChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan pinnedLayout,
            out ComponentColumnLayout[] pinnedColumns);
        Assert.Equal(nativeLayout, pinnedLayout);

        int count = nativeLayout.RowCapacity + 77;
        var entityIndices = new uint[count];
        var nativeLocations = new ChunkRow[count];
        var pinnedLocations = new ChunkRow[count];
        for (int i = 0; i < count; i++)
            entityIndices[i] = (uint)(i + 1);
        Assert.Equal(count, native.Add(entityIndices, nativeLocations));
        Assert.Equal(count, pinned.Add(entityIndices, pinnedLocations));

        ComponentColumnLayout nativeWide = FindColumn(
            nativeColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout pinnedWide = FindColumn(
            pinnedColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout nativeEnable = FindColumn(
            nativeColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>());
        ComponentColumnLayout pinnedEnable = FindColumn(
            pinnedColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>());
        ComponentColumnLayout nativeTag = FindColumn(
            nativeColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>());
        ComponentColumnLayout pinnedTag = FindColumn(
            pinnedColumns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>());

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(nativeLocations[i], pinnedLocations[i]);
            Assert.Equal(0L, native.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(nativeLocations[i], nativeWide).A);
            Assert.Equal(0L, pinned.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(pinnedLocations[i], pinnedWide).A);
            Assert.True(native.IsEnabled(nativeLocations[i], nativeEnable));
            Assert.True(pinned.IsEnabled(pinnedLocations[i], pinnedEnable));
            Assert.True(native.IsEnabled(nativeLocations[i], nativeTag));
            Assert.True(pinned.IsEnabled(pinnedLocations[i], pinnedTag));
        }

        int partialRows = count - nativeLayout.RowCapacity;
        Span<ulong> nativePartialEnableWords = native.Arena.GetEnableWords(
            native.GetChunkHandle(1),
            nativeLayout,
            nativeEnable);
        Span<ulong> pinnedPartialEnableWords = pinned.Arena.GetEnableWords(
            pinned.GetChunkHandle(1),
            pinnedLayout,
            pinnedEnable);
        for (int row = 0; row < nativeLayout.RowCapacity; row++)
        {
            bool expected = row < partialRows;
            bool nativeEnabled = (nativePartialEnableWords[row >> 6] & (1UL << (row & 63))) != 0;
            bool pinnedEnabled = (pinnedPartialEnableWords[row >> 6] & (1UL << (row & 63))) != 0;
            Assert.Equal(expected, nativeEnabled);
            Assert.Equal(expected, pinnedEnabled);
        }

        for (int i = 0; i < count; i += 17)
        {
            native.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(nativeLocations[i], nativeWide).A = i * 13L;
            pinned.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(pinnedLocations[i], pinnedWide).A = i * 13L;
            bool enabled = (i & 1) == 0;
            native.SetEnabled(nativeLocations[i], nativeEnable, enabled);
            pinned.SetEnabled(pinnedLocations[i], pinnedEnable, enabled);
        }

        native.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(nativeLocations[^1], nativeWide).A = 999_999;
        pinned.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(pinnedLocations[^1], pinnedWide).A = 999_999;
        native.SetEnabled(nativeLocations[^1], nativeEnable, enabled: false);
        pinned.SetEnabled(pinnedLocations[^1], pinnedEnable, enabled: false);

        Assert.True(native.TryRemove(nativeLocations[3], entityIndices[3], out ChunkRowRelocation nativeMove));
        Assert.True(pinned.TryRemove(pinnedLocations[3], entityIndices[3], out ChunkRowRelocation pinnedMove));
        Assert.Equal(nativeMove.HasValue, pinnedMove.HasValue);
        Assert.Equal(nativeMove.EntityIndex, pinnedMove.EntityIndex);
        Assert.Equal(nativeMove.Source, pinnedMove.Source);
        Assert.Equal(nativeMove.Destination, pinnedMove.Destination);

        int acquired = native.GetSnapshot().AcquiredChunkCount;
        Assert.Equal(acquired, pinned.GetSnapshot().AcquiredChunkCount);
        for (uint chunkIndex = 0; chunkIndex < acquired; chunkIndex++)
        {
            Span<byte> nativeBytes = native.Arena.GetChunkBytes(native.GetChunkHandle(chunkIndex));
            Span<byte> pinnedBytes = pinned.Arena.GetChunkBytes(pinned.GetChunkHandle(chunkIndex));
            Assert.True(nativeBytes.SequenceEqual(pinnedBytes));
        }

        Assert.True(native.TryAdd(700_001, out ChunkRow nativeNew));
        Assert.True(pinned.TryAdd(700_001, out ChunkRow pinnedNew));
        Assert.Equal(nativeNew, pinnedNew);
        Assert.Equal(0L, native.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(nativeNew, nativeWide).A);
        Assert.Equal(0L, pinned.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(pinnedNew, pinnedWide).A);
        Assert.True(native.IsEnabled(nativeNew, nativeEnable));
        Assert.True(pinned.IsEnabled(pinnedNew, pinnedEnable));
        native.ValidateInvariants();
        pinned.ValidateInvariants();
    }

    private static void ArchetypeChunkStore_RandomizedNativeOperationsMatchReferenceModel() =>
        RunRandomizedArchetypeModel<NativeChunkArena>(unchecked((int)0xA11CE001));

    private static void ArchetypeChunkStore_RandomizedPinnedOperationsMatchReferenceModel() =>
        RunRandomizedArchetypeModel<PinnedChunkArena>(unchecked((int)0xA11CE002));

    private static void ArchetypeChunkStore_WarmedChurnAllocatesZero()
    {
        const int operations = 100_000;
        using ArchetypeChunkStore<NativeChunkArena> native = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out ChunkLayoutPlan layout,
            out _);
        using ArchetypeChunkStore<PinnedChunkArena> pinned = CreateArchetypeStore<PinnedChunkArena>(
            maximumChunks: 2,
            out _,
            out _);
        int count = layout.RowCapacity * 2;
        var entityIndices = new uint[count];
        var nativeLocations = new ChunkRow[count];
        var pinnedLocations = new ChunkRow[count];
        for (int i = 0; i < count; i++)
            entityIndices[i] = (uint)i;
        Assert.Equal(count, native.Add(entityIndices, nativeLocations));
        Assert.Equal(count, pinned.Add(entityIndices, pinnedLocations));

        for (int i = 0; i < 32; i++)
            ChurnOnce(native, pinned, entityIndices, nativeLocations, pinnedLocations, i % count);

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < operations; i++)
        {
            int victim = i % count;
            ChurnOnce(native, pinned, entityIndices, nativeLocations, pinnedLocations, victim);
            checksum += nativeLocations[victim].Row + pinnedLocations[victim].Row;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum >= 0);
        Assert.Equal(0L, allocated);
        native.ValidateInvariants();
        pinned.ValidateInvariants();
    }

    private static void ArchetypeChunkStore_DisposalIsOwnedAndIdempotent()
    {
        ArchetypeChunkStore<NativeChunkArena> store = CreateArchetypeStore<NativeChunkArena>(
            maximumChunks: 2,
            out _,
            out _);
        NativeChunkArena arena = store.Arena;
        Assert.True(store.TryAdd(1, out ChunkRow location));

        store.Dispose();
        store.Dispose();
        Assert.True(store.IsDisposed);
        Assert.True(arena.IsDisposed);
        Assert.True(store.GetSnapshot().IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => store.TryAdd(2, out _));
        Assert.Throws<ObjectDisposedException>(() => store.GetEntityIndex(location));
    }

    private static void RunRandomizedArchetypeModel<TArena>(int seed)
        where TArena : class, IChunkArenaBackend
    {
        const int operations = 30_000;
        using ArchetypeChunkStore<TArena> store = CreateArchetypeStore<TArena>(
            maximumChunks: 4,
            out ChunkLayoutPlan layout,
            out ComponentColumnLayout[] columns);
        ComponentColumnLayout wide = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>());
        ComponentColumnLayout enable = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>());
        ComponentColumnLayout tag = FindColumn(
            columns,
            UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>());
        int capacity = checked(layout.RowCapacity * 4);
        var model = new Dictionary<uint, ArchetypeModelEntry>(capacity);
        var live = new List<uint>(capacity);
        var random = new Random(seed);
        uint nextEntityIndex = 1;

        for (int operation = 0; operation < operations; operation++)
        {
            bool add = live.Count == 0 || (live.Count < capacity && random.Next(100) < 57);
            if (add)
            {
                uint entityIndex = nextEntityIndex++;
                Assert.True(store.TryAdd(entityIndex, out ChunkRow location));
                long value = ((long)entityIndex << 16) ^ random.Next();
                bool enabled = random.Next(2) != 0;
                bool tagEnabled = random.Next(2) != 0;
                store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(location, wide).A = value;
                store.SetEnabled(location, enable, enabled);
                store.SetEnabled(location, tag, tagEnabled);
                model.Add(entityIndex, new ArchetypeModelEntry(location, value, enabled, tagEnabled));
                live.Add(entityIndex);
            }
            else
            {
                int victimListIndex = random.Next(live.Count);
                uint victim = live[victimListIndex];
                ArchetypeModelEntry victimEntry = model[victim];
                Assert.True(store.TryRemove(victimEntry.Location, victim, out ChunkRowRelocation relocation));
                model.Remove(victim);
                live[victimListIndex] = live[^1];
                live.RemoveAt(live.Count - 1);
                if (relocation.HasValue)
                {
                    ArchetypeModelEntry moved = model[relocation.EntityIndex];
                    Assert.Equal(moved.Location, relocation.Source);
                    moved.Location = relocation.Destination;
                    model[relocation.EntityIndex] = moved;
                }
                Assert.False(store.TryRemove(victimEntry.Location, victim, out _));
            }

            if ((operation & 255) == 0)
            {
                store.ValidateInvariants();
                Assert.Equal(model.Count, store.Count);
                foreach ((uint entityIndex, ArchetypeModelEntry expected) in model)
                {
                    Assert.Equal(entityIndex, store.GetEntityIndex(expected.Location));
                    Assert.Equal(
                        expected.Value,
                        store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(expected.Location, wide).A);
                    Assert.Equal(expected.Enabled, store.IsEnabled(expected.Location, enable));
                    Assert.Equal(expected.TagEnabled, store.IsEnabled(expected.Location, tag));
                }
            }
        }

        store.ValidateInvariants();
        Assert.Equal(model.Count, store.Count);
    }

    private static void ChurnOnce<TNative, TPinned>(
        ArchetypeChunkStore<TNative> native,
        ArchetypeChunkStore<TPinned> pinned,
        uint[] entityIndices,
        ChunkRow[] nativeLocations,
        ChunkRow[] pinnedLocations,
        int victim)
        where TNative : class, IChunkArenaBackend
        where TPinned : class, IChunkArenaBackend
    {
        uint entityIndex = entityIndices[victim];
        Assert.True(native.TryRemove(nativeLocations[victim], entityIndex, out ChunkRowRelocation nativeMove));
        Assert.True(pinned.TryRemove(pinnedLocations[victim], entityIndex, out ChunkRowRelocation pinnedMove));
        if (nativeMove.HasValue)
            nativeLocations[nativeMove.EntityIndex] = nativeMove.Destination;
        if (pinnedMove.HasValue)
            pinnedLocations[pinnedMove.EntityIndex] = pinnedMove.Destination;
        Assert.True(native.TryAdd(entityIndex, out nativeLocations[victim]));
        Assert.True(pinned.TryAdd(entityIndex, out pinnedLocations[victim]));
    }

    private static void LocatedChurnOnce<TArena>(
        EntityPool pool,
        ArchetypeChunkStore<TArena> store,
        ArchetypeId archetypeId,
        Entity[] live,
        int victimIndex)
        where TArena : class, IChunkArenaBackend
    {
        Entity victim = live[victimIndex];
        Assert.True(pool.TryGetLocation(victim, out EntityLocation location));
        Assert.True(store.TryRemove(location.ChunkRow, victim.Index, out ChunkRowRelocation relocation));
        Assert.True(pool.TryUnbindLocation(victim, location));
        pool.ApplyRelocation(archetypeId, relocation);
        Assert.True(pool.TryFree(victim));

        Entity replacement = pool.Allocate();
        Assert.Equal(victim.Index, replacement.Index);
        Assert.True(store.TryAdd(replacement.Index, out ChunkRow row));
        Assert.True(pool.TryBindLocation(replacement, new EntityLocation(archetypeId, row)));
        live[victimIndex] = replacement;
    }

    private static ArchetypeChunkStore<TArena> CreateArchetypeStore<TArena>(
        int maximumChunks,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
        where TArena : class, IChunkArenaBackend
    {
        CreateLayoutStoragePlan(ChunkLayoutOptions.KiB16, out _, out layout, out columns);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, maximumChunks);
        object arena = typeof(TArena) == typeof(NativeChunkArena)
            ? new NativeChunkArena(capacity)
            : typeof(TArena) == typeof(PinnedChunkArena)
                ? new PinnedChunkArena(capacity)
                : throw new InvalidOperationException($"Unsupported test arena {typeof(TArena).Name}.");
        return new ArchetypeChunkStore<TArena>((TArena)arena, layout, columns);
    }

    private static int FindColumnIndex(
        ReadOnlySpan<ComponentColumnLayout> columns,
        ComponentId componentId)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].ComponentId == componentId)
                return i;
        }

        return -1;
    }

    private struct ArchetypeModelEntry
    {
        internal ArchetypeModelEntry(ChunkRow location, long value, bool enabled, bool tagEnabled)
        {
            Location = location;
            Value = value;
            Enabled = enabled;
            TagEnabled = tagEnabled;
        }

        internal ChunkRow Location;
        internal long Value;
        internal bool Enabled;
        internal bool TagEnabled;
    }

    private static void CreateLayoutStoragePlan(
        ChunkLayoutOptions options,
        out ComponentSchema schema,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
    {
        schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = CreateLayoutSignature();
        columns = new ComponentColumnLayout[signature.Length];
        layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
    }

    private static ComponentColumnLayout FindColumn(
        ReadOnlySpan<ComponentColumnLayout> columns,
        ComponentId componentId)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].ComponentId == componentId)
                return columns[i];
        }

        throw new InvalidOperationException($"Component column {componentId} was not found.");
    }

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
                return false;
        }

        return true;
    }

    private static ComponentId[] CreateLayoutSignature() =>
    [
        UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutByte>(),
        UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>(),
        UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutInt>(),
        UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutTag>(),
        UltraSim.ECS.Tests.LayoutTestSchema.GetComponentId<UltraSim.ECS.Tests.LayoutWide>(),
    ];

    private static long ReferenceRequiredBytes(
        ChunkLayoutOptions options,
        ReadOnlySpan<ComponentColumnLayout> physicalColumns,
        int rows)
    {
        long cursor = options.ReservedHeaderBytes;
        cursor = ReferenceAlign(cursor, Math.Max(options.MinimumColumnAlignmentBytes, options.EntityIndexSizeBytes));
        cursor += (long)rows * options.EntityIndexSizeBytes;
        for (int i = 0; i < physicalColumns.Length; i++)
        {
            ComponentColumnLayout column = physicalColumns[i];
            if (!column.HasData)
                continue;
            cursor = ReferenceAlign(cursor, column.EffectiveAlignmentBytes);
            cursor += (long)rows * column.ElementSizeBytes;
        }

        long enableBytes = ((rows + 63L) / 64L) * sizeof(ulong);
        for (int i = 0; i < physicalColumns.Length; i++)
        {
            if (!physicalColumns[i].HasEnableBits)
                continue;
            cursor = ReferenceAlign(cursor, sizeof(ulong));
            cursor += enableBytes;
        }

        return cursor;
    }

    private static long ReferenceAlign(long value, int alignment)
    {
        long remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }
}

internal static class Assert
{
    internal static void True(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Expected condition to be true.");
    }

    internal static void False(bool condition) => True(!condition);

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void NotEqual<T>(T unexpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"Did not expect {actual}.");
    }

    internal static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
