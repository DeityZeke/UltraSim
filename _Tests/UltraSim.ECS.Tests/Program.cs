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
        Run(nameof(ArchetypeRegistry_RegistersDenseCanonicalIdsAndRoutesOwnedStorage), ArchetypeRegistry_RegistersDenseCanonicalIdsAndRoutesOwnedStorage);
        Run(nameof(ArchetypeRegistry_RejectsInvalidSignaturesGeometryAndCapacity), ArchetypeRegistry_RejectsInvalidSignaturesGeometryAndCapacity);
        Run(nameof(ArchetypeRegistry_OpenAddressCollisionsPreserveFullSignatureIdentity), ArchetypeRegistry_OpenAddressCollisionsPreserveFullSignatureIdentity);
        Run(nameof(ArchetypeRegistry_RandomizedSealedLookupIsCorrectAndAllocationFree), ArchetypeRegistry_RandomizedSealedLookupIsCorrectAndAllocationFree);
        Run(nameof(ArchetypeRegistry_IsolatesInstancesAndOwnsNativePinnedDisposal), ArchetypeRegistry_IsolatesInstancesAndOwnsNativePinnedDisposal);
        Run(nameof(ArchetypeMigrator_PreservesRetainedStateDefaultsAdditionsAndRelocatesDonor), ArchetypeMigrator_PreservesRetainedStateDefaultsAdditionsAndRelocatesDonor);
        Run(nameof(ArchetypeMigrator_RejectsInvalidPlansAndLeavesSourceOnCapacityFailure), ArchetypeMigrator_RejectsInvalidPlansAndLeavesSourceOnCapacityFailure);
        Run(nameof(ArchetypeMigrator_RandomizedBidirectionalModelRemainsConsistent), ArchetypeMigrator_RandomizedBidirectionalModelRemainsConsistent);
        Run(nameof(ArchetypeMigrator_NativeAndPinnedExecutionRemainEquivalent), ArchetypeMigrator_NativeAndPinnedExecutionRemainEquivalent);
        Run(nameof(ArchetypeMigrator_WarmedBidirectionalChurnAllocatesZeroManagedBytes), ArchetypeMigrator_WarmedBidirectionalChurnAllocatesZeroManagedBytes);
        Run(nameof(EcsWorld_LifecycleSealsRegistrationAndOwnsDisposal), EcsWorld_LifecycleSealsRegistrationAndOwnsDisposal);
        Run(nameof(EcsWorld_CreateDestroyMaintainsRowsLocationsAndGenerations), EcsWorld_CreateDestroyMaintainsRowsLocationsAndGenerations);
        Run(nameof(EcsWorld_CapacityFailuresAreAtomicAndDoNotBurnSlots), EcsWorld_CapacityFailuresAreAtomicAndDoNotBurnSlots);
        Run(nameof(EcsWorld_RejectsForeignEntitiesAndTransitionPlans), EcsWorld_RejectsForeignEntitiesAndTransitionPlans);
        Run(nameof(EcsWorld_RandomizedLifecycleMatchesReferenceModel), EcsWorld_RandomizedLifecycleMatchesReferenceModel);
        Run(nameof(EcsWorld_NativeAndPinnedLifecycleRemainEquivalent), EcsWorld_NativeAndPinnedLifecycleRemainEquivalent);
        Run(nameof(EcsWorld_WarmedCreateTransitionDestroyAllocatesZeroManagedBytes), EcsWorld_WarmedCreateTransitionDestroyAllocatesZeroManagedBytes);
        Run(nameof(ArchetypeComponentBinding_LayoutAndGeneratedBindingAreExplicit), ArchetypeComponentBinding_LayoutAndGeneratedBindingAreExplicit);
        Run(nameof(EcsWorld_BoundAccessReadsWritesAndInitializes), EcsWorld_BoundAccessReadsWritesAndInitializes);
        Run(nameof(EcsWorld_BoundAccessRejectsTagsNonEnableableAndInvalidBindings), EcsWorld_BoundAccessRejectsTagsNonEnableableAndInvalidBindings);
        Run(nameof(EcsWorld_BoundAccessTracksExactArchetypeAcrossTransitions), EcsWorld_BoundAccessTracksExactArchetypeAcrossTransitions);
        Run(nameof(EcsWorld_BoundAccessRejectsForeignWorldAndStaleEntities), EcsWorld_BoundAccessRejectsForeignWorldAndStaleEntities);
        Run(nameof(EcsWorld_BoundAccessRandomizedModelRemainsConsistent), EcsWorld_BoundAccessRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_BoundAccessNativePinnedParity), EcsWorld_BoundAccessNativePinnedParity);
        Run(nameof(EcsWorld_WarmedBoundAccessAllocatesZeroManagedBytes), EcsWorld_WarmedBoundAccessAllocatesZeroManagedBytes);
        Run(nameof(EcsWorld_ChunkReadVisitsExactPackedRowsAndMetadata), EcsWorld_ChunkReadVisitsExactPackedRowsAndMetadata);
        Run(nameof(EcsWorld_ChunkWriteUpdatesDataAndEnableState), EcsWorld_ChunkWriteUpdatesDataAndEnableState);
        Run(nameof(EcsWorld_ChunkViewsRejectTagsForeignBindingsAndInvalidOperations), EcsWorld_ChunkViewsRejectTagsForeignBindingsAndInvalidOperations);
        Run(nameof(EcsWorld_ChunkVisitEnforcesBarrierAndReleasesAfterException), EcsWorld_ChunkVisitEnforcesBarrierAndReleasesAfterException);
        Run(nameof(EcsWorld_ChunkVisitRandomizedModelRemainsConsistent), EcsWorld_ChunkVisitRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_ChunkVisitNativePinnedParity), EcsWorld_ChunkVisitNativePinnedParity);
        Run(nameof(EcsWorld_WarmedChunkVisitsAllocateZeroManagedBytes), EcsWorld_WarmedChunkVisitsAllocateZeroManagedBytes);
        Run(nameof(ArchetypeReadWriteQueryPlan_SealCompilesCompleteArchetypeSet), ArchetypeReadWriteQueryPlan_SealCompilesCompleteArchetypeSet);
        Run(nameof(EcsWorld_ReadWriteQueryVisitsEveryMatchedArchetype), EcsWorld_ReadWriteQueryVisitsEveryMatchedArchetype);
        Run(nameof(EcsWorld_ReadWriteQueryRejectsAliasesTagsForeignPlansAndCapacity), EcsWorld_ReadWriteQueryRejectsAliasesTagsForeignPlansAndCapacity);
        Run(nameof(EcsWorld_ReadWriteQueryEnforcesBarrierAndReleasesAfterException), EcsWorld_ReadWriteQueryEnforcesBarrierAndReleasesAfterException);
        Run(nameof(EcsWorld_ReadWriteQueryRandomizedModelRemainsConsistent), EcsWorld_ReadWriteQueryRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_ReadWriteQueryNativePinnedParity), EcsWorld_ReadWriteQueryNativePinnedParity);
        Run(nameof(EcsWorld_WarmedReadWriteQueryAllocatesZeroManagedBytes), EcsWorld_WarmedReadWriteQueryAllocatesZeroManagedBytes);
        Run(nameof(ArchetypeQueryFilter_NormalizesAndRejectsInvalidConstraints), ArchetypeQueryFilter_NormalizesAndRejectsInvalidConstraints);
        Run(nameof(ArchetypeReadWriteQueryPlan_FiltersArchetypesAtSeal), ArchetypeReadWriteQueryPlan_FiltersArchetypesAtSeal);
        Run(nameof(EcsWorld_ReadWriteQueryFiltersEnabledAndDisabledRows), EcsWorld_ReadWriteQueryFiltersEnabledAndDisabledRows);
        Run(nameof(EcsWorld_FilteredReadWriteQueryRandomizedModelRemainsConsistent), EcsWorld_FilteredReadWriteQueryRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_FilteredReadWriteQueryNativePinnedParity), EcsWorld_FilteredReadWriteQueryNativePinnedParity);
        Run(nameof(EcsWorld_WarmedFilteredReadWriteQueryAllocatesZeroManagedBytes), EcsWorld_WarmedFilteredReadWriteQueryAllocatesZeroManagedBytes);
        Run(nameof(GeneratedQueryFamily_SealCompilesCompleteArchetypeSet), GeneratedQueryFamily_SealCompilesCompleteArchetypeSet);
        Run(nameof(EcsWorld_Generated2R1WVisitsEveryMatchedArchetype), EcsWorld_Generated2R1WVisitsEveryMatchedArchetype);
        Run(nameof(EcsWorld_Generated2R1WRejectsAliasesTagsForeignPlansAndCapacity), EcsWorld_Generated2R1WRejectsAliasesTagsForeignPlansAndCapacity);
        Run(nameof(EcsWorld_Generated2R1WFiltersRows), EcsWorld_Generated2R1WFiltersRows);
        Run(nameof(EcsWorld_Generated2R1WEnforcesBarrierAndReleasesAfterException), EcsWorld_Generated2R1WEnforcesBarrierAndReleasesAfterException);
        Run(nameof(EcsWorld_Generated2R1WRandomizedModelRemainsConsistent), EcsWorld_Generated2R1WRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_Generated2R1WNativePinnedParity), EcsWorld_Generated2R1WNativePinnedParity);
        Run(nameof(EcsWorld_WarmedGenerated2R1WAllocatesZeroManagedBytes), EcsWorld_WarmedGenerated2R1WAllocatesZeroManagedBytes);
        Run(nameof(GeneratedWriteFamily_SealCompilesCompleteArchetypeSet), GeneratedWriteFamily_SealCompilesCompleteArchetypeSet);
        Run(nameof(EcsWorld_Generated1R2WWritesEveryMatchedArchetype), EcsWorld_Generated1R2WWritesEveryMatchedArchetype);
        Run(nameof(EcsWorld_Generated1R2WRejectsAliasesTagsForeignPlansAndCapacity), EcsWorld_Generated1R2WRejectsAliasesTagsForeignPlansAndCapacity);
        Run(nameof(EcsWorld_Generated1R2WFiltersRows), EcsWorld_Generated1R2WFiltersRows);
        Run(nameof(EcsWorld_Generated1R2WEnforcesBarrierAndReleasesAfterException), EcsWorld_Generated1R2WEnforcesBarrierAndReleasesAfterException);
        Run(nameof(EcsWorld_Generated1R2WRandomizedModelRemainsConsistent), EcsWorld_Generated1R2WRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_Generated1R2WNativePinnedParity), EcsWorld_Generated1R2WNativePinnedParity);
        Run(nameof(EcsWorld_WarmedGenerated1R2WAllocatesZeroManagedBytes), EcsWorld_WarmedGenerated1R2WAllocatesZeroManagedBytes);
        Run(nameof(GeneratedReadOnlyFamily_SealCompilesCompleteArchetypeSet), GeneratedReadOnlyFamily_SealCompilesCompleteArchetypeSet);
        Run(nameof(EcsWorld_Generated2R0WReadsEveryMatchedArchetype), EcsWorld_Generated2R0WReadsEveryMatchedArchetype);
        Run(nameof(EcsWorld_Generated2R0WRejectsAliasesTagsForeignPlansAndCapacity), EcsWorld_Generated2R0WRejectsAliasesTagsForeignPlansAndCapacity);
        Run(nameof(EcsWorld_Generated2R0WFiltersRows), EcsWorld_Generated2R0WFiltersRows);
        Run(nameof(EcsWorld_Generated2R0WEnforcesBarrierAndReleasesAfterException), EcsWorld_Generated2R0WEnforcesBarrierAndReleasesAfterException);
        Run(nameof(EcsWorld_Generated2R0WRandomizedModelRemainsConsistent), EcsWorld_Generated2R0WRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_Generated2R0WNativePinnedParity), EcsWorld_Generated2R0WNativePinnedParity);
        Run(nameof(EcsWorld_WarmedGenerated2R0WAllocatesZeroManagedBytes), EcsWorld_WarmedGenerated2R0WAllocatesZeroManagedBytes);
        Run(nameof(GeneratedWideFamilies_SealCompileAndRejectMisuse), GeneratedWideFamilies_SealCompileAndRejectMisuse);
        Run(nameof(EcsWorld_Generated3R1WVisitsAndFiltersRows), EcsWorld_Generated3R1WVisitsAndFiltersRows);
        Run(nameof(EcsWorld_Generated2R2WVisitsAndFiltersRows), EcsWorld_Generated2R2WVisitsAndFiltersRows);
        Run(nameof(EcsWorld_GeneratedWideFamiliesEnforceBarrierAndReleaseAfterException), EcsWorld_GeneratedWideFamiliesEnforceBarrierAndReleaseAfterException);
        Run(nameof(EcsWorld_GeneratedWideFamiliesRandomizedModelRemainsConsistent), EcsWorld_GeneratedWideFamiliesRandomizedModelRemainsConsistent);
        Run(nameof(EcsWorld_GeneratedWideFamiliesNativePinnedParity), EcsWorld_GeneratedWideFamiliesNativePinnedParity);
        Run(nameof(EcsWorld_WarmedGeneratedWideFamiliesAllocateZeroManagedBytes), EcsWorld_WarmedGeneratedWideFamiliesAllocateZeroManagedBytes);

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
        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.GetSlotSnapshot(-1));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.GetSlotSnapshot(-1));
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

    private static void ArchetypeRegistry_RegistersDenseCanonicalIdsAndRoutesOwnedStorage()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var registry = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: 4,
            static capacity => new NativeChunkArena(capacity));
        ComponentId[] fullSignature = CreateLayoutSignature();
        ComponentId[] oneComponent = [fullSignature[0]];

        ArchetypeId emptyId = registry.Register(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeId oneId = registry.Register(oneComponent, ChunkLayoutOptions.KiB16, maximumChunks: 2);
        ArchetypeId fullId = registry.Register(fullSignature, ChunkLayoutOptions.KiB16, maximumChunks: 2);
        ArchetypeId duplicate = registry.Register(fullSignature, ChunkLayoutOptions.KiB16, maximumChunks: 2);

        Assert.Equal(new ArchetypeId(1), emptyId);
        Assert.Equal(new ArchetypeId(2), oneId);
        Assert.Equal(new ArchetypeId(3), fullId);
        Assert.Equal(fullId, duplicate);
        Assert.Equal(3, registry.Count);
        Assert.Equal(4, registry.MaximumArchetypes);
        Assert.Equal(8, registry.LookupCapacity);
        Assert.True(registry.TryGetId(ReadOnlySpan<ComponentId>.Empty, out ArchetypeId foundEmpty));
        Assert.True(registry.TryGetId(oneComponent, out ArchetypeId foundOne));
        Assert.True(registry.TryGetId(fullSignature, out ArchetypeId foundFull));
        Assert.Equal(emptyId, foundEmpty);
        Assert.Equal(oneId, foundOne);
        Assert.Equal(fullId, foundFull);

        ArchetypeStorage<NativeChunkArena> storage = registry.GetStorage(fullId);
        Assert.Equal(fullId, storage.Id);
        Assert.True(storage.Signature.SequenceEqual(fullSignature));
        Assert.Equal(fullSignature.Length, storage.Columns.Length);
        Assert.Equal(2, storage.Store.GetSnapshot().MaximumChunks);

        var pool = new EntityPool(WorldId.Create(), new EntityCapacity(1));
        Entity entity = pool.Allocate();
        Assert.True(storage.Store.TryAdd(entity.Index, out ChunkRow row));
        Assert.True(pool.TryBindLocation(entity, new EntityLocation(storage.Id, row)));
        Assert.True(pool.TryGetLocation(entity, out EntityLocation location));
        ArchetypeStorage<NativeChunkArena> routed = registry.GetStorage(location.ArchetypeId);
        Assert.Equal(entity.Index, routed.Store.GetEntityIndex(location.ChunkRow));

        registry.Seal();
        registry.Seal();
        Assert.True(registry.IsSealed);
        Assert.Throws<InvalidOperationException>(() =>
            registry.Register([fullSignature[0], fullSignature[1]], ChunkLayoutOptions.KiB16, 1));
    }

    private static void ArchetypeRegistry_RejectsInvalidSignaturesGeometryAndCapacity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] full = CreateLayoutSignature();
        Assert.Throws<ArgumentNullException>(() =>
            new ArchetypeRegistry<NativeChunkArena>(null!, 1, static capacity => new NativeChunkArena(capacity)));
        Assert.Throws<ArgumentNullException>(() =>
            new ArchetypeRegistry<NativeChunkArena>(schema, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArchetypeRegistry<NativeChunkArena>(schema, 0, static capacity => new NativeChunkArena(capacity)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArchetypeRegistry<NativeChunkArena>(schema, (1 << 29) + 1, static capacity => new NativeChunkArena(capacity)));

        using var registry = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity));
        Assert.Throws<ArgumentException>(() => registry.Register(full, default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Register(full, ChunkLayoutOptions.KiB16, 0));
        Assert.Throws<ArgumentException>(() => registry.Register([full[0], full[0]], ChunkLayoutOptions.KiB16, 1));
        Assert.Throws<ArgumentException>(() => registry.Register([full[1], full[0]], ChunkLayoutOptions.KiB16, 1));
        Assert.Throws<ArgumentException>(() => registry.Register([ComponentId.Invalid], ChunkLayoutOptions.KiB16, 1));
        Assert.Throws<ArgumentException>(() => registry.Register([new ComponentId(100)], ChunkLayoutOptions.KiB16, 1));

        ArchetypeId first = registry.Register([full[0]], ChunkLayoutOptions.KiB16, 1);
        Assert.Throws<ArgumentException>(() => registry.Register([full[0]], ChunkLayoutOptions.KiB32, 1));
        Assert.Throws<ArgumentException>(() => registry.Register([full[0]], ChunkLayoutOptions.KiB16, 2));
        Assert.False(registry.TryRegister([full[1]], ChunkLayoutOptions.KiB16, 1, out ArchetypeId unavailable));
        Assert.Equal(ArchetypeId.Invalid, unavailable);
        Assert.False(registry.TryGetId([full[1]], out ArchetypeId missing));
        Assert.Equal(ArchetypeId.Invalid, missing);
        Assert.False(registry.TryGetStorage(ArchetypeId.Invalid, out _));
        Assert.False(registry.TryGetStorage(new ArchetypeId(2), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.GetStorage(ArchetypeId.Invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.GetStorage(new ArchetypeId(2)));
        Assert.Equal(first, registry.GetStorage(first).Id);

        NativeChunkArena? rejectedArena = null;
        using var rejecting = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: 1,
            capacity =>
            {
                rejectedArena = new NativeChunkArena(capacity);
                _ = rejectedArena.Acquire();
                return rejectedArena;
            });
        Assert.Throws<ArgumentException>(() =>
            rejecting.Register([full[0]], ChunkLayoutOptions.KiB16, 1));
        Assert.True(rejectedArena is not null && rejectedArena.IsDisposed);
        Assert.Equal(0, rejecting.Count);

        NativeChunkArena? wrongCapacityArena = null;
        using var wrongCapacity = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: 1,
            capacity =>
            {
                wrongCapacityArena = new NativeChunkArena(new ChunkArenaCapacity(
                    capacity.ChunkSizeBytes,
                    capacity.ChunkAlignmentBytes,
                    maximumChunks: 2));
                return wrongCapacityArena;
            });
        Assert.Throws<ArgumentException>(() =>
            wrongCapacity.Register([full[0]], ChunkLayoutOptions.KiB16, 1));
        Assert.True(wrongCapacityArena is not null && wrongCapacityArena.IsDisposed);
        Assert.Equal(0, wrongCapacity.Count);
    }

    private static void ArchetypeRegistry_OpenAddressCollisionsPreserveFullSignatureIdentity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.IndexedTestSchema.CreateSchema();
        using var registry = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: 4,
            static capacity => new NativeChunkArena(capacity));
        int mask = registry.LookupCapacity - 1;
        var firstBySlot = new int[registry.LookupCapacity];
        Array.Fill(firstBySlot, -1);
        int leftIndex = -1;
        int rightIndex = -1;
        for (ushort component = 0; component < schema.ComponentCount; component++)
        {
            ComponentId[] signature = [new ComponentId(component)];
            int slot = ArchetypeSignatureHash.GetInitialSlot(
                ArchetypeSignatureHash.Compute(signature),
                mask);
            if (firstBySlot[slot] >= 0)
            {
                leftIndex = firstBySlot[slot];
                rightIndex = component;
                break;
            }

            firstBySlot[slot] = component;
        }

        Assert.True(leftIndex >= 0 && rightIndex >= 0);
        ComponentId[] left = [new ComponentId((ushort)leftIndex)];
        ComponentId[] right = [new ComponentId((ushort)rightIndex)];
        ArchetypeId leftId = registry.Register(left, ChunkLayoutOptions.KiB16, 1);
        ArchetypeId rightId = registry.Register(right, ChunkLayoutOptions.KiB16, 1);
        Assert.NotEqual(leftId, rightId);
        Assert.True(registry.TryGetId(left, out ArchetypeId foundLeft));
        Assert.True(registry.TryGetId(right, out ArchetypeId foundRight));
        Assert.Equal(leftId, foundLeft);
        Assert.Equal(rightId, foundRight);
        Assert.True(registry.GetStorage(leftId).Signature.SequenceEqual(left));
        Assert.True(registry.GetStorage(rightId).Signature.SequenceEqual(right));
    }

    private static void ArchetypeRegistry_RandomizedSealedLookupIsCorrectAndAllocationFree()
    {
        const int signatureCount = 128;
        const int randomLookups = 30_000;
        const int measuredLookups = 1_000_000;
        ComponentSchema schema = UltraSim.ECS.Tests.IndexedTestSchema.CreateSchema();
        using var registry = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            maximumArchetypes: signatureCount,
            static capacity => new NativeChunkArena(capacity));
        var signatures = new ComponentId[signatureCount][];
        var ids = new ArchetypeId[signatureCount];
        for (int mask = 0; mask < signatureCount; mask++)
        {
            signatures[mask] = CreateIndexedSignature(mask);
            ids[mask] = registry.Register(signatures[mask], ChunkLayoutOptions.KiB16, 1);
            Assert.Equal(new ArchetypeId((uint)mask + 1), ids[mask]);
        }
        registry.Seal();

        var random = new Random(unchecked((int)0xA2C4E7F1));
        for (int i = 0; i < randomLookups; i++)
        {
            int selected = random.Next(signatureCount);
            Assert.True(registry.TryGetId(signatures[selected], out ArchetypeId actual));
            Assert.Equal(ids[selected], actual);
            Assert.Equal(actual, registry.GetStorage(actual).Id);
        }

        Parallel.For(0, 4096, i =>
        {
            int selected = i & (signatureCount - 1);
            Assert.True(registry.TryGetId(signatures[selected], out ArchetypeId actual));
            Assert.Equal(ids[selected], actual);
            Assert.Equal(actual, registry.GetStorage(actual).Id);
        });

        for (int i = 0; i < 32; i++)
            _ = registry.TryGetId(signatures[i], out _);

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredLookups; i++)
        {
            int selected = i & (signatureCount - 1);
            if (registry.TryGetId(signatures[selected], out ArchetypeId id))
                checksum += registry.GetStorage(id).SignatureHash != 0 ? id.Value : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
        Assert.Equal(signatureCount, registry.Count);
    }

    private static void ArchetypeRegistry_IsolatesInstancesAndOwnsNativePinnedDisposal()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        ComponentId[] signature = [CreateLayoutSignature()[0]];
        var native = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            1,
            static capacity => new NativeChunkArena(capacity));
        var pinned = new ArchetypeRegistry<PinnedChunkArena>(
            schema,
            1,
            static capacity => new PinnedChunkArena(capacity));
        ArchetypeId nativeId = native.Register(signature, ChunkLayoutOptions.KiB16, 1);
        ArchetypeId pinnedId = pinned.Register(signature, ChunkLayoutOptions.KiB16, 1);
        NativeChunkArena nativeArena = native.GetStorage(nativeId).Store.Arena;
        PinnedChunkArena pinnedArena = pinned.GetStorage(pinnedId).Store.Arena;

        Assert.Equal(new ArchetypeId(1), nativeId);
        Assert.Equal(new ArchetypeId(1), pinnedId);
        Assert.NotEqual(nativeArena.ArenaId.Value, pinnedArena.ArenaId.Value);
        Assert.True(native.GetStorage(nativeId).Store.TryAdd(1, out _));
        Assert.True(pinned.GetStorage(pinnedId).Store.TryAdd(2, out _));

        native.Dispose();
        native.Dispose();
        Assert.True(native.IsDisposed);
        Assert.True(nativeArena.IsDisposed);
        Assert.False(pinned.IsDisposed);
        Assert.False(pinnedArena.IsDisposed);
        Assert.Equal(1, pinned.GetStorage(pinnedId).Store.Count);
        Assert.Throws<ObjectDisposedException>(() => native.GetStorage(nativeId));

        pinned.Dispose();
        pinned.Dispose();
        Assert.True(pinned.IsDisposed);
        Assert.True(pinnedArena.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => pinned.TryGetId(signature, out _));
    }

    private static void ArchetypeMigrator_PreservesRetainedStateDefaultsAdditionsAndRelocatesDonor()
    {
        using var fixture = new MigrationFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: 20_000);
        int count = fixture.Source.Layout.RowCapacity + 3;
        var entities = new Entity[count];
        for (int i = 0; i < entities.Length; i++)
            entities[i] = fixture.AddToSource();

        Entity victim = entities[0];
        Entity donor = entities[^1];
        Assert.True(fixture.Pool.TryGetLocation(victim, out EntityLocation sourceLocation));
        Assert.True(fixture.Pool.TryGetLocation(donor, out EntityLocation donorLocation));
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutByte>(
            sourceLocation.ChunkRow,
            fixture.SourceByte).Value = 91;
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
            sourceLocation.ChunkRow,
            fixture.SourceEnable).Value = 123_456_789;
        ref UltraSim.ECS.Tests.LayoutWide sourceWide = ref fixture.Source.Store
            .GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(sourceLocation.ChunkRow, fixture.SourceWide);
        sourceWide.A = 11;
        sourceWide.B = 22;
        sourceWide.C = 33;
        sourceWide.D = 44;
        fixture.Source.Store.SetEnabled(sourceLocation.ChunkRow, fixture.SourceEnable, enabled: false);

        Assert.Equal(2, fixture.SourceToDestination.RetainedComponentCount);
        Assert.Equal(2, fixture.SourceToDestination.StatefulTransferCount);
        Assert.Equal(2, fixture.SourceToDestination.AddedComponentCount);
        Assert.Equal(1, fixture.SourceToDestination.RemovedComponentCount);
        Assert.True(ArchetypeMigrator.TryMigrate(
            fixture.Pool,
            victim,
            fixture.SourceToDestination,
            out EntityLocation destinationLocation));

        Assert.Equal(fixture.Destination.Id, destinationLocation.ArchetypeId);
        Assert.True(fixture.Pool.TryGetLocation(victim, out EntityLocation recorded));
        Assert.Equal(destinationLocation, recorded);
        Assert.Equal(victim.Index, fixture.Destination.Store.GetEntityIndex(destinationLocation.ChunkRow));
        Assert.Equal(
            123_456_789L,
            fixture.Destination.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                destinationLocation.ChunkRow,
                fixture.DestinationEnable).Value);
        Assert.False(fixture.Destination.Store.IsEnabled(destinationLocation.ChunkRow, fixture.DestinationEnable));
        ref UltraSim.ECS.Tests.LayoutWide destinationWide = ref fixture.Destination.Store
            .GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(destinationLocation.ChunkRow, fixture.DestinationWide);
        Assert.Equal(11L, destinationWide.A);
        Assert.Equal(22L, destinationWide.B);
        Assert.Equal(33L, destinationWide.C);
        Assert.Equal(44L, destinationWide.D);
        Assert.Equal(
            0,
            fixture.Destination.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutInt>(
                destinationLocation.ChunkRow,
                fixture.DestinationInt).Value);
        Assert.True(fixture.Destination.Store.IsEnabled(destinationLocation.ChunkRow, fixture.DestinationTag));

        Assert.True(fixture.Pool.TryGetLocation(donor, out EntityLocation relocatedDonor));
        Assert.Equal(fixture.Source.Id, relocatedDonor.ArchetypeId);
        Assert.Equal(sourceLocation.ChunkRow, relocatedDonor.ChunkRow);
        Assert.Equal(donor.Index, fixture.Source.Store.GetEntityIndex(relocatedDonor.ChunkRow));
        Assert.NotEqual(donorLocation, relocatedDonor);
        Assert.Equal(count - 1, fixture.Source.Store.Count);
        Assert.Equal(1, fixture.Destination.Store.Count);
        Assert.Equal(count, fixture.Pool.AliveCount);
        Assert.Equal(count, fixture.Pool.LocatedCount);
        fixture.Source.Store.ValidateInvariants();
        fixture.Destination.Store.ValidateInvariants();
    }

    private static void ArchetypeMigrator_RejectsInvalidPlansAndLeavesSourceOnCapacityFailure()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using (var registry = new ArchetypeRegistry<NativeChunkArena>(
            schema,
            2,
            static capacity => new NativeChunkArena(capacity)))
        {
            ComponentId component = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
            ArchetypeId id = registry.Register([component], ChunkLayoutOptions.KiB16, 1);
            Assert.Throws<ArgumentException>(() => registry.PrepareTransition(id, id));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.PrepareTransition(id, new ArchetypeId(2)));
            ArchetypeId other = registry.Register(ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
            _ = registry.PrepareTransition(id, other);
            registry.Seal();
            Assert.Throws<InvalidOperationException>(() => registry.PrepareTransition(id, other));
        }

        using var fixture = new MigrationFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 20_000);
        int destinationCapacity = checked((int)fixture.Destination.Store.MaximumRows);
        for (int i = 0; i < destinationCapacity; i++)
            _ = fixture.AddToDestination();
        Entity source = fixture.AddToSource();
        Assert.True(fixture.Pool.TryGetLocation(source, out EntityLocation before));
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
            before.ChunkRow,
            fixture.SourceWide).A = 777;

        Assert.False(ArchetypeMigrator.TryMigrate(
            fixture.Pool,
            source,
            fixture.DestinationToSource,
            out EntityLocation wrongPlan));
        Assert.Equal(EntityLocation.Invalid, wrongPlan);
        Assert.False(ArchetypeMigrator.TryMigrate(
            fixture.Pool,
            source,
            fixture.SourceToDestination,
            out EntityLocation unavailable));
        Assert.Equal(EntityLocation.Invalid, unavailable);
        Assert.True(fixture.Pool.TryGetLocation(source, out EntityLocation after));
        Assert.Equal(before, after);
        Assert.Equal(source.Index, fixture.Source.Store.GetEntityIndex(after.ChunkRow));
        Assert.Equal(
            777L,
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
                after.ChunkRow,
                fixture.SourceWide).A);
        Assert.Equal(1, fixture.Source.Store.Count);
        Assert.Equal(destinationCapacity, fixture.Destination.Store.Count);
        fixture.Source.Store.ValidateInvariants();
        fixture.Destination.Store.ValidateInvariants();
    }

    private static void ArchetypeMigrator_RandomizedBidirectionalModelRemainsConsistent()
    {
        const int operations = 30_000;
        using var fixture = new MigrationFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: 20_000);
        int population = Math.Min(
            checked((int)fixture.Source.Store.MaximumRows),
            checked((int)fixture.Destination.Store.MaximumRows)) / 2;
        var entities = new Entity[population];
        var values = new long[population];
        var enabled = new bool[population];
        for (int i = 0; i < population; i++)
        {
            Entity entity = fixture.AddToSource();
            Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation location));
            long value = ((long)i << 32) ^ 0x51A7C0DE;
            bool isEnabled = (i & 1) == 0;
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                location.ChunkRow,
                fixture.SourceEnable).Value = value;
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
                location.ChunkRow,
                fixture.SourceWide).A = ~value;
            fixture.Source.Store.SetEnabled(location.ChunkRow, fixture.SourceEnable, isEnabled);
            entities[i] = entity;
            values[i] = value;
            enabled[i] = isEnabled;
        }

        var random = new Random(unchecked((int)0xC2055A17));
        for (int operation = 0; operation < operations; operation++)
        {
            int selected = random.Next(population);
            Entity entity = entities[selected];
            Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation before));
            ArchetypeTransitionPlan<NativeChunkArena> plan = before.ArchetypeId == fixture.Source.Id
                ? fixture.SourceToDestination
                : fixture.DestinationToSource;
            Assert.True(ArchetypeMigrator.TryMigrate(
                fixture.Pool,
                entity,
                plan,
                out EntityLocation after));
            Assert.Equal(plan.Destination.Id, after.ArchetypeId);
            Assert.Equal(entity.Index, plan.Destination.Store.GetEntityIndex(after.ChunkRow));

            ComponentColumnLayout enableColumn = after.ArchetypeId == fixture.Source.Id
                ? fixture.SourceEnable
                : fixture.DestinationEnable;
            ComponentColumnLayout wideColumn = after.ArchetypeId == fixture.Source.Id
                ? fixture.SourceWide
                : fixture.DestinationWide;
            ArchetypeChunkStore<NativeChunkArena> store = plan.Destination.Store;
            Assert.Equal(
                values[selected],
                store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(after.ChunkRow, enableColumn).Value);
            Assert.Equal(
                ~values[selected],
                store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(after.ChunkRow, wideColumn).A);
            Assert.Equal(enabled[selected], store.IsEnabled(after.ChunkRow, enableColumn));
            if (after.ArchetypeId == fixture.Source.Id)
            {
                Assert.Equal(
                    (byte)0,
                    store.GetComponentRef<UltraSim.ECS.Tests.LayoutByte>(after.ChunkRow, fixture.SourceByte).Value);
            }
            else
            {
                Assert.Equal(
                    0,
                    store.GetComponentRef<UltraSim.ECS.Tests.LayoutInt>(after.ChunkRow, fixture.DestinationInt).Value);
                Assert.True(store.IsEnabled(after.ChunkRow, fixture.DestinationTag));
            }

            if ((operation & 255) == 0)
            {
                Assert.Equal(population, fixture.Source.Store.Count + fixture.Destination.Store.Count);
                Assert.Equal(population, fixture.Pool.AliveCount);
                Assert.Equal(population, fixture.Pool.LocatedCount);
                fixture.Source.Store.ValidateInvariants();
                fixture.Destination.Store.ValidateInvariants();
            }
        }
    }

    private static void ArchetypeMigrator_NativeAndPinnedExecutionRemainEquivalent()
    {
        long native = RunMigrationParity<NativeChunkArena>();
        long pinned = RunMigrationParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void ArchetypeMigrator_WarmedBidirectionalChurnAllocatesZeroManagedBytes()
    {
        const int operations = 100_000;
        using var fixture = new MigrationFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 1);
        Entity entity = fixture.AddToSource();
        Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation initial));
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
            initial.ChunkRow,
            fixture.SourceEnable).Value = 0x1234_5678;
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
            initial.ChunkRow,
            fixture.SourceWide).A = 0x1020_3040;
        fixture.Source.Store.SetEnabled(initial.ChunkRow, fixture.SourceEnable, enabled: false);

        for (int i = 0; i < 32; i++)
        {
            ArchetypeTransitionPlan<NativeChunkArena> warmPlan = (i & 1) == 0
                ? fixture.SourceToDestination
                : fixture.DestinationToSource;
            Assert.True(ArchetypeMigrator.TryMigrate(fixture.Pool, entity, warmPlan, out _));
        }

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < operations; i++)
        {
            ArchetypeTransitionPlan<NativeChunkArena> plan = (i & 1) == 0
                ? fixture.SourceToDestination
                : fixture.DestinationToSource;
            if (!ArchetypeMigrator.TryMigrate(fixture.Pool, entity, plan, out EntityLocation location))
                throw new InvalidOperationException("Warmed migration unexpectedly failed.");
            checksum += location.ArchetypeId.Value + (uint)location.ChunkRow.Row;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum != 0);
        Assert.Equal(0L, allocated);
        Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation final));
        Assert.Equal(fixture.Source.Id, final.ArchetypeId);
        Assert.Equal(
            0x1234_5678L,
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                final.ChunkRow,
                fixture.SourceEnable).Value);
        Assert.Equal(
            0x1020_3040L,
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
                final.ChunkRow,
                fixture.SourceWide).A);
        Assert.False(fixture.Source.Store.IsEnabled(final.ChunkRow, fixture.SourceEnable));
    }

    private static void EcsWorld_LifecycleSealsRegistrationAndOwnsDisposal()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(32),
            maximumArchetypes: 2,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        ArchetypeId source = world.RegisterArchetype(
            [enableId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeId destination = world.RegisterArchetype(
            [enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeTransitionPlan<NativeChunkArena> plan = world.PrepareTransition(source, destination);
        NativeChunkArena sourceArena = world.GetArchetypeStorage(source).Store.Arena;
        NativeChunkArena destinationArena = world.GetArchetypeStorage(destination).Store.Arena;

        Assert.Equal(EcsWorldState.Created, world.State);
        Assert.False(world.IsSealed);
        Assert.False(world.IsDisposed);
        Assert.Equal(schema, world.Schema);
        Assert.Equal(2, world.ArchetypeCount);
        Assert.Throws<InvalidOperationException>(() => world.TryCreateDefault(source, out _));
        Assert.Throws<InvalidOperationException>(() => world.TryDestroy(Entity.Null));
        Assert.Throws<InvalidOperationException>(() => world.TryTransition(Entity.Null, plan, out _));

        world.Seal();
        world.Seal();
        Assert.Equal(EcsWorldState.Sealed, world.State);
        Assert.True(world.IsSealed);
        Assert.Throws<InvalidOperationException>(() => world.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1));
        Assert.Throws<InvalidOperationException>(() => world.PrepareTransition(source, destination));
        Assert.True(world.TryCreateDefault(source, out Entity entity));
        Assert.True(world.IsAlive(entity));

        world.Dispose();
        world.Dispose();
        Assert.Equal(EcsWorldState.Disposed, world.State);
        Assert.True(world.IsDisposed);
        Assert.False(world.IsSealed);
        Assert.True(sourceArena.IsDisposed);
        Assert.True(destinationArena.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = world.AliveCount);
        Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
        Assert.Throws<ObjectDisposedException>(() => world.TryCreateDefault(source, out _));
    }

    private static void EcsWorld_CreateDestroyMaintainsRowsLocationsAndGenerations()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: 20_000);
        int count = fixture.Source.Layout.RowCapacity + 3;
        var entities = new Entity[count];
        for (int i = 0; i < entities.Length; i++)
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out entities[i]));

        Entity victim = entities[0];
        Entity donor = entities[^1];
        Assert.True(fixture.World.TryGetLocation(victim, out EntityLocation victimLocation));
        Assert.True(fixture.World.TryGetLocation(donor, out EntityLocation donorLocation));
        fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutByte>(
            victimLocation.ChunkRow,
            fixture.SourceByte).Value = 0xA5;

        Assert.True(fixture.World.TryDestroy(victim));
        Assert.False(fixture.World.IsAlive(victim));
        Assert.False(fixture.World.TryGetLocation(victim, out EntityLocation removedLocation));
        Assert.Equal(EntityLocation.Invalid, removedLocation);
        Assert.False(fixture.World.TryDestroy(victim));
        Assert.True(fixture.World.TryGetLocation(donor, out EntityLocation relocatedDonor));
        Assert.Equal(victimLocation, relocatedDonor);
        Assert.NotEqual(donorLocation, relocatedDonor);
        Assert.Equal(donor.Index, fixture.Source.Store.GetEntityIndex(relocatedDonor.ChunkRow));

        Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity replacement));
        Assert.Equal(victim.Index, replacement.Index);
        Assert.NotEqual(victim.Generation, replacement.Generation);
        Assert.True(fixture.World.TryGetLocation(replacement, out EntityLocation replacementLocation));
        Assert.Equal(
            (byte)0,
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutByte>(
                replacementLocation.ChunkRow,
                fixture.SourceByte).Value);
        Assert.Equal(count, fixture.World.AliveCount);
        Assert.Equal(count, fixture.World.LocatedCount);
        Assert.Equal(count, fixture.Source.Store.Count);
        fixture.Source.Store.ValidateInvariants();
    }

    private static void EcsWorld_CapacityFailuresAreAtomicAndDoNotBurnSlots()
    {
        using (var entityBound = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 1))
        {
            Assert.True(entityBound.World.TryCreateDefault(entityBound.Source.Id, out Entity only));
            Assert.False(entityBound.World.TryCreateDefault(entityBound.Destination.Id, out Entity unavailable));
            Assert.Equal(Entity.Null, unavailable);
            Assert.Equal(1, entityBound.World.AliveCount);
            Assert.Equal(1, entityBound.World.LocatedCount);
            Assert.Equal(1, entityBound.Source.Store.Count);
            Assert.Equal(0, entityBound.Destination.Store.Count);
            Assert.True(entityBound.World.IsAlive(only));
        }

        using var rowBound = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 20_000);
        int sourceCapacity = checked((int)rowBound.Source.Store.MaximumRows);
        for (int i = 0; i < sourceCapacity; i++)
            Assert.True(rowBound.World.TryCreateDefault(rowBound.Source.Id, out _));

        Assert.False(rowBound.World.TryCreateDefault(rowBound.Source.Id, out Entity full));
        Assert.Equal(Entity.Null, full);
        Assert.Equal(sourceCapacity, rowBound.World.AliveCount);
        Assert.Equal(sourceCapacity, rowBound.World.LocatedCount);
        Assert.True(rowBound.World.TryCreateDefault(rowBound.Destination.Id, out Entity next));
        Assert.Equal((uint)sourceCapacity, next.Index);
        Assert.Equal(1u, next.Generation);
        Assert.Equal(sourceCapacity + 1, rowBound.World.AliveCount);
        Assert.Equal(sourceCapacity, rowBound.Source.Store.Count);
        Assert.Equal(1, rowBound.Destination.Store.Count);
    }

    private static void EcsWorld_RejectsForeignEntitiesAndTransitionPlans()
    {
        using var first = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 16);
        using var second = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 16);
        Assert.Equal(first.Source.Id, second.Source.Id);
        Assert.Equal(first.Destination.Id, second.Destination.Id);
        Assert.NotEqual(first.World.Id, second.World.Id);
        Assert.True(first.World.TryCreateDefault(first.Source.Id, out Entity firstEntity));
        Assert.True(second.World.TryCreateDefault(second.Source.Id, out Entity secondEntity));

        Assert.False(first.World.IsAlive(secondEntity));
        Assert.False(first.World.TryDestroy(secondEntity));
        Assert.False(first.World.TryTransition(
            secondEntity,
            first.SourceToDestination,
            out EntityLocation foreignEntityLocation));
        Assert.Equal(EntityLocation.Invalid, foreignEntityLocation);
        Assert.Throws<ArgumentException>(() => first.World.TryTransition(
            firstEntity,
            second.SourceToDestination,
            out _));

        Assert.True(first.World.IsAlive(firstEntity));
        Assert.True(second.World.IsAlive(secondEntity));
        Assert.Equal(1, first.Source.Store.Count);
        Assert.Equal(1, second.Source.Store.Count);
        Assert.Equal(0, first.Destination.Store.Count);
        Assert.Equal(0, second.Destination.Store.Count);
    }

    private static void EcsWorld_RandomizedLifecycleMatchesReferenceModel()
    {
        const int operations = 30_000;
        const int maximumPopulation = 512;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 4, entityCapacity: 1_024);
        var entities = new List<Entity>(maximumPopulation);
        var inSource = new List<bool>(maximumPopulation);
        var values = new List<long>(maximumPopulation);
        var random = new Random(unchecked((int)0x91C7D20B));
        Entity lastDestroyed = Entity.Null;

        for (int operation = 0; operation < operations; operation++)
        {
            int choice = random.Next(100);
            if (entities.Count == 0 || (choice < 35 && entities.Count < maximumPopulation))
            {
                bool source = random.Next(2) == 0;
                ArchetypeStorage<NativeChunkArena> storage = source
                    ? fixture.Source
                    : fixture.Destination;
                Assert.True(fixture.World.TryCreateDefault(storage.Id, out Entity entity));
                Assert.True(fixture.World.TryGetLocation(entity, out EntityLocation location));
                long value = unchecked(((long)entity.Generation << 32) ^ entity.Index ^ operation);
                ComponentColumnLayout enableColumn = source
                    ? fixture.SourceEnable
                    : fixture.DestinationEnable;
                storage.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                    location.ChunkRow,
                    enableColumn).Value = value;
                storage.Store.SetEnabled(location.ChunkRow, enableColumn, (operation & 1) == 0);
                entities.Add(entity);
                inSource.Add(source);
                values.Add(value);
            }
            else
            {
                int selected = random.Next(entities.Count);
                Entity entity = entities[selected];
                if (choice < 78)
                {
                    ArchetypeTransitionPlan<NativeChunkArena> plan = inSource[selected]
                        ? fixture.SourceToDestination
                        : fixture.DestinationToSource;
                    Assert.True(fixture.World.TryTransition(entity, plan, out EntityLocation destination));
                    inSource[selected] = !inSource[selected];
                    ArchetypeStorage<NativeChunkArena> storage = inSource[selected]
                        ? fixture.Source
                        : fixture.Destination;
                    ComponentColumnLayout enableColumn = inSource[selected]
                        ? fixture.SourceEnable
                        : fixture.DestinationEnable;
                    Assert.Equal(storage.Id, destination.ArchetypeId);
                    Assert.Equal(
                        values[selected],
                        storage.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                            destination.ChunkRow,
                            enableColumn).Value);
                }
                else
                {
                    Assert.True(fixture.World.TryDestroy(entity));
                    lastDestroyed = entity;
                    int last = entities.Count - 1;
                    entities[selected] = entities[last];
                    inSource[selected] = inSource[last];
                    values[selected] = values[last];
                    entities.RemoveAt(last);
                    inSource.RemoveAt(last);
                    values.RemoveAt(last);
                }
            }

            if ((operation & 255) == 0)
            {
                Assert.Equal(entities.Count, fixture.World.AliveCount);
                Assert.Equal(entities.Count, fixture.World.LocatedCount);
                Assert.Equal(
                    entities.Count,
                    fixture.Source.Store.Count + fixture.Destination.Store.Count);
                if (lastDestroyed.IsValid)
                    Assert.False(fixture.World.IsAlive(lastDestroyed));
                for (int i = 0; i < entities.Count; i++)
                {
                    Assert.True(fixture.World.TryGetLocation(entities[i], out EntityLocation location));
                    Assert.Equal(
                        inSource[i] ? fixture.Source.Id : fixture.Destination.Id,
                        location.ArchetypeId);
                }
                fixture.Source.Store.ValidateInvariants();
                fixture.Destination.Store.ValidateInvariants();
            }
        }
    }

    private static void EcsWorld_NativeAndPinnedLifecycleRemainEquivalent()
    {
        long native = RunWorldLifecycleParity<NativeChunkArena>();
        long pinned = RunWorldLifecycleParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedCreateTransitionDestroyAllocatesZeroManagedBytes()
    {
        const int operations = 100_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 1);

        for (int i = 0; i < 32; i++)
        {
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity warm));
            Assert.True(fixture.World.TryTransition(warm, fixture.SourceToDestination, out _));
            Assert.True(fixture.World.TryDestroy(warm));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
            Assert.True(fixture.World.TryTransition(
                entity,
                fixture.SourceToDestination,
                out EntityLocation destination));
            checksum = unchecked(checksum + entity.Generation + destination.ChunkRow.Row);
            Assert.True(fixture.World.TryDestroy(entity));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(checksum != 0);
        Assert.Equal(0, fixture.World.AliveCount);
        Assert.Equal(0, fixture.World.LocatedCount);
        Assert.Equal(0, fixture.Source.Store.Count);
        Assert.Equal(0, fixture.Destination.Store.Count);
    }

    private static void ArchetypeComponentBinding_LayoutAndGeneratedBindingAreExplicit()
    {
        Assert.Equal(16, Unsafe.SizeOf<ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide>>());
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> invalid = default;
        Assert.False(invalid.IsValid);
        Assert.False(invalid.HasData);
        Assert.False(invalid.IsEnableable);

        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        ArchetypeId archetype = world.RegisterArchetype(
            [byteId, enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> byteBinding =
            world.BindComponent<UltraSim.ECS.Tests.LayoutByte>(archetype, byteId);
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> duplicateByteBinding =
            world.BindComponent<UltraSim.ECS.Tests.LayoutByte>(archetype, byteId);
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding =
            world.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(archetype, enableId);

        Assert.True(byteBinding.IsValid);
        Assert.Equal(world.Id, byteBinding.WorldId);
        Assert.Equal(archetype, byteBinding.ArchetypeId);
        Assert.Equal(byteId, byteBinding.ComponentId);
        Assert.True(byteBinding.HasData);
        Assert.False(byteBinding.IsEnableable);
        Assert.Equal(byteBinding, duplicateByteBinding);
        Assert.True(enableBinding.HasData);
        Assert.True(enableBinding.IsEnableable);
        Assert.Equal(
            world.GetArchetypeStorage(archetype).Store.GetColumnIndex(enableId),
            enableBinding.ColumnIndex);
        Assert.Throws<ArgumentException>(() =>
            world.BindComponent<UltraSim.ECS.Tests.LayoutByte>(archetype, intId));
        Assert.Throws<ArgumentException>(() =>
            world.BindComponent<UltraSim.ECS.Tests.LayoutTag>(archetype, tagId));

        world.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            world.BindComponent<UltraSim.ECS.Tests.LayoutWide>(archetype, wideId));
    }

    private static void EcsWorld_BoundAccessReadsWritesAndInitializes()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 8);
        var initial = new UltraSim.ECS.Tests.LayoutWide
        {
            A = 11,
            B = 22,
            C = 33,
            D = 44,
        };
        Assert.True(fixture.World.TryCreateInitialized(
            fixture.Source.Id,
            fixture.SourceWideBinding,
            initial,
            out Entity entity));
        Assert.True(fixture.World.HasComponent(entity, fixture.SourceWideBinding));
        Assert.True(fixture.World.HasComponent(entity, fixture.SourceByteBinding));
        Assert.False(fixture.World.HasComponent(entity, fixture.DestinationIntBinding));
        Assert.True(fixture.World.TryGetComponent(
            entity,
            fixture.SourceWideBinding,
            out UltraSim.ECS.Tests.LayoutWide read));
        Assert.Equal(11L, read.A);
        Assert.Equal(22L, read.B);
        Assert.Equal(33L, read.C);
        Assert.Equal(44L, read.D);

        var byteValue = new UltraSim.ECS.Tests.LayoutByte { Value = 0xC7 };
        Assert.True(fixture.World.TrySetComponent(entity, fixture.SourceByteBinding, byteValue));
        Assert.True(fixture.World.TryGetComponent(
            entity,
            fixture.SourceByteBinding,
            out UltraSim.ECS.Tests.LayoutByte readByte));
        Assert.Equal((byte)0xC7, readByte.Value);
        Assert.True(fixture.World.TryGetComponentEnabled(
            entity,
            fixture.SourceEnableBinding,
            out bool enabled));
        Assert.True(enabled);
        Assert.True(fixture.World.TrySetComponentEnabled(
            entity,
            fixture.SourceEnableBinding,
            enabled: false));
        Assert.True(fixture.World.TryGetComponentEnabled(
            entity,
            fixture.SourceEnableBinding,
            out enabled));
        Assert.False(enabled);

        int before = fixture.World.AliveCount;
        Assert.Throws<ArgumentException>(() => fixture.World.TryCreateInitialized(
            fixture.Source.Id,
            fixture.DestinationIntBinding,
            new UltraSim.ECS.Tests.LayoutInt { Value = 9 },
            out _));
        Assert.Equal(before, fixture.World.AliveCount);
    }

    private static void EcsWorld_BoundAccessRejectsTagsNonEnableableAndInvalidBindings()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.Destination.Id, out Entity destination));
        Assert.True(fixture.World.HasComponent(destination, fixture.DestinationTagBinding));
        Assert.False(fixture.DestinationTagBinding.HasData);
        Assert.True(fixture.DestinationTagBinding.IsEnableable);
        Assert.Throws<InvalidOperationException>(() => fixture.World.TryGetComponent(
            destination,
            fixture.DestinationTagBinding,
            out _));
        Assert.Throws<InvalidOperationException>(() => fixture.World.TrySetComponent(
            destination,
            fixture.DestinationTagBinding,
            default));
        Assert.True(fixture.World.TryGetComponentEnabled(
            destination,
            fixture.DestinationTagBinding,
            out bool tagEnabled));
        Assert.True(tagEnabled);
        Assert.True(fixture.World.TrySetComponentEnabled(
            destination,
            fixture.DestinationTagBinding,
            enabled: false));

        Assert.Throws<InvalidOperationException>(() => fixture.World.TryGetComponentEnabled(
            destination,
            fixture.DestinationIntBinding,
            out _));
        Assert.Throws<InvalidOperationException>(() => fixture.World.TrySetComponentEnabled(
            destination,
            fixture.DestinationIntBinding,
            enabled: false));
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> invalid = default;
        Assert.Throws<ArgumentException>(() => fixture.World.HasComponent(destination, invalid));
    }

    private static void EcsWorld_BoundAccessTracksExactArchetypeAcrossTransitions()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 2);
        var initial = new UltraSim.ECS.Tests.LayoutEnableLong { Value = 0x1234_5678 };
        Assert.True(fixture.World.TryCreateInitialized(
            fixture.Source.Id,
            fixture.SourceEnableBinding,
            initial,
            out Entity entity));
        Assert.True(fixture.World.TrySetComponentEnabled(
            entity,
            fixture.SourceEnableBinding,
            enabled: false));
        Assert.False(fixture.World.TryGetComponent(
            entity,
            fixture.DestinationEnableBinding,
            out _));

        Assert.True(fixture.World.TryTransition(
            entity,
            fixture.SourceToDestination,
            out _));
        Assert.False(fixture.World.HasComponent(entity, fixture.SourceEnableBinding));
        Assert.False(fixture.World.TryGetComponent(
            entity,
            fixture.SourceEnableBinding,
            out UltraSim.ECS.Tests.LayoutEnableLong staleSource));
        Assert.Equal(0L, staleSource.Value);
        Assert.True(fixture.World.TryGetComponent(
            entity,
            fixture.DestinationEnableBinding,
            out UltraSim.ECS.Tests.LayoutEnableLong retained));
        Assert.Equal(initial.Value, retained.Value);
        Assert.True(fixture.World.TryGetComponentEnabled(
            entity,
            fixture.DestinationEnableBinding,
            out bool enabled));
        Assert.False(enabled);
        Assert.True(fixture.World.TryGetComponent(
            entity,
            fixture.DestinationIntBinding,
            out UltraSim.ECS.Tests.LayoutInt added));
        Assert.Equal(0, added.Value);
        Assert.True(fixture.World.TryGetComponentEnabled(
            entity,
            fixture.DestinationTagBinding,
            out bool tagEnabled));
        Assert.True(tagEnabled);
    }

    private static void EcsWorld_BoundAccessRejectsForeignWorldAndStaleEntities()
    {
        using var first = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 2);
        using var second = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 2);
        Assert.True(first.World.TryCreateDefault(first.Source.Id, out Entity entity));
        Assert.Throws<ArgumentException>(() => second.World.HasComponent(entity, first.SourceByteBinding));
        Assert.Throws<ArgumentException>(() => second.World.TryGetComponent(
            entity,
            first.SourceByteBinding,
            out _));
        Assert.False(first.World.TryGetComponent(
            new Entity(second.World.Id, entity.Index, entity.Generation),
            first.SourceByteBinding,
            out _));

        Assert.True(first.World.TryDestroy(entity));
        Assert.False(first.World.HasComponent(entity, first.SourceByteBinding));
        Assert.False(first.World.TryGetComponent(
            entity,
            first.SourceByteBinding,
            out UltraSim.ECS.Tests.LayoutByte stale));
        Assert.Equal((byte)0, stale.Value);
        Assert.False(first.World.TrySetComponent(
            entity,
            first.SourceByteBinding,
            new UltraSim.ECS.Tests.LayoutByte { Value = 1 }));
    }

    private static void EcsWorld_BoundAccessRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int operations = 30_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var values = new long[entityCount];
        var enabled = new bool[entityCount];
        var inSource = new bool[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out entities[i]));
            values[i] = i * 31L;
            enabled[i] = (i & 1) == 0;
            inSource[i] = true;
            Assert.True(fixture.World.TrySetComponent(
                entities[i],
                fixture.SourceEnableBinding,
                new UltraSim.ECS.Tests.LayoutEnableLong { Value = values[i] }));
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[i],
                fixture.SourceEnableBinding,
                enabled[i]));
        }

        var random = new Random(unchecked((int)0xB17DACC5));
        for (int operation = 0; operation < operations; operation++)
        {
            int selected = random.Next(entityCount);
            Entity entity = entities[selected];
            int choice = random.Next(4);
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = inSource[selected]
                ? fixture.SourceEnableBinding
                : fixture.DestinationEnableBinding;
            if (choice == 0)
            {
                values[selected] = unchecked(values[selected] * 33 + operation + 1);
                Assert.True(fixture.World.TrySetComponent(
                    entity,
                    binding,
                    new UltraSim.ECS.Tests.LayoutEnableLong { Value = values[selected] }));
            }
            else if (choice == 1)
            {
                enabled[selected] = !enabled[selected];
                Assert.True(fixture.World.TrySetComponentEnabled(
                    entity,
                    binding,
                    enabled[selected]));
            }
            else if (choice == 2)
            {
                ArchetypeTransitionPlan<NativeChunkArena> plan = inSource[selected]
                    ? fixture.SourceToDestination
                    : fixture.DestinationToSource;
                Assert.True(fixture.World.TryTransition(entity, plan, out _));
                inSource[selected] = !inSource[selected];
            }
            else
            {
                Assert.True(fixture.World.TryGetComponent(
                    entity,
                    binding,
                    out UltraSim.ECS.Tests.LayoutEnableLong value));
                Assert.Equal(values[selected], value.Value);
                Assert.True(fixture.World.TryGetComponentEnabled(entity, binding, out bool actualEnabled));
                Assert.Equal(enabled[selected], actualEnabled);
            }

            if ((operation & 255) == 0)
            {
                for (int i = 0; i < entityCount; i++)
                {
                    ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> currentBinding = inSource[i]
                        ? fixture.SourceEnableBinding
                        : fixture.DestinationEnableBinding;
                    Assert.True(fixture.World.TryGetComponent(
                        entities[i],
                        currentBinding,
                        out UltraSim.ECS.Tests.LayoutEnableLong value));
                    Assert.Equal(values[i], value.Value);
                    Assert.True(fixture.World.TryGetComponentEnabled(
                        entities[i],
                        currentBinding,
                        out bool actualEnabled));
                    Assert.Equal(enabled[i], actualEnabled);
                }
                fixture.Source.Store.ValidateInvariants();
                fixture.Destination.Store.ValidateInvariants();
            }
        }
    }

    private static void EcsWorld_BoundAccessNativePinnedParity()
    {
        long native = RunBoundAccessParity<NativeChunkArena>();
        long pinned = RunBoundAccessParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedBoundAccessAllocatesZeroManagedBytes()
    {
        const int operations = 1_000_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 1);
        Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
        for (int i = 0; i < 32; i++)
        {
            Assert.True(fixture.World.TrySetComponent(
                entity,
                fixture.SourceEnableBinding,
                new UltraSim.ECS.Tests.LayoutEnableLong { Value = i }));
            Assert.True(fixture.World.TryGetComponent(
                entity,
                fixture.SourceEnableBinding,
                out _));
            Assert.True(fixture.World.TrySetComponentEnabled(
                entity,
                fixture.SourceEnableBinding,
                (i & 1) == 0));
            Assert.True(fixture.World.TryGetComponentEnabled(
                entity,
                fixture.SourceEnableBinding,
                out _));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            var value = new UltraSim.ECS.Tests.LayoutEnableLong { Value = i * 17L };
            if (!fixture.World.TrySetComponent(entity, fixture.SourceEnableBinding, value) ||
                !fixture.World.TryGetComponent(
                    entity,
                    fixture.SourceEnableBinding,
                    out UltraSim.ECS.Tests.LayoutEnableLong read) ||
                !fixture.World.TrySetComponentEnabled(
                    entity,
                    fixture.SourceEnableBinding,
                    (i & 1) == 0) ||
                !fixture.World.TryGetComponentEnabled(
                    entity,
                    fixture.SourceEnableBinding,
                    out bool isEnabled))
            {
                throw new InvalidOperationException("Warmed bound component access unexpectedly failed.");
            }
            checksum = unchecked(checksum + read.Value + (isEnabled ? 1 : 0));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(checksum != 0);
    }

    private static void EcsWorld_ChunkReadVisitsExactPackedRowsAndMetadata()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: 2_000);
        int count = fixture.Source.Store.Layout.RowCapacity + 17;
        var seen = new bool[count];
        long expectedChecksum = 0;
        for (int i = 0; i < count; i++)
        {
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                fixture.SourceEnableBinding,
                new UltraSim.ECS.Tests.LayoutEnableLong { Value = i * 13L + 7 }));
            expectedChecksum += i * 13L + 7;
        }

        var visitor = new ChunkReadAuditVisitor(
            fixture.World.Id,
            fixture.Source.Id,
            fixture.Source.Store.Layout.RowCapacity,
            seen);
        int visited = fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref visitor);

        Assert.Equal(count, visited);
        Assert.Equal(count, visitor.Rows);
        Assert.Equal(2, visitor.Chunks);
        Assert.Equal(expectedChecksum, visitor.Checksum);
        for (int i = 0; i < seen.Length; i++)
            Assert.True(seen[i]);

        var empty = new ChunkCountVisitor<UltraSim.ECS.Tests.LayoutInt>();
        Assert.Equal(0, fixture.World.VisitReadChunks(fixture.DestinationIntBinding, ref empty));
        Assert.Equal(0, empty.Chunks);
    }

    private static void EcsWorld_ChunkWriteUpdatesDataAndEnableState()
    {
        const int count = 257;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 2, entityCapacity: count);
        var entities = new Entity[count];
        for (int i = 0; i < count; i++)
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out entities[i]));

        var writer = new EnableChunkWriter(salt: 0x1234);
        Assert.Equal(count, fixture.World.VisitWriteChunks(fixture.SourceEnableBinding, ref writer));
        Assert.Equal(count, writer.Rows);
        Assert.True(writer.Chunks > 0);

        for (int i = 0; i < count; i++)
        {
            Assert.True(fixture.World.TryGetComponent(
                entities[i],
                fixture.SourceEnableBinding,
                out UltraSim.ECS.Tests.LayoutEnableLong value));
            Assert.Equal(0x1234L + (entities[i].Index * 31L), value.Value);
            Assert.True(fixture.World.TryGetComponentEnabled(
                entities[i],
                fixture.SourceEnableBinding,
                out bool enabled));
            Assert.Equal((entities[i].Index & 1) == 0, enabled);
        }
    }

    private static void EcsWorld_ChunkViewsRejectTagsForeignBindingsAndInvalidOperations()
    {
        using var first = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 4);
        using var second = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 4);
        Assert.True(first.World.TryCreateDefault(first.Source.Id, out _));

        var tagVisitor = new ChunkCountVisitor<UltraSim.ECS.Tests.LayoutTag>();
        Assert.Throws<InvalidOperationException>(() =>
            first.World.VisitReadChunks(first.DestinationTagBinding, ref tagVisitor));

        var foreignVisitor = new ChunkCountVisitor<UltraSim.ECS.Tests.LayoutEnableLong>();
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadChunks(first.SourceEnableBinding, ref foreignVisitor));

        var ordinaryWriter = new OrdinaryChunkMisuseVisitor();
        Assert.Equal(1, first.World.VisitWriteChunks(first.SourceByteBinding, ref ordinaryWriter));
        Assert.True(ordinaryWriter.NonEnableableRejected);
        Assert.True(ordinaryWriter.RowBoundsRejected);
        Assert.True(ordinaryWriter.NegativeRowBoundsRejected);
    }

    private static void EcsWorld_ChunkVisitEnforcesBarrierAndReleasesAfterException()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
        var probe = new ChunkBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.SourceEnableBinding,
            fixture.SourceToDestination,
            entity);
        Assert.Equal(1, fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref probe));
        Assert.Equal(7, probe.Rejections);
        Assert.True(fixture.World.IsAlive(entity));

        var throwing = new ThrowingChunkVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref throwing));

        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.SourceEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 91 }));
        Assert.True(fixture.World.TryTransition(entity, fixture.SourceToDestination, out _));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_ChunkVisitRandomizedModelRemainsConsistent()
    {
        const int entityCount = 513;
        const int rounds = 2_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 3, entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new long[entityCount];
        var enabled = new bool[entityCount];
        for (int i = 0; i < entityCount; i++)
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out entities[i]));

        var random = new Random(unchecked((int)0xC11A5EED));
        for (int round = 0; round < rounds; round++)
        {
            long salt = random.NextInt64();
            var writer = new EnableChunkWriter(salt);
            Assert.Equal(entityCount, fixture.World.VisitWriteChunks(fixture.SourceEnableBinding, ref writer));
            for (int i = 0; i < entityCount; i++)
            {
                expected[i] = unchecked(salt + (entities[i].Index * 31L));
                enabled[i] = (entities[i].Index & 1) == 0;
            }

            int selected = random.Next(entityCount);
            long overrideValue = random.NextInt64();
            Assert.True(fixture.World.TrySetComponent(
                entities[selected],
                fixture.SourceEnableBinding,
                new UltraSim.ECS.Tests.LayoutEnableLong { Value = overrideValue }));
            expected[selected] = overrideValue;
            bool overrideEnabled = random.Next(2) == 0;
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[selected],
                fixture.SourceEnableBinding,
                overrideEnabled));
            enabled[selected] = overrideEnabled;

            if ((round & 63) == 0)
            {
                var audit = new ChunkModelAuditVisitor(expected, enabled);
                Assert.Equal(entityCount, fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref audit));
                Assert.Equal(entityCount, audit.Rows);
            }
        }
    }

    private static void EcsWorld_ChunkVisitNativePinnedParity()
    {
        long native = RunChunkVisitParity<NativeChunkArena>();
        long pinned = RunChunkVisitParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedChunkVisitsAllocateZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 10_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 4, entityCapacity: entityCount);
        for (int i = 0; i < entityCount; i++)
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out _));

        var writer = new EnableChunkWriter(1);
        var reader = new ChunkChecksumVisitor();
        for (int i = 0; i < 32; i++)
        {
            writer.Salt = i;
            fixture.World.VisitWriteChunks(fixture.SourceEnableBinding, ref writer);
            fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref reader);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < rounds; i++)
        {
            writer.Salt = i * 17L;
            fixture.World.VisitWriteChunks(fixture.SourceEnableBinding, ref writer);
            fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref reader);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(reader.Checksum != 0);
    }

    private static long RunChunkVisitParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 1_000;
        using var fixture = new WorldFixture<TArena>(maximumChunks: 4, entityCapacity: entityCount);
        for (int i = 0; i < entityCount; i++)
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out _));

        var writer = new EnableChunkWriter(0);
        var reader = new ChunkChecksumVisitor();
        for (int round = 0; round < rounds; round++)
        {
            writer.Salt = round * 97L;
            Assert.Equal(entityCount, fixture.World.VisitWriteChunks(fixture.SourceEnableBinding, ref writer));
            Assert.Equal(entityCount, fixture.World.VisitReadChunks(fixture.SourceEnableBinding, ref reader));
        }

        fixture.Source.Store.ValidateInvariants();
        return unchecked(reader.Checksum + writer.Rows + writer.Chunks);
    }

    private static void ArchetypeReadWriteQueryPlan_SealCompilesCompleteArchetypeSet()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(32),
            maximumArchetypes: 3,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 2);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        ArchetypeId first = world.RegisterArchetype(
            [enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeReadWriteQueryPlan<NativeChunkArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> plan =
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId);
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);

        ArchetypeId second = world.RegisterArchetype(
            [byteId, enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [byteId, enableId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        world.Seal();

        Assert.True(plan.IsCompiled);
        Assert.Equal(1, world.QueryPlanCount);
        Assert.Equal(2, world.MaximumQueryPlans);
        Assert.Equal(2, plan.MatchedArchetypeCount);
        Assert.Equal(first, plan.Routes[0].Storage.Id);
        Assert.Equal(second, plan.Routes[1].Storage.Id);
        Assert.Equal(wideId, plan.ReadComponentId);
        Assert.Equal(enableId, plan.WriteComponentId);
        Assert.False(plan.ReadIsEnableable);
        Assert.True(plan.WriteIsEnableable);
        world.Dispose();
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);
    }

    private static void EcsWorld_ReadWriteQueryVisitsEveryMatchedArchetype()
    {
        const int sourceCount = 193;
        const int destinationCount = 129;
        using var fixture = new WorldFixture<NativeChunkArena>(
            maximumChunks: 2,
            entityCapacity: sourceCount + destinationCount);
        var entities = new Entity[sourceCount + destinationCount];
        var expected = new long[entities.Length];
        var enabled = new bool[entities.Length];
        for (int i = 0; i < sourceCount; i++)
        {
            var wide = new UltraSim.ECS.Tests.LayoutWide { A = i * 17L + 3 };
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Source.Id,
                fixture.SourceWideBinding,
                wide,
                out entities[i]));
        }
        for (int i = 0; i < destinationCount; i++)
        {
            int index = sourceCount + i;
            var wide = new UltraSim.ECS.Tests.LayoutWide { A = index * 17L + 3 };
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Destination.Id,
                fixture.DestinationWideBinding,
                wide,
                out entities[index]));
        }

        var visitor = new WideReadEnableWriteVisitor(
            fixture.Source.Id,
            fixture.Destination.Id,
            salt: 41);
        int visited = fixture.World.VisitReadWriteChunks(
            fixture.WideReadEnableWriteQuery,
            ref visitor);
        Assert.Equal(entities.Length, visited);
        Assert.Equal(entities.Length, visitor.Rows);
        Assert.Equal(sourceCount, visitor.SourceRows);
        Assert.Equal(destinationCount, visitor.DestinationRows);

        for (int i = 0; i < entities.Length; i++)
        {
            expected[i] = unchecked((i * 17L + 3) * 3L + entities[i].Index + 41);
            enabled[i] = ((i * 17L + 3) & 1) == 0;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = i < sourceCount
                ? fixture.SourceEnableBinding
                : fixture.DestinationEnableBinding;
            Assert.True(fixture.World.TryGetComponent(
                entities[i],
                binding,
                out UltraSim.ECS.Tests.LayoutEnableLong value));
            Assert.Equal(expected[i], value.Value);
            Assert.True(fixture.World.TryGetComponentEnabled(entities[i], binding, out bool actualEnabled));
            Assert.Equal(enabled[i], actualEnabled);
        }
    }

    private static void EcsWorld_ReadWriteQueryRejectsAliasesTagsForeignPlansAndCapacity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        _ = world.RegisterArchetype(
            [enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutWide>(
                wideId,
                wideId));
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutTag, UltraSim.ECS.Tests.LayoutInt>(
                tagId,
                intId));
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutInt>(
                intId,
                wideId));
        _ = world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
            wideId,
            enableId);
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutInt>(
                wideId,
                intId));
        world.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId));

        using var first = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 2);
        using var second = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 2);
        var visitor = new WideReadEnableWriteVisitor(first.Source.Id, first.Destination.Id, salt: 0);
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadWriteChunks(first.WideReadEnableWriteQuery, ref visitor));

        using var queryless = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        Assert.Equal(0, queryless.MaximumQueryPlans);
        Assert.Throws<InvalidOperationException>(() =>
            queryless.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: (1 << 20) + 1));
    }

    private static void EcsWorld_ReadWriteQueryEnforcesBarrierAndReleasesAfterException()
    {
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 1, entityCapacity: 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
        var probe = new ReadWriteBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.WideReadEnableWriteQuery,
            fixture.SourceEnableBinding,
            entity);
        Assert.Equal(1, fixture.World.VisitReadWriteChunks(
            fixture.WideReadEnableWriteQuery,
            ref probe));
        Assert.Equal(3, probe.Rejections);

        var throwing = new ThrowingReadWriteVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadWriteChunks(fixture.WideReadEnableWriteQuery, ref throwing));
        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.SourceEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 77 }));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_ReadWriteQueryRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 1_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 3, entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var inSource = new bool[entityCount];
        var wideValues = new long[entityCount];
        var expected = new long[entityCount];
        var enabled = new bool[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            wideValues[i] = i * 29L + 5;
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Source.Id,
                fixture.SourceWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
            inSource[i] = true;
        }

        var random = new Random(unchecked((int)0xA17C0DE5));
        for (int round = 0; round < rounds; round++)
        {
            long salt = random.NextInt64();
            var writer = new WideReadEnableWriteVisitor(
                fixture.Source.Id,
                fixture.Destination.Id,
                salt);
            Assert.Equal(entityCount, fixture.World.VisitReadWriteChunks(
                fixture.WideReadEnableWriteQuery,
                ref writer));
            for (int i = 0; i < entityCount; i++)
            {
                expected[i] = unchecked(wideValues[i] * 3L + entities[i].Index + salt);
                enabled[i] = (wideValues[i] & 1) == 0;
            }

            int transitionCount = random.Next(1, 6);
            for (int move = 0; move < transitionCount; move++)
            {
                int selected = random.Next(entityCount);
                ArchetypeTransitionPlan<NativeChunkArena> transition = inSource[selected]
                    ? fixture.SourceToDestination
                    : fixture.DestinationToSource;
                Assert.True(fixture.World.TryTransition(entities[selected], transition, out _));
                inSource[selected] = !inSource[selected];
            }

            if ((round & 31) == 0)
            {
                for (int i = 0; i < entityCount; i++)
                {
                    ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = inSource[i]
                        ? fixture.SourceEnableBinding
                        : fixture.DestinationEnableBinding;
                    Assert.True(fixture.World.TryGetComponent(
                        entities[i],
                        binding,
                        out UltraSim.ECS.Tests.LayoutEnableLong value));
                    Assert.Equal(expected[i], value.Value);
                    Assert.True(fixture.World.TryGetComponentEnabled(
                        entities[i],
                        binding,
                        out bool actualEnabled));
                    Assert.Equal(enabled[i], actualEnabled);
                }
            }
        }
    }

    private static void EcsWorld_ReadWriteQueryNativePinnedParity()
    {
        long native = RunReadWriteQueryParity<NativeChunkArena>();
        long pinned = RunReadWriteQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedReadWriteQueryAllocatesZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 10_000;
        using var fixture = new WorldFixture<NativeChunkArena>(maximumChunks: 4, entityCapacity: entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Source.Id,
                fixture.SourceWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 11L + 1 },
                out Entity entity));
            if ((i & 1) != 0)
                Assert.True(fixture.World.TryTransition(entity, fixture.SourceToDestination, out _));
        }

        var visitor = new WideReadEnableWriteVisitor(
            fixture.Source.Id,
            fixture.Destination.Id,
            salt: 0);
        for (int i = 0; i < 32; i++)
        {
            visitor.Salt = i;
            fixture.World.VisitReadWriteChunks(fixture.WideReadEnableWriteQuery, ref visitor);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < rounds; i++)
        {
            visitor.Salt = i * 13L;
            fixture.World.VisitReadWriteChunks(fixture.WideReadEnableWriteQuery, ref visitor);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(visitor.Checksum != 0);
    }

    private static long RunReadWriteQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 1_000;
        using var fixture = new WorldFixture<TArena>(maximumChunks: 4, entityCapacity: entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Source.Id,
                fixture.SourceWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 7L + 9 },
                out Entity entity));
            if ((i & 1) != 0)
                Assert.True(fixture.World.TryTransition(entity, fixture.SourceToDestination, out _));
        }

        var visitor = new WideReadEnableWriteVisitor(
            fixture.Source.Id,
            fixture.Destination.Id,
            salt: 0);
        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 101L;
            Assert.Equal(entityCount, fixture.World.VisitReadWriteChunks(
                fixture.WideReadEnableWriteQuery,
                ref visitor));
            checksum = unchecked(checksum + visitor.Checksum);
        }
        return unchecked(checksum + visitor.Rows + visitor.SourceRows + visitor.DestinationRows);
    }

    private static void ArchetypeQueryFilter_NormalizesAndRejectsInvalidConstraints()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

        ArchetypeQueryFilter filter = world.CreateQueryFilter(
            [intId, intId],
            [byteId, byteId],
            [tagId, tagId],
            [enableId, enableId]);
        Assert.True(filter.IsValid);
        Assert.Equal(world.Id, filter.WorldId);
        Assert.True(filter.RequiredComponents.SequenceEqual([enableId, intId, tagId]));
        Assert.True(filter.ExcludedComponents.SequenceEqual([byteId]));
        Assert.True(filter.EnabledComponents.SequenceEqual([tagId]));
        Assert.True(filter.DisabledComponents.SequenceEqual([enableId]));

        Assert.Throws<ArgumentException>(() => world.CreateQueryFilter(
            new[] { intId },
            new[] { intId },
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>()));
        Assert.Throws<ArgumentException>(() => world.CreateQueryFilter(
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>(),
            new[] { tagId },
            new[] { tagId }));
        Assert.Throws<ArgumentException>(() => world.CreateQueryFilter(
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>(),
            new[] { intId },
            Array.Empty<ComponentId>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateQueryFilter(
            new[] { new ComponentId(99) },
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>()));
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId,
                default));

        ArchetypeQueryFilter excludesRead = world.CreateQueryFilter(
            [],
            [wideId],
            [],
            []);
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId,
                excludesRead));

        using var foreign = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        Assert.Throws<ArgumentException>(() =>
            foreign.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId,
                filter));

        world.Seal();
        Assert.Throws<InvalidOperationException>(() => world.CreateQueryFilter(
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>(),
            Array.Empty<ComponentId>()));
    }

    private static void ArchetypeReadWriteQueryPlan_FiltersArchetypesAtSeal()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(16),
            maximumArchetypes: 3,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

        ArchetypeId match = world.RegisterArchetype(
            [enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [enableId, intId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [byteId, enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeQueryFilter filter = world.CreateQueryFilter(
            [intId],
            [byteId],
            [tagId],
            [enableId]);
        ArchetypeReadWriteQueryPlan<NativeChunkArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> plan =
            world.PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                wideId,
                enableId,
                filter);
        world.Seal();

        Assert.Equal(1, plan.MatchedArchetypeCount);
        Assert.Equal(match, plan.Routes[0].Storage.Id);
        Assert.True(plan.Routes[0].HasRowFilters);
        Assert.True(plan.RequiredComponents.SequenceEqual([enableId, intId, tagId, wideId]));
        Assert.True(plan.ExcludedComponents.SequenceEqual([byteId]));
        Assert.True(plan.EnabledComponents.SequenceEqual([tagId]));
        Assert.True(plan.DisabledComponents.SequenceEqual([enableId]));
    }

    private static void EcsWorld_ReadWriteQueryFiltersEnabledAndDisabledRows()
    {
        const int entityCount = 1_000;
        using var fixture = new FilteredQueryFixture<NativeChunkArena>(
            maximumChunks: 4,
            entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        int expectedCount = 0;
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Matching.Id,
                fixture.WideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 17L + 3 },
                out entities[i]));
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(fixture.World.TrySetComponentEnabled(entities[i], fixture.TagBinding, tagEnabled));
            Assert.True(fixture.World.TrySetComponentEnabled(entities[i], fixture.EnableBinding, valueEnabled));
            expected[entities[i].Index] = tagEnabled && !valueEnabled;
            if (tagEnabled && !valueEnabled)
                expectedCount++;
        }

        var visitor = new FilteredWideVisitor(expected, seen, salt: 71);
        int visited = fixture.World.VisitReadWriteChunks(fixture.Query, ref visitor);
        Assert.Equal(expectedCount, visited);
        Assert.Equal(expectedCount, visitor.Rows);
        Assert.True(visitor.Ranges > 1);
        Assert.True(visitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < entityCount; i++)
        {
            uint entityIndex = entities[i].Index;
            Assert.Equal(expected[entityIndex], seen[entityIndex]);
            Assert.True(fixture.World.TryGetComponent(
                entities[i],
                fixture.EnableBinding,
                out UltraSim.ECS.Tests.LayoutEnableLong value));
            long expectedValue = expected[entityIndex]
                ? unchecked((i * 17L + 3) * 5L + entityIndex + 71)
                : 0L;
            Assert.Equal(expectedValue, value.Value);
            Assert.True(fixture.World.TryGetComponentEnabled(
                entities[i],
                fixture.EnableBinding,
                out bool valueEnabled));
            Assert.Equal(expected[entityIndex] || i % 3 != 0, valueEnabled);
        }

        Array.Clear(seen);
        var second = new FilteredWideVisitor(expected, seen, salt: 99);
        Assert.Equal(0, fixture.World.VisitReadWriteChunks(fixture.Query, ref second));
        Assert.Equal(0, second.Rows);
    }

    private static void EcsWorld_FilteredReadWriteQueryRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 300;
        using var fixture = new FilteredQueryFixture<NativeChunkArena>(
            maximumChunks: 2,
            entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        var random = new Random(unchecked((int)0xF17E4E5D));
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Matching.Id,
                fixture.WideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 23L + 11 },
                out entities[i]));
        }

        for (int round = 0; round < rounds; round++)
        {
            Array.Clear(expected);
            Array.Clear(seen);
            int expectedCount = 0;
            for (int i = 0; i < entityCount; i++)
            {
                bool tagEnabled = random.Next(4) != 0;
                bool valueEnabled = random.Next(3) != 0;
                Assert.True(fixture.World.TrySetComponentEnabled(entities[i], fixture.TagBinding, tagEnabled));
                Assert.True(fixture.World.TrySetComponentEnabled(entities[i], fixture.EnableBinding, valueEnabled));
                bool matches = tagEnabled && !valueEnabled;
                expected[entities[i].Index] = matches;
                if (matches)
                    expectedCount++;
            }

            var visitor = new FilteredWideVisitor(expected, seen, random.NextInt64());
            Assert.Equal(expectedCount, fixture.World.VisitReadWriteChunks(fixture.Query, ref visitor));
            Assert.Equal(expectedCount, visitor.Rows);
            for (int i = 0; i < entityCount; i++)
                Assert.Equal(expected[entities[i].Index], seen[entities[i].Index]);
        }
    }

    private static void EcsWorld_FilteredReadWriteQueryNativePinnedParity()
    {
        long native = RunFilteredReadWriteQueryParity<NativeChunkArena>();
        long pinned = RunFilteredReadWriteQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedFilteredReadWriteQueryAllocatesZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 5_000;
        using var fixture = new FilteredQueryFixture<NativeChunkArena>(
            maximumChunks: 4,
            entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Matching.Id,
                fixture.WideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 7L + 1 },
                out entities[i]));
            expected[entities[i].Index] = true;
            Assert.True(fixture.World.TrySetComponentEnabled(entities[i], fixture.EnableBinding, false));
        }

        var visitor = new FilteredWideVisitor(expected, seen, salt: 0);
        for (int i = 0; i < 32; i++)
        {
            Array.Clear(seen);
            visitor.Salt = i;
            fixture.World.VisitReadWriteChunks(fixture.Query, ref visitor);
            for (int entityIndex = 0; entityIndex < entityCount; entityIndex++)
                fixture.World.TrySetComponentEnabled(entities[entityIndex], fixture.EnableBinding, false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            Array.Clear(seen);
            visitor.Salt = round * 19L;
            fixture.World.VisitReadWriteChunks(fixture.Query, ref visitor);
            for (int entityIndex = 0; entityIndex < entityCount; entityIndex++)
                fixture.World.TrySetComponentEnabled(entities[entityIndex], fixture.EnableBinding, false);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(visitor.Checksum != 0);
    }

    private static long RunFilteredReadWriteQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 200;
        using var fixture = new FilteredQueryFixture<TArena>(
            maximumChunks: 4,
            entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Matching.Id,
                fixture.WideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 13L + 5 },
                out entities[i]));
        }

        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Array.Clear(expected);
            Array.Clear(seen);
            int expectedCount = 0;
            for (int i = 0; i < entityCount; i++)
            {
                Entity entity = entities[i];
                bool tagEnabled = ((i + round) & 3) != 0;
                bool valueEnabled = ((i * 3 + round) & 7) != 0;
                Assert.True(fixture.World.TrySetComponentEnabled(entity, fixture.TagBinding, tagEnabled));
                Assert.True(fixture.World.TrySetComponentEnabled(entity, fixture.EnableBinding, valueEnabled));
                expected[entity.Index] = tagEnabled && !valueEnabled;
                if (expected[entity.Index])
                    expectedCount++;
            }

            var visitor = new FilteredWideVisitor(expected, seen, round * 101L);
            Assert.Equal(expectedCount, fixture.World.VisitReadWriteChunks(fixture.Query, ref visitor));
            checksum = unchecked(checksum + visitor.Checksum + visitor.Rows + visitor.Ranges);
        }
        return checksum;
    }

    private static void GeneratedQueryFamily_SealCompilesCompleteArchetypeSet()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(32),
            maximumArchetypes: 3,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 2);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

        ArchetypeId first = world.RegisterArchetype(
            [enableId, intId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeReadReadWriteQueryPlan<
            NativeChunkArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> plan = world
                .PrepareReadReadWriteQuery<
                    UltraSim.ECS.Tests.LayoutWide,
                    UltraSim.ECS.Tests.LayoutInt,
                    UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);

        ArchetypeId second = world.RegisterArchetype(
            [byteId, enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [byteId, enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        world.Seal();

        Assert.True(plan.IsCompiled);
        Assert.Equal(1, world.QueryPlanCount);
        Assert.Equal(2, plan.MatchedArchetypeCount);
        Assert.Equal(first, plan.Routes[0].Storage.Id);
        Assert.Equal(second, plan.Routes[1].Storage.Id);
        Assert.Equal(wideId, plan.Read1ComponentId);
        Assert.Equal(intId, plan.Read2ComponentId);
        Assert.Equal(enableId, plan.WriteComponentId);
        Assert.False(plan.Read1IsEnableable);
        Assert.False(plan.Read2IsEnableable);
        Assert.True(plan.WriteIsEnableable);
        Assert.True(plan.RequiredComponents.SequenceEqual([enableId, intId, wideId]));
        Assert.False(plan.Routes[0].HasRowFilters);
        Assert.NotEqual(plan.Routes[0].Read1ColumnIndex, plan.Routes[0].Read2ColumnIndex);
        Assert.NotEqual(plan.Routes[0].Read1ColumnIndex, plan.Routes[0].WriteColumnIndex);
        Assert.NotEqual(plan.Routes[0].Read2ColumnIndex, plan.Routes[0].WriteColumnIndex);
        world.Dispose();
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);
    }

    private static void EcsWorld_Generated2R1WVisitsEveryMatchedArchetype()
    {
        const int firstCount = 193;
        const int secondCount = 129;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(
            maximumChunks: 2,
            entityCapacity: firstCount + secondCount);
        var entities = new Entity[firstCount + secondCount];
        var wideValues = new long[entities.Length];
        var intValues = new int[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            ArchetypeStorage<NativeChunkArena> storage = i < firstCount
                ? fixture.First
                : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = i < firstCount
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = i < firstCount
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            wideValues[i] = i * 17L + 3;
            intValues[i] = i * 7 + 11;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
            Assert.True(fixture.World.TrySetComponent(
                entities[i],
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = intValues[i] }));
        }

        var visitor = new WideIntEnableGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 41);
        int visited = fixture.World.VisitReadReadWriteChunks(fixture.Query, ref visitor);
        Assert.Equal(entities.Length, visited);
        Assert.Equal(entities.Length, visitor.Rows);
        Assert.Equal(firstCount, visitor.FirstRows);
        Assert.Equal(secondCount, visitor.SecondRows);

        for (int i = 0; i < entities.Length; i++)
        {
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding = i < firstCount
                ? fixture.FirstEnableBinding
                : fixture.SecondEnableBinding;
            long expected = GeneratedQueryValue(
                wideValues[i],
                intValues[i],
                entities[i].Index,
                41);
            Assert.True(fixture.World.TryGetComponent(
                entities[i],
                enableBinding,
                out UltraSim.ECS.Tests.LayoutEnableLong value));
            Assert.Equal(expected, value.Value);
            Assert.True(fixture.World.TryGetComponentEnabled(
                entities[i],
                enableBinding,
                out bool enabled));
            Assert.Equal((expected & 1) == 0, enabled);
        }
    }

    private static void EcsWorld_Generated2R1WRejectsAliasesTagsForeignPlansAndCapacity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        _ = world.RegisterArchetype(
            [enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);

        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, wideId, enableId));
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutTag,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(tagId, intId, enableId));
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(intId, wideId, enableId));
        ArchetypeQueryFilter excludesAccess = world.CreateQueryFilter(
            [],
            [intId],
            [],
            []);
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId, excludesAccess));

        _ = world.PrepareReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));
        world.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));

        using var first = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        using var second = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        var visitor = new WideIntEnableGeneratedVisitor(
            second.First.Id,
            second.Second.Id,
            salt: 0);
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadReadWriteChunks(first.Query, ref visitor));

        using var queryless = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        Assert.Throws<InvalidOperationException>(() =>
            queryless.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));
    }

    private static void EcsWorld_Generated2R1WFiltersRows()
    {
        const int entityCount = 1_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(
            maximumChunks: 8,
            entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        int expectedCount = 0;
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Second.Id,
                fixture.SecondWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 17L + 3 },
                out entities[i]));
            Assert.True(fixture.World.TrySetComponent(
                entities[i],
                fixture.SecondIntBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 7 + 11 }));
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[i], fixture.SecondTagBinding, tagEnabled));
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[i], fixture.SecondEnableBinding, valueEnabled));
            expected[entities[i].Index] = tagEnabled && !valueEnabled;
            if (expected[entities[i].Index])
                expectedCount++;
        }

        var visitor = new FilteredWideIntEnableGeneratedVisitor(expected, seen, salt: 71);
        int visited = fixture.World.VisitReadReadWriteChunks(fixture.FilteredQuery, ref visitor);
        Assert.Equal(expectedCount, visited);
        Assert.Equal(expectedCount, visitor.Rows);
        Assert.True(visitor.Ranges > 1);
        Assert.True(visitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < entityCount; i++)
        {
            uint entityIndex = entities[i].Index;
            Assert.Equal(expected[entityIndex], seen[entityIndex]);
            Assert.True(fixture.World.TryGetComponent(
                entities[i],
                fixture.SecondEnableBinding,
                out UltraSim.ECS.Tests.LayoutEnableLong value));
            long expectedValue = expected[entityIndex]
                ? GeneratedQueryValue(i * 17L + 3, i * 7 + 11, entityIndex, 71)
                : 0L;
            Assert.Equal(expectedValue, value.Value);
        }

        Array.Clear(seen);
        var second = new FilteredWideIntEnableGeneratedVisitor(expected, seen, salt: 99);
        Assert.Equal(0, fixture.World.VisitReadReadWriteChunks(fixture.FilteredQuery, ref second));
        Assert.Equal(0, second.Rows);
    }

    private static void EcsWorld_Generated2R1WEnforcesBarrierAndReleasesAfterException()
    {
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(1, 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.First.Id, out Entity entity));
        var probe = new GeneratedQueryBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.Query,
            fixture.FirstEnableBinding,
            entity,
            fixture.First.Id,
            fixture.Second.Id);
        Assert.Equal(1, fixture.World.VisitReadReadWriteChunks(fixture.Query, ref probe));
        Assert.Equal(3, probe.Rejections);

        var throwing = new ThrowingGeneratedQueryVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadReadWriteChunks(fixture.Query, ref throwing));
        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.FirstEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 77 }));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_Generated2R1WRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(4, entityCount);
        var entities = new Entity[entityCount];
        var inFirst = new bool[entityCount];
        var wideValues = new long[entityCount];
        var intValues = new int[entityCount];
        var random = new Random(unchecked((int)0x2A1B3C4D));
        for (int i = 0; i < entityCount; i++)
        {
            inFirst[i] = (i & 1) == 0;
            wideValues[i] = i * 29L + 5;
            intValues[i] = i * 13 + 7;
            ArchetypeStorage<NativeChunkArena> storage = inFirst[i] ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = inFirst[i]
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[i]
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
            Assert.True(fixture.World.TrySetComponent(
                entities[i], intBinding, new UltraSim.ECS.Tests.LayoutInt { Value = intValues[i] }));
        }

        for (int round = 0; round < rounds; round++)
        {
            for (int mutation = 0; mutation < 5; mutation++)
            {
                int index = random.Next(entityCount);
                wideValues[index] = random.NextInt64();
                intValues[index] = random.Next();
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = inFirst[index]
                    ? fixture.FirstWideBinding
                    : fixture.SecondWideBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[index]
                    ? fixture.FirstIntBinding
                    : fixture.SecondIntBinding;
                Assert.True(fixture.World.TrySetComponent(
                    entities[index],
                    wideBinding,
                    new UltraSim.ECS.Tests.LayoutWide { A = wideValues[index] }));
                Assert.True(fixture.World.TrySetComponent(
                    entities[index],
                    intBinding,
                    new UltraSim.ECS.Tests.LayoutInt { Value = intValues[index] }));
            }

            long salt = random.NextInt64();
            var visitor = new WideIntEnableGeneratedVisitor(
                fixture.First.Id,
                fixture.Second.Id,
                salt);
            Assert.Equal(entityCount, fixture.World.VisitReadReadWriteChunks(fixture.Query, ref visitor));
            if ((round & 15) != 0)
                continue;

            for (int i = 0; i < entityCount; i++)
            {
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = inFirst[i]
                    ? fixture.FirstEnableBinding
                    : fixture.SecondEnableBinding;
                long expected = GeneratedQueryValue(
                    wideValues[i], intValues[i], entities[i].Index, salt);
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], binding, out UltraSim.ECS.Tests.LayoutEnableLong actual));
                Assert.Equal(expected, actual.Value);
                Assert.True(fixture.World.TryGetComponentEnabled(entities[i], binding, out bool enabled));
                Assert.Equal((expected & 1) == 0, enabled);
            }
        }
    }

    private static void EcsWorld_Generated2R1WNativePinnedParity()
    {
        long native = RunGeneratedQueryParity<NativeChunkArena>();
        long pinned = RunGeneratedQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedGenerated2R1WAllocatesZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 5_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<NativeChunkArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 11L + 1 },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 3 + 7 }));
        }

        var visitor = new WideIntEnableGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        for (int i = 0; i < 32; i++)
        {
            visitor.Salt = i;
            fixture.World.VisitReadReadWriteChunks(fixture.Query, ref visitor);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 19L;
            fixture.World.VisitReadReadWriteChunks(fixture.Query, ref visitor);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(visitor.Checksum != 0);
    }

    private static long RunGeneratedQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<TArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<TArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 7L + 9 },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 5 + 3 }));
        }

        var visitor = new WideIntEnableGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 101L;
            Assert.Equal(entityCount, fixture.World.VisitReadReadWriteChunks(fixture.Query, ref visitor));
            checksum = unchecked(checksum + visitor.Checksum);
        }
        return unchecked(checksum + visitor.Rows + visitor.FirstRows + visitor.SecondRows);
    }

    private static void GeneratedWriteFamily_SealCompilesCompleteArchetypeSet()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(32),
            maximumArchetypes: 3,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

        ArchetypeId first = world.RegisterArchetype(
            [enableId, intId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeReadWriteWriteQueryPlan<
            NativeChunkArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> plan = world
                .PrepareReadWriteWriteQuery<
                    UltraSim.ECS.Tests.LayoutWide,
                    UltraSim.ECS.Tests.LayoutInt,
                    UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);

        ArchetypeId second = world.RegisterArchetype(
            [byteId, enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [byteId, enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        world.Seal();

        Assert.True(plan.IsCompiled);
        Assert.Equal(2, plan.MatchedArchetypeCount);
        Assert.Equal(first, plan.Routes[0].Storage.Id);
        Assert.Equal(second, plan.Routes[1].Storage.Id);
        Assert.Equal(wideId, plan.ReadComponentId);
        Assert.Equal(intId, plan.Write1ComponentId);
        Assert.Equal(enableId, plan.Write2ComponentId);
        Assert.False(plan.ReadIsEnableable);
        Assert.False(plan.Write1IsEnableable);
        Assert.True(plan.Write2IsEnableable);
        Assert.True(plan.RequiredComponents.SequenceEqual([enableId, intId, wideId]));
        Assert.NotEqual(plan.Routes[0].ReadColumnIndex, plan.Routes[0].Write1ColumnIndex);
        Assert.NotEqual(plan.Routes[0].ReadColumnIndex, plan.Routes[0].Write2ColumnIndex);
        Assert.NotEqual(plan.Routes[0].Write1ColumnIndex, plan.Routes[0].Write2ColumnIndex);
        world.Dispose();
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);
    }

    private static void EcsWorld_Generated1R2WWritesEveryMatchedArchetype()
    {
        const int firstCount = 193;
        const int secondCount = 129;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(
            maximumChunks: 2,
            entityCapacity: firstCount + secondCount);
        var entities = new Entity[firstCount + secondCount];
        var wideValues = new long[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            bool first = i < firstCount;
            ArchetypeStorage<NativeChunkArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> binding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            wideValues[i] = i * 17L + 3;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                binding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
        }

        var visitor = new WideIntEnableGeneratedWriteVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 41);
        int visited = fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref visitor);
        Assert.Equal(entities.Length, visited);
        Assert.Equal(entities.Length, visitor.Rows);
        Assert.Equal(firstCount, visitor.FirstRows);
        Assert.Equal(secondCount, visitor.SecondRows);
        Assert.Equal(1, visitor.NonEnableableWriteRejections);
        Assert.Equal(2, visitor.RowBoundsRejections);

        for (int i = 0; i < entities.Length; i++)
        {
            bool first = i < firstCount;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding = first
                ? fixture.FirstEnableBinding
                : fixture.SecondEnableBinding;
            GeneratedWriteQueryValues(
                wideValues[i], entities[i].Index, 41, out int expectedInt, out long expectedLong);
            Assert.True(fixture.World.TryGetComponent(
                entities[i], intBinding, out UltraSim.ECS.Tests.LayoutInt actualInt));
            Assert.Equal(expectedInt, actualInt.Value);
            Assert.True(fixture.World.TryGetComponent(
                entities[i], enableBinding, out UltraSim.ECS.Tests.LayoutEnableLong actualLong));
            Assert.Equal(expectedLong, actualLong.Value);
            Assert.True(fixture.World.TryGetComponentEnabled(
                entities[i], enableBinding, out bool enabled));
            Assert.Equal((expectedLong & 1) == 0, enabled);
        }
    }

    private static void EcsWorld_Generated1R2WRejectsAliasesTagsForeignPlansAndCapacity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        _ = world.RegisterArchetype(
            [enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);

        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutInt>(wideId, intId, intId));
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutTag,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(tagId, intId, enableId));
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(intId, wideId, enableId));
        ArchetypeQueryFilter excludesWrite = world.CreateQueryFilter([], [intId], [], []);
        Assert.Throws<ArgumentException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId, excludesWrite));

        _ = world.PrepareReadWriteWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));
        world.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            world.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));

        using var first = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        using var second = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        var visitor = new WideIntEnableGeneratedWriteVisitor(
            second.First.Id,
            second.Second.Id,
            salt: 0);
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadWriteWriteChunks(first.WriteQuery, ref visitor));

        using var queryless = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        Assert.Throws<InvalidOperationException>(() =>
            queryless.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId));
    }

    private static void EcsWorld_Generated1R2WFiltersRows()
    {
        const int entityCount = 1_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(8, entityCount);
        var entities = new Entity[entityCount];
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        int expectedCount = 0;
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Second.Id,
                fixture.SecondWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 17L + 3 },
                out entities[i]));
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[i], fixture.SecondTagBinding, tagEnabled));
            Assert.True(fixture.World.TrySetComponentEnabled(
                entities[i], fixture.SecondEnableBinding, valueEnabled));
            expected[entities[i].Index] = tagEnabled && !valueEnabled;
            if (expected[entities[i].Index])
                expectedCount++;
        }

        var visitor = new FilteredWideIntEnableGeneratedWriteVisitor(expected, seen, salt: 71);
        int visited = fixture.World.VisitReadWriteWriteChunks(fixture.FilteredWriteQuery, ref visitor);
        Assert.Equal(expectedCount, visited);
        Assert.Equal(expectedCount, visitor.Rows);
        Assert.True(visitor.Ranges > 1);
        Assert.True(visitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < entityCount; i++)
        {
            uint entityIndex = entities[i].Index;
            Assert.Equal(expected[entityIndex], seen[entityIndex]);
            Assert.True(fixture.World.TryGetComponent(
                entities[i], fixture.SecondIntBinding, out UltraSim.ECS.Tests.LayoutInt actualInt));
            Assert.True(fixture.World.TryGetComponent(
                entities[i], fixture.SecondEnableBinding, out UltraSim.ECS.Tests.LayoutEnableLong actualLong));
            if (expected[entityIndex])
            {
                GeneratedWriteQueryValues(
                    i * 17L + 3, entityIndex, 71, out int expectedInt, out long expectedLong);
                Assert.Equal(expectedInt, actualInt.Value);
                Assert.Equal(expectedLong, actualLong.Value);
            }
            else
            {
                Assert.Equal(0, actualInt.Value);
                Assert.Equal(0L, actualLong.Value);
            }
        }

        Array.Clear(seen);
        var second = new FilteredWideIntEnableGeneratedWriteVisitor(expected, seen, salt: 99);
        Assert.Equal(0, fixture.World.VisitReadWriteWriteChunks(fixture.FilteredWriteQuery, ref second));
        Assert.Equal(0, second.Rows);
    }

    private static void EcsWorld_Generated1R2WEnforcesBarrierAndReleasesAfterException()
    {
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(1, 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.First.Id, out Entity entity));
        var probe = new GeneratedWriteQueryBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.WriteQuery,
            fixture.FirstEnableBinding,
            entity,
            fixture.First.Id,
            fixture.Second.Id);
        Assert.Equal(1, fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref probe));
        Assert.Equal(3, probe.Rejections);

        var throwing = new ThrowingGeneratedWriteQueryVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref throwing));
        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.FirstEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 77 }));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_Generated1R2WRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(4, entityCount);
        var entities = new Entity[entityCount];
        var inFirst = new bool[entityCount];
        var wideValues = new long[entityCount];
        var random = new Random(unchecked((int)0x7E21A54B));
        for (int i = 0; i < entityCount; i++)
        {
            inFirst[i] = (i & 1) == 0;
            wideValues[i] = i * 29L + 5;
            ArchetypeStorage<NativeChunkArena> storage = inFirst[i] ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> binding = inFirst[i]
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                binding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
        }

        for (int round = 0; round < rounds; round++)
        {
            for (int mutation = 0; mutation < 5; mutation++)
            {
                int index = random.Next(entityCount);
                wideValues[index] = random.NextInt64();
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> binding = inFirst[index]
                    ? fixture.FirstWideBinding
                    : fixture.SecondWideBinding;
                Assert.True(fixture.World.TrySetComponent(
                    entities[index],
                    binding,
                    new UltraSim.ECS.Tests.LayoutWide { A = wideValues[index] }));
            }

            long salt = random.NextInt64();
            var visitor = new WideIntEnableGeneratedWriteVisitor(
                fixture.First.Id,
                fixture.Second.Id,
                salt);
            Assert.Equal(entityCount, fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref visitor));
            if ((round & 15) != 0)
                continue;

            for (int i = 0; i < entityCount; i++)
            {
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[i]
                    ? fixture.FirstIntBinding
                    : fixture.SecondIntBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding = inFirst[i]
                    ? fixture.FirstEnableBinding
                    : fixture.SecondEnableBinding;
                GeneratedWriteQueryValues(
                    wideValues[i], entities[i].Index, salt, out int expectedInt, out long expectedLong);
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], intBinding, out UltraSim.ECS.Tests.LayoutInt actualInt));
                Assert.Equal(expectedInt, actualInt.Value);
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], enableBinding, out UltraSim.ECS.Tests.LayoutEnableLong actualLong));
                Assert.Equal(expectedLong, actualLong.Value);
                Assert.True(fixture.World.TryGetComponentEnabled(
                    entities[i], enableBinding, out bool enabled));
                Assert.Equal((expectedLong & 1) == 0, enabled);
            }
        }
    }

    private static void EcsWorld_Generated1R2WNativePinnedParity()
    {
        long native = RunGeneratedWriteQueryParity<NativeChunkArena>();
        long pinned = RunGeneratedWriteQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedGenerated1R2WAllocatesZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 5_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<NativeChunkArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> binding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                binding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 11L + 1 },
                out _));
        }

        var visitor = new WideIntEnableGeneratedWriteVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        for (int i = 0; i < 32; i++)
        {
            visitor.Salt = i;
            fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref visitor);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 19L;
            fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref visitor);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(visitor.Checksum != 0);
    }

    private static long RunGeneratedWriteQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<TArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<TArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> binding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                binding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 7L + 9 },
                out _));
        }

        var visitor = new WideIntEnableGeneratedWriteVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 101L;
            Assert.Equal(entityCount, fixture.World.VisitReadWriteWriteChunks(fixture.WriteQuery, ref visitor));
            checksum = unchecked(checksum + visitor.Checksum);
        }
        return unchecked(checksum + visitor.Rows + visitor.FirstRows + visitor.SecondRows);
    }

    private static void GeneratedReadOnlyFamily_SealCompilesCompleteArchetypeSet()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(32),
            maximumArchetypes: 3,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

        ArchetypeId first = world.RegisterArchetype(
            [enableId, intId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        ArchetypeReadReadQueryPlan<
            NativeChunkArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> plan = world.PrepareReadReadQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt>(wideId, intId);
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);

        ArchetypeId second = world.RegisterArchetype(
            [byteId, enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        _ = world.RegisterArchetype(
            [byteId, enableId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        world.Seal();

        Assert.True(plan.IsCompiled);
        Assert.Equal(1, world.QueryPlanCount);
        Assert.Equal(2, plan.MatchedArchetypeCount);
        Assert.Equal(first, plan.Routes[0].Storage.Id);
        Assert.Equal(second, plan.Routes[1].Storage.Id);
        Assert.Equal(wideId, plan.Read1ComponentId);
        Assert.Equal(intId, plan.Read2ComponentId);
        Assert.False(plan.Read1IsEnableable);
        Assert.False(plan.Read2IsEnableable);
        Assert.True(plan.RequiredComponents.SequenceEqual([intId, wideId]));
        Assert.False(plan.Routes[0].HasRowFilters);
        Assert.NotEqual(plan.Routes[0].Read1ColumnIndex, plan.Routes[0].Read2ColumnIndex);
        world.Dispose();
        Assert.False(plan.IsCompiled);
        Assert.Equal(0, plan.MatchedArchetypeCount);
    }

    private static void EcsWorld_Generated2R0WReadsEveryMatchedArchetype()
    {
        const int firstCount = 193;
        const int secondCount = 129;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(
            maximumChunks: 2,
            entityCapacity: firstCount + secondCount);
        long expectedChecksum = 0;
        for (int i = 0; i < firstCount + secondCount; i++)
        {
            bool first = i < firstCount;
            ArchetypeStorage<NativeChunkArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            long wide = i * 17L + 3;
            int integer = i * 7 + 11;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = wide },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = integer }));
            expectedChecksum = unchecked(expectedChecksum +
                GeneratedQueryValue(wide, integer, entity.Index, 41));
        }

        var visitor = new WideIntGeneratedReadVisitor(fixture.First.Id, fixture.Second.Id, salt: 41);
        Assert.Equal(firstCount + secondCount,
            fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref visitor));
        Assert.Equal(firstCount + secondCount, visitor.Rows);
        Assert.Equal(firstCount, visitor.FirstRows);
        Assert.Equal(secondCount, visitor.SecondRows);
        Assert.Equal(2, visitor.RowBoundsRejections);
        Assert.Equal(expectedChecksum, visitor.Checksum);
    }

    private static void EcsWorld_Generated2R0WRejectsAliasesTagsForeignPlansAndCapacity()
    {
        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        _ = world.RegisterArchetype(
            [enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);

        Assert.Throws<ArgumentException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutWide>(wideId, wideId));
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutTag,
            UltraSim.ECS.Tests.LayoutInt>(tagId, intId));
        Assert.Throws<ArgumentException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(intId, wideId));
        ArchetypeQueryFilter excludesRead = world.CreateQueryFilter([], [intId], [], []);
        Assert.Throws<ArgumentException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(wideId, intId, excludesRead));

        _ = world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(wideId, intId);
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(wideId, intId));
        world.Seal();
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(wideId, intId));

        using var first = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        using var second = new GeneratedQueryFixture<NativeChunkArena>(1, 2);
        var visitor = new WideIntGeneratedReadVisitor(second.First.Id, second.Second.Id, salt: 0);
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadReadChunks(first.ReadQuery, ref visitor));

        using var queryless = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(1),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 0);
        Assert.Throws<InvalidOperationException>(() => queryless.PrepareReadReadQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>(wideId, intId));
    }

    private static void EcsWorld_Generated2R0WFiltersRows()
    {
        const int entityCount = 1_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(8, entityCount);
        var expected = new bool[entityCount];
        var seen = new bool[entityCount];
        int expectedCount = 0;
        for (int i = 0; i < entityCount; i++)
        {
            Assert.True(fixture.World.TryCreateInitialized(
                fixture.Second.Id,
                fixture.SecondWideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 17L + 3 },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                fixture.SecondIntBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 7 + 11 }));
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(fixture.World.TrySetComponentEnabled(entity, fixture.SecondTagBinding, tagEnabled));
            Assert.True(fixture.World.TrySetComponentEnabled(entity, fixture.SecondEnableBinding, valueEnabled));
            expected[entity.Index] = tagEnabled && !valueEnabled;
            if (expected[entity.Index])
                expectedCount++;
        }

        var visitor = new FilteredWideIntGeneratedReadVisitor(expected, seen, salt: 71);
        Assert.Equal(expectedCount,
            fixture.World.VisitReadReadChunks(fixture.FilteredReadQuery, ref visitor));
        Assert.Equal(expectedCount, visitor.Rows);
        Assert.True(visitor.Ranges > 1);
        Assert.True(visitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < entityCount; i++)
            Assert.Equal(expected[i], seen[i]);

        Array.Clear(seen);
        var second = new FilteredWideIntGeneratedReadVisitor(expected, seen, salt: 99);
        Assert.Equal(expectedCount,
            fixture.World.VisitReadReadChunks(fixture.FilteredReadQuery, ref second));
        Assert.Equal(expectedCount, second.Rows);
    }

    private static void EcsWorld_Generated2R0WEnforcesBarrierAndReleasesAfterException()
    {
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(1, 4);
        Assert.True(fixture.World.TryCreateDefault(fixture.First.Id, out Entity entity));
        var probe = new GeneratedReadQueryBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.ReadQuery,
            fixture.FirstEnableBinding,
            entity,
            fixture.First.Id,
            fixture.Second.Id);
        Assert.Equal(1, fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref probe));
        Assert.Equal(3, probe.Rejections);

        var throwing = new ThrowingGeneratedReadQueryVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref throwing));
        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.FirstEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 77 }));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_Generated2R0WRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(4, entityCount);
        var entities = new Entity[entityCount];
        var inFirst = new bool[entityCount];
        var wideValues = new long[entityCount];
        var intValues = new int[entityCount];
        var random = new Random(unchecked((int)0x51A30C2D));
        for (int i = 0; i < entityCount; i++)
        {
            inFirst[i] = (i & 1) == 0;
            wideValues[i] = i * 29L + 5;
            intValues[i] = i * 13 + 7;
            ArchetypeStorage<NativeChunkArena> storage = inFirst[i] ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = inFirst[i]
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[i]
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = wideValues[i] },
                out entities[i]));
            Assert.True(fixture.World.TrySetComponent(
                entities[i], intBinding, new UltraSim.ECS.Tests.LayoutInt { Value = intValues[i] }));
        }

        for (int round = 0; round < rounds; round++)
        {
            for (int mutation = 0; mutation < 5; mutation++)
            {
                int index = random.Next(entityCount);
                wideValues[index] = random.NextInt64();
                intValues[index] = random.Next();
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = inFirst[index]
                    ? fixture.FirstWideBinding
                    : fixture.SecondWideBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[index]
                    ? fixture.FirstIntBinding
                    : fixture.SecondIntBinding;
                Assert.True(fixture.World.TrySetComponent(
                    entities[index], wideBinding, new UltraSim.ECS.Tests.LayoutWide { A = wideValues[index] }));
                Assert.True(fixture.World.TrySetComponent(
                    entities[index], intBinding, new UltraSim.ECS.Tests.LayoutInt { Value = intValues[index] }));
            }

            var visitor = new ModelWideIntGeneratedReadVisitor(wideValues, intValues, random.NextInt64());
            Assert.Equal(entityCount,
                fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref visitor));
            Assert.Equal(entityCount, visitor.Rows);
        }
    }

    private static void EcsWorld_Generated2R0WNativePinnedParity()
    {
        long native = RunGeneratedReadQueryParity<NativeChunkArena>();
        long pinned = RunGeneratedReadQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedGenerated2R0WAllocatesZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 5_000;
        using var fixture = new GeneratedQueryFixture<NativeChunkArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<NativeChunkArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 11L + 1 },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 3 + 7 }));
        }

        var visitor = new WideIntGeneratedReadVisitor(fixture.First.Id, fixture.Second.Id, salt: 0);
        for (int i = 0; i < 32; i++)
        {
            visitor.Salt = i;
            fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref visitor);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 19L;
            fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref visitor);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(visitor.Checksum != 0);
    }

    private static long RunGeneratedReadQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 300;
        using var fixture = new GeneratedQueryFixture<TArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            bool first = (i & 1) == 0;
            ArchetypeStorage<TArena> storage = first ? fixture.First : fixture.Second;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
                ? fixture.FirstWideBinding
                : fixture.SecondWideBinding;
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                ? fixture.FirstIntBinding
                : fixture.SecondIntBinding;
            Assert.True(fixture.World.TryCreateInitialized(
                storage.Id,
                wideBinding,
                new UltraSim.ECS.Tests.LayoutWide { A = i * 7L + 9 },
                out Entity entity));
            Assert.True(fixture.World.TrySetComponent(
                entity,
                intBinding,
                new UltraSim.ECS.Tests.LayoutInt { Value = i * 5 + 3 }));
        }

        var visitor = new WideIntGeneratedReadVisitor(fixture.First.Id, fixture.Second.Id, salt: 0);
        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            visitor.Salt = round * 101L;
            Assert.Equal(entityCount,
                fixture.World.VisitReadReadChunks(fixture.ReadQuery, ref visitor));
            checksum = unchecked(checksum + visitor.Checksum);
        }
        return unchecked(checksum + visitor.Rows + visitor.FirstRows + visitor.SecondRows);
    }

    private static void GeneratedWideFamilies_SealCompileAndRejectMisuse()
    {
        using (var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(1, 8))
        {
            Assert.True(fixture.ThreeReadQuery.IsCompiled);
            Assert.True(fixture.BalancedQuery.IsCompiled);
            Assert.Equal(2, fixture.ThreeReadQuery.MatchedArchetypeCount);
            Assert.Equal(2, fixture.BalancedQuery.MatchedArchetypeCount);
            Assert.Equal(1, fixture.FilteredThreeReadQuery.MatchedArchetypeCount);
            Assert.Equal(1, fixture.FilteredBalancedQuery.MatchedArchetypeCount);
            Assert.Equal(fixture.First.Id, fixture.ThreeReadQuery.Routes[0].Storage.Id);
            Assert.Equal(fixture.Second.Id, fixture.ThreeReadQuery.Routes[1].Storage.Id);
            Assert.NotEqual(
                fixture.ThreeReadQuery.Routes[0].Read1ColumnIndex,
                fixture.ThreeReadQuery.Routes[0].Read2ColumnIndex);
            Assert.NotEqual(
                fixture.ThreeReadQuery.Routes[0].Read2ColumnIndex,
                fixture.ThreeReadQuery.Routes[0].Read3ColumnIndex);
            Assert.NotEqual(
                fixture.BalancedQuery.Routes[0].Write1ColumnIndex,
                fixture.BalancedQuery.Routes[0].Write2ColumnIndex);
        }

        ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
        ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
        ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
        ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
        ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
            .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
        _ = world.RegisterArchetype(
            [byteId, enableId, intId, tagId, wideId],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);

        Assert.Throws<ArgumentException>(() => world.PrepareReadReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, wideId, enableId));
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutTag,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, tagId, enableId));
        Assert.Throws<ArgumentException>(() => world.PrepareReadReadWriteWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutInt>(wideId, byteId, intId, intId));
        Assert.Throws<ArgumentException>(() => world.PrepareReadReadWriteWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>(byteId, wideId, intId, enableId));
        ArchetypeQueryFilter excludesAccess = world.CreateQueryFilter([], [byteId], [], []);
        Assert.Throws<ArgumentException>(() => world.PrepareReadReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, byteId, enableId, excludesAccess));

        _ = world.PrepareReadReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, byteId, enableId);
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadWriteWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, byteId, intId, enableId));
        world.Seal();
        Assert.Throws<InvalidOperationException>(() => world.PrepareReadReadReadWriteQuery<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, byteId, enableId));

        using var first = new GeneratedWideQueryFixture<NativeChunkArena>(1, 2);
        using var second = new GeneratedWideQueryFixture<NativeChunkArena>(1, 2);
        var visitor = new ThreeReadWideGeneratedVisitor(second.First.Id, second.Second.Id, 0);
        Assert.Throws<ArgumentException>(() =>
            second.World.VisitReadReadReadWriteChunks(first.ThreeReadQuery, ref visitor));
    }

    private static void EcsWorld_Generated3R1WVisitsAndFiltersRows()
    {
        const int firstCount = 193;
        const int secondCount = 129;
        using (var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(2, firstCount + secondCount))
        {
            var entities = new Entity[firstCount + secondCount];
            for (int i = 0; i < entities.Length; i++)
                entities[i] = CreateGeneratedWideEntity(fixture, i < firstCount, i);

            var visitor = new ThreeReadWideGeneratedVisitor(
                fixture.First.Id,
                fixture.Second.Id,
                salt: 41);
            Assert.Equal(entities.Length,
                fixture.World.VisitReadReadReadWriteChunks(fixture.ThreeReadQuery, ref visitor));
            Assert.Equal(firstCount, visitor.FirstRows);
            Assert.Equal(secondCount, visitor.SecondRows);
            Assert.Equal(2, visitor.RowBoundsRejections);
            for (int i = 0; i < entities.Length; i++)
            {
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = i < firstCount
                    ? fixture.FirstEnableBinding
                    : fixture.SecondEnableBinding;
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], binding, out UltraSim.ECS.Tests.LayoutEnableLong value));
                Assert.Equal(GeneratedThreeReadWideValue(i, entities[i].Index, 41), value.Value);
            }
        }

        const int filteredCount = 1_000;
        using var filtered = new GeneratedWideQueryFixture<NativeChunkArena>(8, filteredCount);
        var expected = new bool[filteredCount];
        var seen = new bool[filteredCount];
        int matches = 0;
        for (int i = 0; i < filteredCount; i++)
        {
            Entity entity = CreateGeneratedWideEntity(filtered, first: false, i);
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(filtered.World.TrySetComponentEnabled(entity, filtered.SecondTagBinding, tagEnabled));
            Assert.True(filtered.World.TrySetComponentEnabled(entity, filtered.SecondEnableBinding, valueEnabled));
            expected[entity.Index] = tagEnabled && !valueEnabled;
            if (expected[entity.Index])
                matches++;
        }
        var filteredVisitor = new ThreeReadWideGeneratedVisitor(
            filtered.First.Id,
            filtered.Second.Id,
            salt: 71,
            expected,
            seen);
        Assert.Equal(matches, filtered.World.VisitReadReadReadWriteChunks(
            filtered.FilteredThreeReadQuery,
            ref filteredVisitor));
        Assert.True(filteredVisitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < filteredCount; i++)
            Assert.Equal(expected[i], seen[i]);
    }

    private static void EcsWorld_Generated2R2WVisitsAndFiltersRows()
    {
        const int firstCount = 193;
        const int secondCount = 129;
        using (var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(2, firstCount + secondCount))
        {
            var entities = new Entity[firstCount + secondCount];
            for (int i = 0; i < entities.Length; i++)
                entities[i] = CreateGeneratedWideEntity(fixture, i < firstCount, i);

            var visitor = new BalancedWideGeneratedVisitor(
                fixture.First.Id,
                fixture.Second.Id,
                salt: 41);
            Assert.Equal(entities.Length,
                fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref visitor));
            Assert.Equal(firstCount, visitor.FirstRows);
            Assert.Equal(secondCount, visitor.SecondRows);
            Assert.Equal(1, visitor.NonEnableableWriteRejections);
            Assert.Equal(2, visitor.RowBoundsRejections);
            for (int i = 0; i < entities.Length; i++)
            {
                bool first = i < firstCount;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
                    ? fixture.FirstIntBinding
                    : fixture.SecondIntBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding = first
                    ? fixture.FirstEnableBinding
                    : fixture.SecondEnableBinding;
                GeneratedBalancedWideValues(i, entities[i].Index, 41, out int integer, out long value);
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], intBinding, out UltraSim.ECS.Tests.LayoutInt actualInt));
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], enableBinding, out UltraSim.ECS.Tests.LayoutEnableLong actualLong));
                Assert.Equal(integer, actualInt.Value);
                Assert.Equal(value, actualLong.Value);
            }
        }

        const int filteredCount = 1_000;
        using var filtered = new GeneratedWideQueryFixture<NativeChunkArena>(8, filteredCount);
        var expected = new bool[filteredCount];
        var seen = new bool[filteredCount];
        int matches = 0;
        for (int i = 0; i < filteredCount; i++)
        {
            Entity entity = CreateGeneratedWideEntity(filtered, first: false, i);
            bool tagEnabled = i % 5 != 0;
            bool valueEnabled = i % 3 != 0;
            Assert.True(filtered.World.TrySetComponentEnabled(entity, filtered.SecondTagBinding, tagEnabled));
            Assert.True(filtered.World.TrySetComponentEnabled(entity, filtered.SecondEnableBinding, valueEnabled));
            expected[entity.Index] = tagEnabled && !valueEnabled;
            if (expected[entity.Index])
                matches++;
        }
        var filteredVisitor = new BalancedWideGeneratedVisitor(
            filtered.First.Id,
            filtered.Second.Id,
            salt: 71,
            expected,
            seen);
        Assert.Equal(matches, filtered.World.VisitReadReadWriteWriteChunks(
            filtered.FilteredBalancedQuery,
            ref filteredVisitor));
        Assert.True(filteredVisitor.NonZeroRowOffsets > 0);
        for (int i = 0; i < filteredCount; i++)
            Assert.Equal(expected[i], seen[i]);
    }

    private static void EcsWorld_GeneratedWideFamiliesEnforceBarrierAndReleaseAfterException()
    {
        using var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(1, 4);
        Entity entity = CreateGeneratedWideEntity(fixture, first: true, seed: 1);
        var probe = new GeneratedWideBarrierProbeVisitor<NativeChunkArena>(
            fixture.World,
            fixture.BalancedQuery,
            fixture.FirstEnableBinding,
            entity,
            fixture.First.Id,
            fixture.Second.Id);
        Assert.Equal(1,
            fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref probe));
        Assert.Equal(3, probe.Rejections);

        var throwing = new ThrowingGeneratedWideVisitor();
        Assert.Throws<ChunkVisitorSentinelException>(() =>
            fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref throwing));
        Assert.True(fixture.World.TrySetComponent(
            entity,
            fixture.FirstEnableBinding,
            new UltraSim.ECS.Tests.LayoutEnableLong { Value = 77 }));
        Assert.True(fixture.World.TryDestroy(entity));
    }

    private static void EcsWorld_GeneratedWideFamiliesRandomizedModelRemainsConsistent()
    {
        const int entityCount = 257;
        const int rounds = 300;
        using var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(4, entityCount);
        var entities = new Entity[entityCount];
        var inFirst = new bool[entityCount];
        var seeds = new int[entityCount];
        var random = new Random(unchecked((int)0x62B41D39));
        for (int i = 0; i < entityCount; i++)
        {
            inFirst[i] = (i & 1) == 0;
            seeds[i] = i;
            entities[i] = CreateGeneratedWideEntity(fixture, inFirst[i], seeds[i]);
        }

        for (int round = 0; round < rounds; round++)
        {
            for (int mutation = 0; mutation < 5; mutation++)
            {
                int index = random.Next(entityCount);
                seeds[index] = random.Next();
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = inFirst[index]
                    ? fixture.FirstWideBinding
                    : fixture.SecondWideBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> byteBinding = inFirst[index]
                    ? fixture.FirstByteBinding
                    : fixture.SecondByteBinding;
                Assert.True(fixture.World.TrySetComponent(
                    entities[index], wideBinding, CreateGeneratedWideValue(seeds[index])));
                Assert.True(fixture.World.TrySetComponent(
                    entities[index], byteBinding, CreateGeneratedByteValue(seeds[index])));
            }

            long salt = random.NextInt64();
            var visitor = new BalancedWideGeneratedVisitor(
                fixture.First.Id,
                fixture.Second.Id,
                salt);
            Assert.Equal(entityCount,
                fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref visitor));
            if ((round & 15) != 0)
                continue;
            for (int i = 0; i < entityCount; i++)
            {
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = inFirst[i]
                    ? fixture.FirstIntBinding
                    : fixture.SecondIntBinding;
                ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> enableBinding = inFirst[i]
                    ? fixture.FirstEnableBinding
                    : fixture.SecondEnableBinding;
                GeneratedBalancedWideValues(
                    seeds[i], entities[i].Index, salt, out int expectedInt, out long expectedLong);
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], intBinding, out UltraSim.ECS.Tests.LayoutInt actualInt));
                Assert.True(fixture.World.TryGetComponent(
                    entities[i], enableBinding, out UltraSim.ECS.Tests.LayoutEnableLong actualLong));
                Assert.Equal(expectedInt, actualInt.Value);
                Assert.Equal(expectedLong, actualLong.Value);
            }
        }
    }

    private static void EcsWorld_GeneratedWideFamiliesNativePinnedParity()
    {
        long native = RunGeneratedWideQueryParity<NativeChunkArena>();
        long pinned = RunGeneratedWideQueryParity<PinnedChunkArena>();
        Assert.Equal(native, pinned);
    }

    private static void EcsWorld_WarmedGeneratedWideFamiliesAllocateZeroManagedBytes()
    {
        const int entityCount = 1_000;
        const int rounds = 5_000;
        using var fixture = new GeneratedWideQueryFixture<NativeChunkArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
            _ = CreateGeneratedWideEntity(fixture, (i & 1) == 0, i);
        var threeRead = new ThreeReadWideGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        var balanced = new BalancedWideGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        for (int i = 0; i < 32; i++)
        {
            threeRead.Salt = i;
            balanced.Salt = i;
            fixture.World.VisitReadReadReadWriteChunks(fixture.ThreeReadQuery, ref threeRead);
            fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref balanced);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            threeRead.Salt = round * 19L;
            balanced.Salt = round * 23L;
            fixture.World.VisitReadReadReadWriteChunks(fixture.ThreeReadQuery, ref threeRead);
            fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref balanced);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(threeRead.Checksum != 0);
        Assert.True(balanced.Checksum != 0);
    }

    private static long RunGeneratedWideQueryParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 1_000;
        const int rounds = 300;
        using var fixture = new GeneratedWideQueryFixture<TArena>(8, entityCount);
        for (int i = 0; i < entityCount; i++)
            _ = CreateGeneratedWideEntity(fixture, (i & 1) == 0, i);
        var threeRead = new ThreeReadWideGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        var balanced = new BalancedWideGeneratedVisitor(
            fixture.First.Id,
            fixture.Second.Id,
            salt: 0);
        long checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            threeRead.Salt = round * 101L;
            balanced.Salt = round * 103L;
            Assert.Equal(entityCount,
                fixture.World.VisitReadReadReadWriteChunks(fixture.ThreeReadQuery, ref threeRead));
            Assert.Equal(entityCount,
                fixture.World.VisitReadReadWriteWriteChunks(fixture.BalancedQuery, ref balanced));
            checksum = unchecked(checksum + threeRead.Checksum + balanced.Checksum);
        }
        return unchecked(checksum + threeRead.Rows + balanced.Rows);
    }

    private static Entity CreateGeneratedWideEntity<TArena>(
        GeneratedWideQueryFixture<TArena> fixture,
        bool first,
        int seed)
        where TArena : class, IChunkArenaBackend
    {
        ArchetypeStorage<TArena> storage = first ? fixture.First : fixture.Second;
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> wideBinding = first
            ? fixture.FirstWideBinding
            : fixture.SecondWideBinding;
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> intBinding = first
            ? fixture.FirstIntBinding
            : fixture.SecondIntBinding;
        ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> byteBinding = first
            ? fixture.FirstByteBinding
            : fixture.SecondByteBinding;
        Assert.True(fixture.World.TryCreateInitialized(
            storage.Id,
            wideBinding,
            CreateGeneratedWideValue(seed),
            out Entity entity));
        Assert.True(fixture.World.TrySetComponent(entity, intBinding, CreateGeneratedIntValue(seed)));
        Assert.True(fixture.World.TrySetComponent(entity, byteBinding, CreateGeneratedByteValue(seed)));
        return entity;
    }

    private static UltraSim.ECS.Tests.LayoutWide CreateGeneratedWideValue(int seed) => new()
    {
        A = unchecked(seed * 17L + 3),
        B = unchecked(seed * 19L + 5),
        C = unchecked(seed * 23L + 7),
        D = unchecked(seed * 29L + 11),
    };

    private static UltraSim.ECS.Tests.LayoutInt CreateGeneratedIntValue(int seed) => new()
    {
        Value = unchecked(seed * 7 + 13),
    };

    private static UltraSim.ECS.Tests.LayoutByte CreateGeneratedByteValue(int seed) => new()
    {
        Value = unchecked((byte)(seed * 3 + 17)),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GeneratedThreeReadWideValue(int seed, uint entityIndex, long salt)
    {
        UltraSim.ECS.Tests.LayoutWide wide = CreateGeneratedWideValue(seed);
        return unchecked(
            wide.A * 3L + wide.B * 5L + wide.C * 7L + wide.D * 11L +
            CreateGeneratedIntValue(seed).Value * 13L +
            CreateGeneratedByteValue(seed).Value * 17L + entityIndex + salt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratedBalancedWideValues(
        int seed,
        uint entityIndex,
        long salt,
        out int integer,
        out long value)
    {
        UltraSim.ECS.Tests.LayoutWide wide = CreateGeneratedWideValue(seed);
        byte narrow = CreateGeneratedByteValue(seed).Value;
        integer = unchecked((int)(wide.A + wide.C + narrow + entityIndex + salt));
        value = unchecked(wide.B * 5L + wide.D * 11L + integer * 13L + narrow + salt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratedWriteQueryValues(
        long wide,
        uint entityIndex,
        long salt,
        out int integer,
        out long value)
    {
        integer = unchecked((int)(wide + entityIndex + salt));
        value = unchecked(wide * 5L + integer * 7L + entityIndex + salt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GeneratedQueryValue(long wide, int integer, uint entityIndex, long salt) =>
        unchecked(wide * 3L + integer * 5L + entityIndex + salt);

    private struct ThreeReadWideGeneratedVisitor :
        IComponentChunkReadReadReadWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;
        private readonly bool[]? _expected;
        private readonly bool[]? _seen;

        internal ThreeReadWideGeneratedVisitor(
            ArchetypeId firstId,
            ArchetypeId secondId,
            long salt,
            bool[]? expected = null,
            bool[]? seen = null)
        {
            _firstId = firstId;
            _secondId = secondId;
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            FirstRows = 0;
            SecondRows = 0;
            NonZeroRowOffsets = 0;
            RowBoundsRejections = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int FirstRows;
        internal int SecondRows;
        internal int NonZeroRowOffsets;
        internal int RowBoundsRejections;
        internal long Checksum;

        public void Execute(in ReadReadReadWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.False(chunk.IsRead1Enableable);
            Assert.False(chunk.IsRead2Enableable);
            Assert.False(chunk.IsRead3Enableable);
            Assert.True(chunk.IsWriteEnableable);
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            if (chunk.Count != 0 && RowBoundsRejections == 0)
            {
                try
                {
                    _ = chunk.IsRead3Enabled(-1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
                try
                {
                    chunk.SetWriteEnabled(chunk.Count, false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
            }

            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                if (_expected is not null && _seen is not null)
                {
                    Assert.True(_expected[entityIndex]);
                    Assert.False(_seen[entityIndex]);
                    _seen[entityIndex] = true;
                }
                ref readonly UltraSim.ECS.Tests.LayoutWide wide = ref chunk.ReadComponents1[row];
                long value = unchecked(
                    wide.A * 3L + wide.B * 5L + wide.C * 7L + wide.D * 11L +
                    chunk.ReadComponents2[row].Value * 13L +
                    chunk.ReadComponents3[row].Value * 17L + entityIndex + Salt);
                chunk.WriteComponents[row].Value = value;
                chunk.SetWriteEnabled(row, (value & 1) == 0);
                checksum = unchecked(checksum + value);
            }
            Rows += chunk.Count;
            if (chunk.ArchetypeId == _firstId)
                FirstRows += chunk.Count;
            else if (chunk.ArchetypeId == _secondId)
                SecondRows += chunk.Count;
            else
                throw new InvalidOperationException("The generated 3R1W query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct BalancedWideGeneratedVisitor :
        IComponentChunkReadReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;
        private readonly bool[]? _expected;
        private readonly bool[]? _seen;

        internal BalancedWideGeneratedVisitor(
            ArchetypeId firstId,
            ArchetypeId secondId,
            long salt,
            bool[]? expected = null,
            bool[]? seen = null)
        {
            _firstId = firstId;
            _secondId = secondId;
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            FirstRows = 0;
            SecondRows = 0;
            NonZeroRowOffsets = 0;
            NonEnableableWriteRejections = 0;
            RowBoundsRejections = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int FirstRows;
        internal int SecondRows;
        internal int NonZeroRowOffsets;
        internal int NonEnableableWriteRejections;
        internal int RowBoundsRejections;
        internal long Checksum;

        public void Execute(in ReadReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.False(chunk.IsRead1Enableable);
            Assert.False(chunk.IsRead2Enableable);
            Assert.False(chunk.IsWrite1Enableable);
            Assert.True(chunk.IsWrite2Enableable);
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            if (chunk.Count != 0 && NonEnableableWriteRejections == 0)
            {
                try
                {
                    chunk.SetWrite1Enabled(0, false);
                }
                catch (InvalidOperationException)
                {
                    NonEnableableWriteRejections++;
                }
                try
                {
                    _ = chunk.IsRead2Enabled(-1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
                try
                {
                    chunk.SetWrite2Enabled(chunk.Count, false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
            }

            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                if (_expected is not null && _seen is not null)
                {
                    Assert.True(_expected[entityIndex]);
                    Assert.False(_seen[entityIndex]);
                    _seen[entityIndex] = true;
                }
                ref readonly UltraSim.ECS.Tests.LayoutWide wide = ref chunk.ReadComponents1[row];
                byte narrow = chunk.ReadComponents2[row].Value;
                int integer = unchecked((int)(wide.A + wide.C + narrow + entityIndex + Salt));
                long value = unchecked(wide.B * 5L + wide.D * 11L + integer * 13L + narrow + Salt);
                chunk.WriteComponents1[row].Value = integer;
                chunk.WriteComponents2[row].Value = value;
                chunk.SetWrite2Enabled(row, (value & 1) == 0);
                checksum = unchecked(checksum + integer + value);
            }
            Rows += chunk.Count;
            if (chunk.ArchetypeId == _firstId)
                FirstRows += chunk.Count;
            else if (chunk.ArchetypeId == _secondId)
                SecondRows += chunk.Count;
            else
                throw new InvalidOperationException("The generated 2R2W query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct GeneratedWideBarrierProbeVisitor<TArena> :
        IComponentChunkReadReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> _plan;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly Entity _entity;
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal GeneratedWideBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeReadReadWriteWriteQueryPlan<
                TArena,
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutByte,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong> plan,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            Entity entity,
            ArchetypeId firstId,
            ArchetypeId secondId)
        {
            _world = world;
            _plan = plan;
            _binding = binding;
            _entity = entity;
            _firstId = firstId;
            _secondId = secondId;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new BalancedWideGeneratedVisitor(_firstId, _secondId, 0);
                _world.VisitReadReadWriteWriteChunks(_plan, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingGeneratedWideVisitor :
        IComponentChunkReadReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        public void Execute(in ReadReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk) => throw new ChunkVisitorSentinelException();
    }

    private struct WideIntGeneratedReadVisitor :
        IComponentChunkReadReadVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>
    {
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal WideIntGeneratedReadVisitor(ArchetypeId firstId, ArchetypeId secondId, long salt)
        {
            _firstId = firstId;
            _secondId = secondId;
            Salt = salt;
            Rows = 0;
            FirstRows = 0;
            SecondRows = 0;
            RowBoundsRejections = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int FirstRows;
        internal int SecondRows;
        internal int RowBoundsRejections;
        internal long Checksum;

        public void Execute(in ReadReadComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> chunk)
        {
            Assert.False(chunk.IsRead1Enableable);
            Assert.False(chunk.IsRead2Enableable);
            if (chunk.Count != 0 && RowBoundsRejections == 0)
            {
                try
                {
                    _ = chunk.IsRead1Enabled(-1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
                try
                {
                    _ = chunk.IsRead2Enabled(chunk.Count);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
            }

            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Assert.True(chunk.IsRead1Enabled(row));
                Assert.True(chunk.IsRead2Enabled(row));
                checksum = unchecked(checksum + GeneratedQueryValue(
                    chunk.ReadComponents1[row].A,
                    chunk.ReadComponents2[row].Value,
                    chunk.EntityIndices[row],
                    Salt));
            }

            Rows += chunk.Count;
            if (chunk.ArchetypeId == _firstId)
                FirstRows += chunk.Count;
            else if (chunk.ArchetypeId == _secondId)
                SecondRows += chunk.Count;
            else
                throw new InvalidOperationException("The generated read query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct FilteredWideIntGeneratedReadVisitor :
        IComponentChunkReadReadVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>
    {
        private readonly bool[] _expected;
        private readonly bool[] _seen;

        internal FilteredWideIntGeneratedReadVisitor(bool[] expected, bool[] seen, long salt)
        {
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            Ranges = 0;
            NonZeroRowOffsets = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int Ranges;
        internal int NonZeroRowOffsets;
        internal long Checksum;

        public void Execute(in ReadReadComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> chunk)
        {
            long checksum = Checksum;
            Ranges++;
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.True(_expected[entityIndex]);
                Assert.False(_seen[entityIndex]);
                _seen[entityIndex] = true;
                checksum = unchecked(checksum + GeneratedQueryValue(
                    chunk.ReadComponents1[row].A,
                    chunk.ReadComponents2[row].Value,
                    entityIndex,
                    Salt));
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct ModelWideIntGeneratedReadVisitor :
        IComponentChunkReadReadVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>
    {
        private readonly long[] _expectedWide;
        private readonly int[] _expectedInt;
        private readonly long _salt;

        internal ModelWideIntGeneratedReadVisitor(long[] expectedWide, int[] expectedInt, long salt)
        {
            _expectedWide = expectedWide;
            _expectedInt = expectedInt;
            _salt = salt;
            Rows = 0;
            Checksum = 0;
        }

        internal int Rows;
        internal long Checksum;

        public void Execute(in ReadReadComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.Equal(_expectedWide[entityIndex], chunk.ReadComponents1[row].A);
                Assert.Equal(_expectedInt[entityIndex], chunk.ReadComponents2[row].Value);
                checksum = unchecked(checksum + GeneratedQueryValue(
                    chunk.ReadComponents1[row].A,
                    chunk.ReadComponents2[row].Value,
                    entityIndex,
                    _salt));
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct GeneratedReadQueryBarrierProbeVisitor<TArena> :
        IComponentChunkReadReadVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadReadQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> _plan;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly Entity _entity;
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal GeneratedReadQueryBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeReadReadQueryPlan<
                TArena,
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt> plan,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            Entity entity,
            ArchetypeId firstId,
            ArchetypeId secondId)
        {
            _world = world;
            _plan = plan;
            _binding = binding;
            _entity = entity;
            _firstId = firstId;
            _secondId = secondId;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadReadComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> chunk)
        {
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new WideIntGeneratedReadVisitor(_firstId, _secondId, 0);
                _world.VisitReadReadChunks(_plan, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingGeneratedReadQueryVisitor :
        IComponentChunkReadReadVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt>
    {
        public void Execute(in ReadReadComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> chunk) => throw new ChunkVisitorSentinelException();
    }

    private struct WideIntEnableGeneratedVisitor :
        IComponentChunkReadReadWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal WideIntEnableGeneratedVisitor(ArchetypeId firstId, ArchetypeId secondId, long salt)
        {
            _firstId = firstId;
            _secondId = secondId;
            Salt = salt;
            Rows = 0;
            FirstRows = 0;
            SecondRows = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int FirstRows;
        internal int SecondRows;
        internal long Checksum;

        public void Execute(in ReadReadWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.False(chunk.IsRead1Enableable);
            Assert.False(chunk.IsRead2Enableable);
            Assert.True(chunk.IsWriteEnableable);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Assert.True(chunk.IsRead1Enabled(row));
                Assert.True(chunk.IsRead2Enabled(row));
                long value = GeneratedQueryValue(
                    chunk.ReadComponents1[row].A,
                    chunk.ReadComponents2[row].Value,
                    chunk.EntityIndices[row],
                    Salt);
                chunk.WriteComponents[row].Value = value;
                bool enabled = (value & 1) == 0;
                chunk.SetWriteEnabled(row, enabled);
                checksum = unchecked(checksum + value + (enabled ? 1 : 0));
            }

            Rows += chunk.Count;
            if (chunk.ArchetypeId == _firstId)
                FirstRows += chunk.Count;
            else if (chunk.ArchetypeId == _secondId)
                SecondRows += chunk.Count;
            else
                throw new InvalidOperationException("The generated query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct FilteredWideIntEnableGeneratedVisitor :
        IComponentChunkReadReadWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly bool[] _expected;
        private readonly bool[] _seen;

        internal FilteredWideIntEnableGeneratedVisitor(bool[] expected, bool[] seen, long salt)
        {
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            Ranges = 0;
            NonZeroRowOffsets = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int Ranges;
        internal int NonZeroRowOffsets;
        internal long Checksum;

        public void Execute(in ReadReadWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            long checksum = Checksum;
            Ranges++;
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.True(_expected[entityIndex]);
                Assert.False(_seen[entityIndex]);
                Assert.False(chunk.IsWriteEnabled(row));
                long value = GeneratedQueryValue(
                    chunk.ReadComponents1[row].A,
                    chunk.ReadComponents2[row].Value,
                    entityIndex,
                    Salt);
                chunk.WriteComponents[row].Value = value;
                chunk.SetWriteEnabled(row, true);
                _seen[entityIndex] = true;
                checksum = unchecked(checksum + value);
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct GeneratedQueryBarrierProbeVisitor<TArena> :
        IComponentChunkReadReadWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadReadWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> _plan;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly Entity _entity;
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal GeneratedQueryBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeReadReadWriteQueryPlan<
                TArena,
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong> plan,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            Entity entity,
            ArchetypeId firstId,
            ArchetypeId secondId)
        {
            _world = world;
            _plan = plan;
            _binding = binding;
            _entity = entity;
            _firstId = firstId;
            _secondId = secondId;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadReadWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new WideIntEnableGeneratedVisitor(_firstId, _secondId, 0);
                _world.VisitReadReadWriteChunks(_plan, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingGeneratedQueryVisitor :
        IComponentChunkReadReadWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        public void Execute(in ReadReadWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk) => throw new ChunkVisitorSentinelException();
    }

    private struct WideIntEnableGeneratedWriteVisitor :
        IComponentChunkReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal WideIntEnableGeneratedWriteVisitor(
            ArchetypeId firstId,
            ArchetypeId secondId,
            long salt)
        {
            _firstId = firstId;
            _secondId = secondId;
            Salt = salt;
            Rows = 0;
            FirstRows = 0;
            SecondRows = 0;
            NonEnableableWriteRejections = 0;
            RowBoundsRejections = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int FirstRows;
        internal int SecondRows;
        internal int NonEnableableWriteRejections;
        internal int RowBoundsRejections;
        internal long Checksum;

        public void Execute(in ReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.False(chunk.IsReadEnableable);
            Assert.False(chunk.IsWrite1Enableable);
            Assert.True(chunk.IsWrite2Enableable);
            if (chunk.Count != 0 && NonEnableableWriteRejections == 0)
            {
                try
                {
                    chunk.SetWrite1Enabled(0, false);
                }
                catch (InvalidOperationException)
                {
                    NonEnableableWriteRejections++;
                }
                try
                {
                    _ = chunk.IsReadEnabled(chunk.Count);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
                try
                {
                    chunk.SetWrite2Enabled(-1, false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    RowBoundsRejections++;
                }
            }

            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Assert.True(chunk.IsReadEnabled(row));
                Assert.True(chunk.IsWrite1Enabled(row));
                GeneratedWriteQueryValues(
                    chunk.ReadComponents[row].A,
                    chunk.EntityIndices[row],
                    Salt,
                    out int integer,
                    out long value);
                chunk.WriteComponents1[row].Value = integer;
                chunk.WriteComponents2[row].Value = value;
                bool enabled = (value & 1) == 0;
                chunk.SetWrite2Enabled(row, enabled);
                checksum = unchecked(checksum + integer + value + (enabled ? 1 : 0));
            }

            Rows += chunk.Count;
            if (chunk.ArchetypeId == _firstId)
                FirstRows += chunk.Count;
            else if (chunk.ArchetypeId == _secondId)
                SecondRows += chunk.Count;
            else
                throw new InvalidOperationException("The generated write query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct FilteredWideIntEnableGeneratedWriteVisitor :
        IComponentChunkReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly bool[] _expected;
        private readonly bool[] _seen;

        internal FilteredWideIntEnableGeneratedWriteVisitor(bool[] expected, bool[] seen, long salt)
        {
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            Ranges = 0;
            NonZeroRowOffsets = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int Ranges;
        internal int NonZeroRowOffsets;
        internal long Checksum;

        public void Execute(in ReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            long checksum = Checksum;
            Ranges++;
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.True(_expected[entityIndex]);
                Assert.False(_seen[entityIndex]);
                Assert.False(chunk.IsWrite2Enabled(row));
                GeneratedWriteQueryValues(
                    chunk.ReadComponents[row].A,
                    entityIndex,
                    Salt,
                    out int integer,
                    out long value);
                chunk.WriteComponents1[row].Value = integer;
                chunk.WriteComponents2[row].Value = value;
                chunk.SetWrite2Enabled(row, true);
                _seen[entityIndex] = true;
                checksum = unchecked(checksum + integer + value);
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct GeneratedWriteQueryBarrierProbeVisitor<TArena> :
        IComponentChunkReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> _plan;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly Entity _entity;
        private readonly ArchetypeId _firstId;
        private readonly ArchetypeId _secondId;

        internal GeneratedWriteQueryBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeReadWriteWriteQueryPlan<
                TArena,
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong> plan,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            Entity entity,
            ArchetypeId firstId,
            ArchetypeId secondId)
        {
            _world = world;
            _plan = plan;
            _binding = binding;
            _entity = entity;
            _firstId = firstId;
            _secondId = secondId;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new WideIntEnableGeneratedWriteVisitor(_firstId, _secondId, 0);
                _world.VisitReadWriteWriteChunks(_plan, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingGeneratedWriteQueryVisitor :
        IComponentChunkReadWriteWriteVisitor<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong>
    {
        public void Execute(in ReadWriteWriteComponentChunk<
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> chunk) => throw new ChunkVisitorSentinelException();
    }

    private struct WideReadEnableWriteVisitor :
        IComponentChunkReadWriteVisitor<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly ArchetypeId _sourceId;
        private readonly ArchetypeId _destinationId;

        internal WideReadEnableWriteVisitor(
            ArchetypeId sourceId,
            ArchetypeId destinationId,
            long salt)
        {
            _sourceId = sourceId;
            _destinationId = destinationId;
            Salt = salt;
            Rows = 0;
            SourceRows = 0;
            DestinationRows = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int SourceRows;
        internal int DestinationRows;
        internal long Checksum;

        public void Execute(in ReadWriteComponentChunk<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.False(chunk.IsReadEnableable);
            Assert.True(chunk.IsWriteEnableable);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Assert.True(chunk.IsReadEnabled(row));
                long wide = chunk.ReadComponents[row].A;
                long value = unchecked(wide * 3L + chunk.EntityIndices[row] + Salt);
                chunk.WriteComponents[row].Value = value;
                bool enabled = (wide & 1) == 0;
                chunk.SetWriteEnabled(row, enabled);
                checksum = unchecked(checksum + value + (enabled ? 1 : 0));
            }

            Rows += chunk.Count;
            if (chunk.ArchetypeId == _sourceId)
                SourceRows += chunk.Count;
            else if (chunk.ArchetypeId == _destinationId)
                DestinationRows += chunk.Count;
            else
                throw new InvalidOperationException("The query visited an unmatched archetype.");
            Checksum = checksum;
        }
    }

    private struct FilteredWideVisitor :
        IComponentChunkReadWriteVisitor<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly bool[] _expected;
        private readonly bool[] _seen;

        internal FilteredWideVisitor(bool[] expected, bool[] seen, long salt)
        {
            _expected = expected;
            _seen = seen;
            Salt = salt;
            Rows = 0;
            Ranges = 0;
            NonZeroRowOffsets = 0;
            Checksum = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int Ranges;
        internal int NonZeroRowOffsets;
        internal long Checksum;

        public void Execute(in ReadWriteComponentChunk<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            long checksum = Checksum;
            Ranges++;
            if (chunk.RowOffset != 0)
                NonZeroRowOffsets++;
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.True(_expected[entityIndex]);
                Assert.False(_seen[entityIndex]);
                Assert.False(chunk.IsWriteEnabled(row));
                long value = unchecked(
                    chunk.ReadComponents[row].A * 5L + entityIndex + Salt);
                chunk.WriteComponents[row].Value = value;
                chunk.SetWriteEnabled(row, true);
                _seen[entityIndex] = true;
                checksum = unchecked(checksum + value);
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct ReadWriteBarrierProbeVisitor<TArena> :
        IComponentChunkReadWriteVisitor<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeReadWriteQueryPlan<TArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> _plan;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly Entity _entity;

        internal ReadWriteBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeReadWriteQueryPlan<TArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> plan,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            Entity entity)
        {
            _world = world;
            _plan = plan;
            _binding = binding;
            _entity = entity;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadWriteComponentChunk<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new WideReadEnableWriteVisitor(
                    chunk.ArchetypeId,
                    ArchetypeId.Invalid,
                    salt: 0);
                _world.VisitReadWriteChunks(_plan, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingReadWriteVisitor :
        IComponentChunkReadWriteVisitor<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>
    {
        public void Execute(in ReadWriteComponentChunk<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> chunk) =>
            throw new ChunkVisitorSentinelException();
    }

    private struct ChunkReadAuditVisitor : IComponentChunkReadVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly WorldId _worldId;
        private readonly ArchetypeId _archetypeId;
        private readonly int _rowCapacity;
        private readonly bool[] _seen;

        internal ChunkReadAuditVisitor(
            WorldId worldId,
            ArchetypeId archetypeId,
            int rowCapacity,
            bool[] seen)
        {
            _worldId = worldId;
            _archetypeId = archetypeId;
            _rowCapacity = rowCapacity;
            _seen = seen;
            Rows = 0;
            Chunks = 0;
            Checksum = 0;
        }

        internal int Rows;
        internal int Chunks;
        internal long Checksum;

        public void Execute(in ReadOnlyComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.Equal(_worldId, chunk.WorldId);
            Assert.Equal(_archetypeId, chunk.ArchetypeId);
            Assert.True(chunk.ChunkIndex < 2);
            Assert.True(chunk.Count > 0 && chunk.Count <= _rowCapacity);
            Assert.Equal(chunk.Count, chunk.EntityIndices.Length);
            Assert.Equal(chunk.Count, chunk.Components.Length);
            Assert.True(chunk.IsEnableable);
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                Assert.True(entityIndex < (uint)_seen.Length);
                Assert.False(_seen[entityIndex]);
                _seen[entityIndex] = true;
                Assert.True(chunk.IsEnabled(row));
                Checksum += chunk.Components[row].Value;
            }
            Rows += chunk.Count;
            Chunks++;
        }
    }

    private struct ChunkCountVisitor<T> : IComponentChunkReadVisitor<T>
        where T : unmanaged, IComponent<T>
    {
        internal int Chunks;

        public void Execute(in ReadOnlyComponentChunk<T> chunk) => Chunks++;
    }

    private struct EnableChunkWriter : IComponentChunkWriteVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
    {
        internal EnableChunkWriter(long salt)
        {
            Salt = salt;
            Rows = 0;
            Chunks = 0;
        }

        internal long Salt;
        internal int Rows;
        internal int Chunks;

        public void Execute(in WritableComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            Assert.True(chunk.IsEnableable);
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                chunk.Components[row].Value = unchecked(Salt + (entityIndex * 31L));
                chunk.SetEnabled(row, (entityIndex & 1) == 0);
            }
            Rows += chunk.Count;
            Chunks++;
        }
    }

    private struct OrdinaryChunkMisuseVisitor : IComponentChunkWriteVisitor<UltraSim.ECS.Tests.LayoutByte>
    {
        internal bool NonEnableableRejected;
        internal bool RowBoundsRejected;
        internal bool NegativeRowBoundsRejected;

        public void Execute(in WritableComponentChunk<UltraSim.ECS.Tests.LayoutByte> chunk)
        {
            Assert.False(chunk.IsEnableable);
            Assert.True(chunk.IsEnabled(0));
            try
            {
                chunk.SetEnabled(0, false);
            }
            catch (InvalidOperationException)
            {
                NonEnableableRejected = true;
            }

            try
            {
                chunk.IsEnabled(chunk.Count);
            }
            catch (ArgumentOutOfRangeException)
            {
                RowBoundsRejected = true;
            }

            try
            {
                chunk.IsEnabled(-1);
            }
            catch (ArgumentOutOfRangeException)
            {
                NegativeRowBoundsRejected = true;
            }
        }
    }

    private struct ChunkBarrierProbeVisitor<TArena> : IComponentChunkReadVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
        where TArena : class, IChunkArenaBackend
    {
        private readonly EcsWorld<TArena> _world;
        private readonly ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> _binding;
        private readonly ArchetypeTransitionPlan<TArena> _transition;
        private readonly Entity _entity;

        internal ChunkBarrierProbeVisitor(
            EcsWorld<TArena> world,
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding,
            ArchetypeTransitionPlan<TArena> transition,
            Entity entity)
        {
            _world = world;
            _binding = binding;
            _transition = transition;
            _entity = entity;
            Rejections = 0;
        }

        internal int Rejections;

        public void Execute(in ReadOnlyComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            try
            {
                _world.TryCreateDefault(_binding.ArchetypeId, out _);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TryDestroy(_entity);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TryTransition(_entity, _transition, out _);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponent(
                    _entity,
                    _binding,
                    new UltraSim.ECS.Tests.LayoutEnableLong { Value = 1 });
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.TrySetComponentEnabled(_entity, _binding, false);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                _world.Dispose();
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
            try
            {
                var nested = new ChunkCountVisitor<UltraSim.ECS.Tests.LayoutEnableLong>();
                _world.VisitReadChunks(_binding, ref nested);
            }
            catch (InvalidOperationException)
            {
                Rejections++;
            }
        }
    }

    private struct ThrowingChunkVisitor : IComponentChunkReadVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
    {
        public void Execute(in ReadOnlyComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk) =>
            throw new ChunkVisitorSentinelException();
    }

    private sealed class ChunkVisitorSentinelException : Exception;

    private struct ChunkModelAuditVisitor : IComponentChunkReadVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
    {
        private readonly long[] _expected;
        private readonly bool[] _enabled;

        internal ChunkModelAuditVisitor(long[] expected, bool[] enabled)
        {
            _expected = expected;
            _enabled = enabled;
            Rows = 0;
        }

        internal int Rows;

        public void Execute(in ReadOnlyComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                int index = checked((int)chunk.EntityIndices[row]);
                Assert.Equal(_expected[index], chunk.Components[row].Value);
                Assert.Equal(_enabled[index], chunk.IsEnabled(row));
            }
            Rows += chunk.Count;
        }
    }

    private struct ChunkChecksumVisitor : IComponentChunkReadVisitor<UltraSim.ECS.Tests.LayoutEnableLong>
    {
        internal long Checksum;

        public void Execute(in ReadOnlyComponentChunk<UltraSim.ECS.Tests.LayoutEnableLong> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
                checksum = unchecked(checksum + chunk.Components[row].Value + (chunk.IsEnabled(row) ? 1 : 0));
            Checksum = checksum;
        }
    }

    private static long RunBoundAccessParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int operations = 10_000;
        using var fixture = new WorldFixture<TArena>(maximumChunks: 1, entityCapacity: 1);
        Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> binding = (i & 1) == 0
                ? fixture.SourceEnableBinding
                : fixture.DestinationEnableBinding;
            var value = new UltraSim.ECS.Tests.LayoutEnableLong { Value = i * 19L };
            Assert.True(fixture.World.TrySetComponent(entity, binding, value));
            Assert.True(fixture.World.TrySetComponentEnabled(entity, binding, (i & 2) == 0));
            Assert.True(fixture.World.TryGetComponent(
                entity,
                binding,
                out UltraSim.ECS.Tests.LayoutEnableLong read));
            Assert.True(fixture.World.TryGetComponentEnabled(entity, binding, out bool isEnabled));
            checksum = unchecked(checksum + read.Value + (isEnabled ? 1 : 0));
            ArchetypeTransitionPlan<TArena> plan = (i & 1) == 0
                ? fixture.SourceToDestination
                : fixture.DestinationToSource;
            Assert.True(fixture.World.TryTransition(entity, plan, out _));
        }
        return checksum;
    }

    private static long RunWorldLifecycleParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int operations = 10_000;
        using var fixture = new WorldFixture<TArena>(maximumChunks: 1, entityCapacity: 1);
        long checksum = 0;
        for (int i = 0; i < operations; i++)
        {
            Assert.True(fixture.World.TryCreateDefault(fixture.Source.Id, out Entity entity));
            Assert.True(fixture.World.TryGetLocation(entity, out EntityLocation source));
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                source.ChunkRow,
                fixture.SourceEnable).Value = i * 17L;
            fixture.Source.Store.SetEnabled(source.ChunkRow, fixture.SourceEnable, (i & 1) == 0);
            Assert.True(fixture.World.TryTransition(
                entity,
                fixture.SourceToDestination,
                out EntityLocation destination));
            checksum = unchecked(checksum + entity.Generation + destination.ArchetypeId.Value);
            checksum = unchecked(checksum + fixture.Destination.Store
                .GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                    destination.ChunkRow,
                    fixture.DestinationEnable).Value);
            checksum = unchecked(checksum + (fixture.Destination.Store.IsEnabled(
                destination.ChunkRow,
                fixture.DestinationEnable) ? 1 : 0));
            Assert.True(fixture.World.TryDestroy(entity));
        }

        fixture.Source.Store.ValidateInvariants();
        fixture.Destination.Store.ValidateInvariants();
        return checksum;
    }

    private static long RunMigrationParity<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        const int entityCount = 257;
        const int operations = 4096;
        using var fixture = new MigrationFixture<TArena>(maximumChunks: 2, entityCapacity: entityCount);
        var entities = new Entity[entityCount];
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = fixture.AddToSource();
            Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation location));
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                location.ChunkRow,
                fixture.SourceEnable).Value = i * 17L;
            fixture.Source.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
                location.ChunkRow,
                fixture.SourceWide).A = i * 31L;
            fixture.Source.Store.SetEnabled(location.ChunkRow, fixture.SourceEnable, (i & 1) == 0);
            entities[i] = entity;
        }

        var random = new Random(0x51A7B17);
        for (int operation = 0; operation < operations; operation++)
        {
            Entity entity = entities[random.Next(entities.Length)];
            Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation location));
            ArchetypeTransitionPlan<TArena> plan = location.ArchetypeId == fixture.Source.Id
                ? fixture.SourceToDestination
                : fixture.DestinationToSource;
            Assert.True(ArchetypeMigrator.TryMigrate(fixture.Pool, entity, plan, out _));
        }

        long checksum = fixture.Source.Store.Count * 17L + fixture.Destination.Store.Count * 31L;
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            Assert.True(fixture.Pool.TryGetLocation(entity, out EntityLocation location));
            ArchetypeStorage<TArena> storage = location.ArchetypeId == fixture.Source.Id
                ? fixture.Source
                : fixture.Destination;
            ComponentColumnLayout enableColumn = location.ArchetypeId == fixture.Source.Id
                ? fixture.SourceEnable
                : fixture.DestinationEnable;
            ComponentColumnLayout wideColumn = location.ArchetypeId == fixture.Source.Id
                ? fixture.SourceWide
                : fixture.DestinationWide;
            checksum = unchecked(checksum +
                storage.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutEnableLong>(
                    location.ChunkRow,
                    enableColumn).Value);
            checksum = unchecked(checksum +
                storage.Store.GetComponentRef<UltraSim.ECS.Tests.LayoutWide>(
                    location.ChunkRow,
                    wideColumn).A);
            checksum = unchecked(checksum +
                (storage.Store.IsEnabled(location.ChunkRow, enableColumn) ? 1 : 0));
        }

        fixture.Source.Store.ValidateInvariants();
        fixture.Destination.Store.ValidateInvariants();
        return checksum;
    }

    private sealed class GeneratedQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        internal GeneratedQueryFixture(int maximumChunks, int entityCapacity)
        {
            ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
            World = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(entityCapacity),
                maximumArchetypes: 3,
                static capacity => CreateGeneratedQueryArena<TArena>(capacity),
                maximumQueryPlans: 6);

            ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
            ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
            ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
            ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
            ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

            ArchetypeId firstId = World.RegisterArchetype(
                [enableId, intId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            ArchetypeId secondId = World.RegisterArchetype(
                [byteId, enableId, intId, tagId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            _ = World.RegisterArchetype(
                [enableId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            First = World.GetArchetypeStorage(firstId);
            Second = World.GetArchetypeStorage(secondId);
            FirstEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(firstId, enableId);
            FirstIntBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutInt>(firstId, intId);
            FirstWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(firstId, wideId);
            SecondEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(secondId, enableId);
            SecondIntBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutInt>(secondId, intId);
            SecondTagBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutTag>(secondId, tagId);
            SecondWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(secondId, wideId);
            Query = World.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
            ArchetypeQueryFilter filter = World.CreateQueryFilter(
                [],
                [],
                [tagId],
                [enableId]);
            FilteredQuery = World.PrepareReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId, filter);
            WriteQuery = World.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId);
            FilteredWriteQuery = World.PrepareReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, enableId, filter);
            ReadQuery = World.PrepareReadReadQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt>(wideId, intId);
            FilteredReadQuery = World.PrepareReadReadQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt>(wideId, intId, filter);
            World.Seal();
        }

        internal EcsWorld<TArena> World { get; }
        internal ArchetypeStorage<TArena> First { get; }
        internal ArchetypeStorage<TArena> Second { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> FirstEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> FirstIntBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> FirstWideBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> SecondEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> SecondIntBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutTag> SecondTagBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> SecondWideBinding { get; }
        internal ArchetypeReadReadWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> Query { get; }
        internal ArchetypeReadReadWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> FilteredQuery { get; }
        internal ArchetypeReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> WriteQuery { get; }
        internal ArchetypeReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> FilteredWriteQuery { get; }
        internal ArchetypeReadReadQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> ReadQuery { get; }
        internal ArchetypeReadReadQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt> FilteredReadQuery { get; }

        public void Dispose() => World.Dispose();

        private static TRequestedArena CreateGeneratedQueryArena<TRequestedArena>(ChunkArenaCapacity capacity)
            where TRequestedArena : class, IChunkArenaBackend
        {
            object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported generated-query test arena {typeof(TRequestedArena).Name}.");
            return (TRequestedArena)arena;
        }
    }

    private sealed class GeneratedWideQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        internal GeneratedWideQueryFixture(int maximumChunks, int entityCapacity)
        {
            ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
            World = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(entityCapacity),
                maximumArchetypes: 3,
                static capacity => CreateArena<TArena>(capacity),
                maximumQueryPlans: 4);
            ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
            ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
            ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
            ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
            ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

            ArchetypeId firstId = World.RegisterArchetype(
                [byteId, enableId, intId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            ArchetypeId secondId = World.RegisterArchetype(
                [byteId, enableId, intId, tagId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            _ = World.RegisterArchetype(
                [enableId, intId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            First = World.GetArchetypeStorage(firstId);
            Second = World.GetArchetypeStorage(secondId);
            FirstByteBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutByte>(firstId, byteId);
            FirstEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(firstId, enableId);
            FirstIntBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutInt>(firstId, intId);
            FirstWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(firstId, wideId);
            SecondByteBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutByte>(secondId, byteId);
            SecondEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(secondId, enableId);
            SecondIntBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutInt>(secondId, intId);
            SecondTagBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutTag>(secondId, tagId);
            SecondWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(secondId, wideId);
            ThreeReadQuery = World.PrepareReadReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutByte,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, byteId, enableId);
            BalancedQuery = World.PrepareReadReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutByte,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, byteId, intId, enableId);
            ArchetypeQueryFilter filter = World.CreateQueryFilter([], [], [tagId], [enableId]);
            FilteredThreeReadQuery = World.PrepareReadReadReadWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutByte,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, intId, byteId, enableId, filter);
            FilteredBalancedQuery = World.PrepareReadReadWriteWriteQuery<
                UltraSim.ECS.Tests.LayoutWide,
                UltraSim.ECS.Tests.LayoutByte,
                UltraSim.ECS.Tests.LayoutInt,
                UltraSim.ECS.Tests.LayoutEnableLong>(wideId, byteId, intId, enableId, filter);
            World.Seal();
        }

        internal EcsWorld<TArena> World { get; }
        internal ArchetypeStorage<TArena> First { get; }
        internal ArchetypeStorage<TArena> Second { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> FirstByteBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> FirstEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> FirstIntBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> FirstWideBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> SecondByteBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> SecondEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> SecondIntBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutTag> SecondTagBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> SecondWideBinding { get; }
        internal ArchetypeReadReadReadWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong> ThreeReadQuery { get; }
        internal ArchetypeReadReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> BalancedQuery { get; }
        internal ArchetypeReadReadReadWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutEnableLong> FilteredThreeReadQuery { get; }
        internal ArchetypeReadReadWriteWriteQueryPlan<
            TArena,
            UltraSim.ECS.Tests.LayoutWide,
            UltraSim.ECS.Tests.LayoutByte,
            UltraSim.ECS.Tests.LayoutInt,
            UltraSim.ECS.Tests.LayoutEnableLong> FilteredBalancedQuery { get; }

        public void Dispose() => World.Dispose();

        private static TRequestedArena CreateArena<TRequestedArena>(ChunkArenaCapacity capacity)
            where TRequestedArena : class, IChunkArenaBackend
        {
            object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported wide generated-query test arena {typeof(TRequestedArena).Name}.");
            return (TRequestedArena)arena;
        }
    }

    private sealed class FilteredQueryFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        internal FilteredQueryFixture(int maximumChunks, int entityCapacity)
        {
            ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
            World = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(entityCapacity),
                maximumArchetypes: 3,
                static capacity => CreateFilteredQueryArena<TArena>(capacity),
                maximumQueryPlans: 1);

            ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
            ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
            ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
            ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
            ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();

            ArchetypeId matchingId = World.RegisterArchetype(
                [enableId, intId, tagId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            _ = World.RegisterArchetype(
                [enableId, intId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            _ = World.RegisterArchetype(
                [byteId, enableId, intId, tagId, wideId],
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            Matching = World.GetArchetypeStorage(matchingId);
            EnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(matchingId, enableId);
            TagBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutTag>(matchingId, tagId);
            WideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(matchingId, wideId);
            ArchetypeQueryFilter filter = World.CreateQueryFilter(
                [intId],
                [byteId],
                [tagId],
                [enableId]);
            Query = World
                .PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                    wideId,
                    enableId,
                    filter);
            World.Seal();
        }

        internal EcsWorld<TArena> World { get; }
        internal ArchetypeStorage<TArena> Matching { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> EnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutTag> TagBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> WideBinding { get; }
        internal ArchetypeReadWriteQueryPlan<TArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> Query { get; }

        public void Dispose() => World.Dispose();

        private static TRequestedArena CreateFilteredQueryArena<TRequestedArena>(ChunkArenaCapacity capacity)
            where TRequestedArena : class, IChunkArenaBackend
        {
            object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported filtered-query test arena {typeof(TRequestedArena).Name}.");
            return (TRequestedArena)arena;
        }
    }

    private sealed class WorldFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        internal WorldFixture(int maximumChunks, int entityCapacity)
        {
            ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
            World = new EcsWorld<TArena>(
                schema,
                new EntityCapacity(entityCapacity),
                maximumArchetypes: 2,
                static capacity => CreateWorldArena<TArena>(capacity),
                maximumQueryPlans: 1);

            ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
            ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
            ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
            ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
            ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
            ComponentId[] sourceSignature = [byteId, enableId, wideId];
            ComponentId[] destinationSignature = [enableId, intId, tagId, wideId];

            ArchetypeId sourceId = World.RegisterArchetype(
                sourceSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            ArchetypeId destinationId = World.RegisterArchetype(
                destinationSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            Source = World.GetArchetypeStorage(sourceId);
            Destination = World.GetArchetypeStorage(destinationId);
            SourceToDestination = World.PrepareTransition(sourceId, destinationId);
            DestinationToSource = World.PrepareTransition(destinationId, sourceId);

            SourceByteBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutByte>(sourceId, byteId);
            SourceEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(sourceId, enableId);
            SourceWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(sourceId, wideId);
            DestinationEnableBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutEnableLong>(destinationId, enableId);
            DestinationIntBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutInt>(destinationId, intId);
            DestinationTagBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutTag>(destinationId, tagId);
            DestinationWideBinding = World.BindComponent<UltraSim.ECS.Tests.LayoutWide>(destinationId, wideId);
            WideReadEnableWriteQuery = World
                .PrepareReadWriteQuery<UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong>(
                    wideId,
                    enableId);

            SourceByte = FindColumn(Source.Columns, byteId);
            SourceEnable = FindColumn(Source.Columns, enableId);
            DestinationEnable = FindColumn(Destination.Columns, enableId);
            World.Seal();
        }

        internal EcsWorld<TArena> World { get; }
        internal ArchetypeStorage<TArena> Source { get; }
        internal ArchetypeStorage<TArena> Destination { get; }
        internal ArchetypeTransitionPlan<TArena> SourceToDestination { get; }
        internal ArchetypeTransitionPlan<TArena> DestinationToSource { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutByte> SourceByteBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> SourceEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> SourceWideBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutEnableLong> DestinationEnableBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutInt> DestinationIntBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutTag> DestinationTagBinding { get; }
        internal ArchetypeComponentBinding<UltraSim.ECS.Tests.LayoutWide> DestinationWideBinding { get; }
        internal ArchetypeReadWriteQueryPlan<TArena, UltraSim.ECS.Tests.LayoutWide, UltraSim.ECS.Tests.LayoutEnableLong> WideReadEnableWriteQuery { get; }
        internal ComponentColumnLayout SourceByte { get; }
        internal ComponentColumnLayout SourceEnable { get; }
        internal ComponentColumnLayout DestinationEnable { get; }

        public void Dispose() => World.Dispose();

        private static TRequestedArena CreateWorldArena<TRequestedArena>(ChunkArenaCapacity capacity)
            where TRequestedArena : class, IChunkArenaBackend
        {
            object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException(
                        $"Unsupported world test arena {typeof(TRequestedArena).Name}.");
            return (TRequestedArena)arena;
        }
    }

    private sealed class MigrationFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        internal MigrationFixture(int maximumChunks, int entityCapacity)
        {
            ComponentSchema schema = UltraSim.ECS.Tests.LayoutTestSchema.CreateSchema();
            Registry = new ArchetypeRegistry<TArena>(
                schema,
                maximumArchetypes: 2,
                static capacity => CreateMigrationArena<TArena>(capacity));

            ComponentId byteId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutByte>();
            ComponentId enableId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutEnableLong>();
            ComponentId intId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutInt>();
            ComponentId tagId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutTag>();
            ComponentId wideId = UltraSim.ECS.Tests.LayoutTestSchema
                .GetComponentId<UltraSim.ECS.Tests.LayoutWide>();
            ComponentId[] sourceSignature = [byteId, enableId, wideId];
            ComponentId[] destinationSignature = [enableId, intId, tagId, wideId];

            ArchetypeId sourceId = Registry.Register(
                sourceSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            ArchetypeId destinationId = Registry.Register(
                destinationSignature,
                ChunkLayoutOptions.KiB16,
                maximumChunks);
            Source = Registry.GetStorage(sourceId);
            Destination = Registry.GetStorage(destinationId);
            SourceToDestination = Registry.PrepareTransition(sourceId, destinationId);
            DestinationToSource = Registry.PrepareTransition(destinationId, sourceId);
            Registry.Seal();

            SourceByte = FindColumn(Source.Columns, byteId);
            SourceEnable = FindColumn(Source.Columns, enableId);
            SourceWide = FindColumn(Source.Columns, wideId);
            DestinationEnable = FindColumn(Destination.Columns, enableId);
            DestinationInt = FindColumn(Destination.Columns, intId);
            DestinationTag = FindColumn(Destination.Columns, tagId);
            DestinationWide = FindColumn(Destination.Columns, wideId);
            Pool = new EntityPool(WorldId.Create(), new EntityCapacity(entityCapacity));
        }

        internal ArchetypeRegistry<TArena> Registry { get; }
        internal EntityPool Pool { get; }
        internal ArchetypeStorage<TArena> Source { get; }
        internal ArchetypeStorage<TArena> Destination { get; }
        internal ArchetypeTransitionPlan<TArena> SourceToDestination { get; }
        internal ArchetypeTransitionPlan<TArena> DestinationToSource { get; }
        internal ComponentColumnLayout SourceByte { get; }
        internal ComponentColumnLayout SourceEnable { get; }
        internal ComponentColumnLayout SourceWide { get; }
        internal ComponentColumnLayout DestinationEnable { get; }
        internal ComponentColumnLayout DestinationInt { get; }
        internal ComponentColumnLayout DestinationTag { get; }
        internal ComponentColumnLayout DestinationWide { get; }

        internal Entity AddToSource() => AddTo(Source);

        internal Entity AddToDestination() => AddTo(Destination);

        public void Dispose() => Registry.Dispose();

        private Entity AddTo(ArchetypeStorage<TArena> storage)
        {
            Entity entity = Pool.Allocate();
            if (!storage.Store.TryAdd(entity.Index, out ChunkRow row) ||
                !Pool.TryBindLocation(entity, new EntityLocation(storage.Id, row)))
            {
                throw new InvalidOperationException("Migration fixture initialization exceeded configured capacity.");
            }

            return entity;
        }

        private static TRequestedArena CreateMigrationArena<TRequestedArena>(ChunkArenaCapacity capacity)
            where TRequestedArena : class, IChunkArenaBackend
        {
            object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
                ? new NativeChunkArena(capacity)
                : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                    ? new PinnedChunkArena(capacity)
                    : throw new InvalidOperationException($"Unsupported migration test arena {typeof(TRequestedArena).Name}.");
            return (TRequestedArena)arena;
        }
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

    private static ComponentId[] CreateIndexedSignature(int mask)
    {
        int count = 0;
        for (int value = mask; value != 0; value >>= 1)
            count += value & 1;

        var signature = new ComponentId[count];
        int destination = 0;
        for (ushort component = 0; mask != 0; component++, mask >>= 1)
        {
            if ((mask & 1) != 0)
                signature[destination++] = new ComponentId(component);
        }

        return signature;
    }

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
