using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using System.Threading;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(RegisterComponent_AssignsStableIdsWithinSchema), RegisterComponent_AssignsStableIdsWithinSchema);
        Run(nameof(RegisterComponent_DuplicateRegistrationReturnsExistingId), RegisterComponent_DuplicateRegistrationReturnsExistingId);
        Run(nameof(RegisterComponent_RejectsInvalidTypes), RegisterComponent_RejectsInvalidTypes);
        Run(nameof(Build_FreezesBuilder), Build_FreezesBuilder);
        Run(nameof(Schemas_AreIsolatedAcrossBuilders), Schemas_AreIsolatedAcrossBuilders);
        Run(nameof(World_UsesSuppliedSchemaAndServices), World_UsesSuppliedSchemaAndServices);
        Run(nameof(Schema_CreatesColumnsFromRegisteredFactories), Schema_CreatesColumnsFromRegisteredFactories);
        Run(nameof(ArchetypeManager_NormalizesAndCachesSignatures), ArchetypeManager_NormalizesAndCachesSignatures);
        Run(nameof(ArchetypeManager_EnumerateWith_FiltersAndSnapshots), ArchetypeManager_EnumerateWith_FiltersAndSnapshots);
        Run(nameof(EntityManager_TracksEntityLifecycleAcrossArchetypes), EntityManager_TracksEntityLifecycleAcrossArchetypes);
        Run(nameof(Entity_ReusedSlotsIncrementVersion_AndOldHandlesStayDead), Entity_ReusedSlotsIncrementVersion_AndOldHandlesStayDead);
        Run(nameof(ComponentMask_IntersectionsAndSupersetsWork), ComponentMask_IntersectionsAndSupersetsWork);
        Run(nameof(SystemSchedule_FromDescriptorAndSettings_UsesNoPriorityField), SystemSchedule_FromDescriptorAndSettings_UsesNoPriorityField);
        Run(nameof(ExplicitSetComponent_MarksDirty), ExplicitSetComponent_MarksDirty);
        Run(nameof(PatchComponent_AndSetOrAddComponent_KeepWritesExplicitAndDirtyAware), PatchComponent_AndSetOrAddComponent_KeepWritesExplicitAndDirtyAware);
        Run(nameof(StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty), StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty);
        Run(nameof(StructuralQueue_BatchedAddRemoveAndDestroy_WorkAcrossMultipleEntities), StructuralQueue_BatchedAddRemoveAndDestroy_WorkAcrossMultipleEntities);
        Run(nameof(StructuralQueue_DestroyWholeArchetypeBatch_KeepsDirtyAndClearsAllRows), StructuralQueue_DestroyWholeArchetypeBatch_KeepsDirtyAndClearsAllRows);
        Run(nameof(StructuralQueue_MixedOperations_UseDeterministicPhaseOrder), StructuralQueue_MixedOperations_UseDeterministicPhaseOrder);
        Run(nameof(StructuralQueue_DestroySparseVictims_PreservesRemainingDataAndMarksDirty), StructuralQueue_DestroySparseVictims_PreservesRemainingDataAndMarksDirty);
        Run(nameof(QueryArchetypes_AndGetComponent_ExposeReadOnlyTraversal), QueryArchetypes_AndGetComponent_ExposeReadOnlyTraversal);
        Run(nameof(ReadOnlyQuery_AndChunkIteration_FilterByArchetype), ReadOnlyQuery_AndChunkIteration_FilterByArchetype);
        Run(nameof(QueryCaches_RefreshWhenStructuralChangesCreateNewArchetypes), QueryCaches_RefreshWhenStructuralChangesCreateNewArchetypes);
        Run(nameof(PatchQuery_UpdatesMatchedRowsAndMarksDirty), PatchQuery_UpdatesMatchedRowsAndMarksDirty);
        Run(nameof(PatchChunks_UpdatesChunkRowsAndMarksDirty), PatchChunks_UpdatesChunkRowsAndMarksDirty);
        Run(nameof(PatchChunksSparse_MarksOnlyTouchedRowsDirty), PatchChunksSparse_MarksOnlyTouchedRowsDirty);
        Run(nameof(PatchChunksSparse_InvalidDirtyIndex_ThrowsArgumentException), PatchChunksSparse_InvalidDirtyIndex_ThrowsArgumentException);
        Run(nameof(PatchChunks_PreparedPlanCache_RefreshesAfterStructuralChanges), PatchChunks_PreparedPlanCache_RefreshesAfterStructuralChanges);
        Run(nameof(PatchChunks_RepeatedUniqueChunkMarks_DoNotDuplicateDirtyEntities), PatchChunks_RepeatedUniqueChunkMarks_DoNotDuplicateDirtyEntities);
        Run(nameof(ManualWorkerPool_RejectsReentrantParallelFor), ManualWorkerPool_RejectsReentrantParallelFor);
        Run(nameof(ComponentColumnCopier_RejectsMismatchedColumns), ComponentColumnCopier_RejectsMismatchedColumns);
        Run(nameof(SystemContext_ParallelFor_KeepsChunkWritesDeterministic), SystemContext_ParallelFor_KeepsChunkWritesDeterministic);
        Run(nameof(SystemContext_ParallelSparseChunkWrites_AreSafeInSerialMode), SystemContext_ParallelSparseChunkWrites_AreSafeInSerialMode);
        Run(nameof(SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags), SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags);
        Run(nameof(SystemManager_DueTicks_WrapAcrossUintBoundary), SystemManager_DueTicks_WrapAcrossUintBoundary);
        Run(nameof(SystemManager_RequiresOverridesRegistrationOrderWithinStage), SystemManager_RequiresOverridesRegistrationOrderWithinStage);
        Run(nameof(SystemManager_BatchesNonConflictingSystemsTogether), SystemManager_BatchesNonConflictingSystemsTogether);
        Run(nameof(World_DefaultPhaseStaggering_SpreadsNonCriticalSamePeriodSystems), World_DefaultPhaseStaggering_SpreadsNonCriticalSamePeriodSystems);
        Run(nameof(World_DefaultPhaseStaggering_KeepsMovementSamePeriodSystemsAligned), World_DefaultPhaseStaggering_KeepsMovementSamePeriodSystemsAligned);
        Run(nameof(World_ConfigurePhaseStaggeringFalse_KeepsSamePeriodSystemsAligned), World_ConfigurePhaseStaggeringFalse_KeepsSamePeriodSystemsAligned);
        Run(nameof(SystemManager_PhaseStaggering_AssignsOffsetsOnlyToNonCriticalStages), SystemManager_PhaseStaggering_AssignsOffsetsOnlyToNonCriticalStages);
        Run(nameof(SystemManager_ParallelMode_RunsNonConflictingBatchMembers), SystemManager_ParallelMode_RunsNonConflictingBatchMembers);
        Run(nameof(SystemManager_ParallelMode_DeferredStructuralCommands_AreCollectedSafely), SystemManager_ParallelMode_DeferredStructuralCommands_AreCollectedSafely);
        Run(nameof(SystemManager_ParallelMode_ExplicitWrites_MatchSerialOutcomeForDisjointMasks), SystemManager_ParallelMode_ExplicitWrites_MatchSerialOutcomeForDisjointMasks);
        Run(nameof(World_RandomizedMutationFuzz_MatchesReferenceModel), World_RandomizedMutationFuzz_MatchesReferenceModel);
        Run(nameof(World_SwapRemoveStress_UpdatesMovedEntityLocationsCorrectly), World_SwapRemoveStress_UpdatesMovedEntityLocationsCorrectly);
        Run(nameof(World_HeavyArchetypeMove_PreservesAllComponentData), World_HeavyArchetypeMove_PreservesAllComponentData);
        Run(nameof(World_CreateDestroyChurn_DoesNotResurrectStaleEntities), World_CreateDestroyChurn_DoesNotResurrectStaleEntities);
        Run(nameof(SystemManager_SerialAndParallel_MatchAcrossManyTicks), SystemManager_SerialAndParallel_MatchAcrossManyTicks);
        Run(nameof(World_ProfilingSnapshot_CapturesTickPhaseCounts), World_ProfilingSnapshot_CapturesTickPhaseCounts);
        Run(nameof(World_SaveLoadAndSaveAllLoadAll_UsePersistenceAndSystemHooks), World_SaveLoadAndSaveAllLoadAll_UsePersistenceAndSystemHooks);
        Run(nameof(WorldPersistenceView_CapturesFilteredReadOnlyArchetypeSet), WorldPersistenceView_CapturesFilteredReadOnlyArchetypeSet);
        Run(nameof(World_Lifecycle_AutoInitializesSealsAndShutdownBlocksFurtherUse), World_Lifecycle_AutoInitializesSealsAndShutdownBlocksFurtherUse);

        Console.WriteLine("All ECS scaffold tests passed.");
    }

    private static void RegisterComponent_AssignsStableIdsWithinSchema()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        EcsSchema schema = builder.Build();

        Assert.Equal(0, positionId.Value);
        Assert.Equal(1, velocityId.Value);
        Assert.Equal(positionId, schema.GetComponentId<Position>());
        Assert.Equal(velocityId, schema.GetComponentId<Velocity>());
        Assert.Equal(2, schema.ComponentCount);
    }

    private static void RegisterComponent_DuplicateRegistrationReturnsExistingId()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId first = builder.RegisterComponent<Position>();
        ComponentId second = builder.RegisterComponent<Position>();

        Assert.Equal(first, second);
        Assert.Equal(1, builder.ComponentCount);
    }

    private static void RegisterComponent_RejectsInvalidTypes()
    {
        var builder = new EcsSchemaBuilder();

        Assert.Throws<ArgumentException>(() => builder.RegisterComponent(typeof(string)));
        Assert.Throws<ArgumentException>(() => builder.RegisterComponent(typeof(NotSelfComponent)));
    }

    private static void Build_FreezesBuilder()
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<Position>();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.RegisterComponent<Velocity>());
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    private static void Schemas_AreIsolatedAcrossBuilders()
    {
        var firstBuilder = new EcsSchemaBuilder();
        var secondBuilder = new EcsSchemaBuilder();

        ComponentId firstOnly = firstBuilder.RegisterComponent<Position>();
        ComponentId secondOnly = secondBuilder.RegisterComponent<Velocity>();

        EcsSchema first = firstBuilder.Build();
        EcsSchema second = secondBuilder.Build();

        Assert.Equal(0, firstOnly.Value);
        Assert.Equal(0, secondOnly.Value);
        Assert.True(first.TryGetComponentId(typeof(Position), out _));
        Assert.False(first.TryGetComponentId(typeof(Velocity), out _));
        Assert.True(second.TryGetComponentId(typeof(Velocity), out _));
        Assert.False(second.TryGetComponentId(typeof(Position), out _));
    }

    private static void World_UsesSuppliedSchemaAndServices()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId id = builder.RegisterComponent<Position>();
        EcsSchema schema = builder.Build();
        var services = new EcsWorldServices
        {
            PrefabResolver = new NoOpPrefabResolver(),
            PersistenceAdapter = new NoOpPersistenceAdapter(),
        };

        var world = new World(schema, services);

        Assert.Same(schema, world.Schema);
        Assert.Same(services, world.Services);
        Assert.Equal(id, world.GetComponentId<Position>());
    }

    private static void Schema_CreatesColumnsFromRegisteredFactories()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId id = builder.RegisterComponent<Position>();
        EcsSchema schema = builder.Build();

        IComponentColumn column = schema.CreateColumn(id, 4);

        Assert.Equal(4, column.Capacity);
        column.EnsureCapacity(32);
        Assert.True(column.Capacity >= 32);
    }

    private static void ArchetypeManager_NormalizesAndCachesSignatures()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Archetype first = world.Archetypes.GetOrCreate([velocityId, positionId, velocityId]);
        Archetype second = world.Archetypes.GetOrCreate([positionId, velocityId]);

        Assert.Same(first, second);
        Assert.Equal(2, first.ComponentIds.Length);
        Assert.Equal(positionId, first.ComponentIds[0]);
        Assert.Equal(velocityId, first.ComponentIds[1]);
        Assert.True(first.HasComponent(positionId));
        Assert.True(first.HasComponent(velocityId));
        Assert.Equal(1, world.Archetypes.Count);
    }

    private static void ArchetypeManager_EnumerateWith_FiltersAndSnapshots()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        ComponentId accelerationId = builder.RegisterComponent<Acceleration>();
        var world = new World(builder.Build());

        Archetype positionOnly = world.Archetypes.GetOrCreate([positionId]);
        Archetype velocityArchetype = world.Archetypes.GetOrCreate([positionId, velocityId]);
        ArchetypeManager.ArchetypeEnumerator enumerator = world.Archetypes.EnumerateWith([velocityId]);

        Archetype lateMatching = world.Archetypes.GetOrCreate([velocityId, accelerationId]);

        var matches = new List<Archetype>();
        while (enumerator.MoveNext())
            matches.Add(enumerator.Current);

        Assert.Equal(1, matches.Count);
        Assert.Same(velocityArchetype, matches[0]);
        Assert.False(matches.Contains(positionOnly));
        Assert.False(matches.Contains(lateMatching));
    }

    private static void EntityManager_TracksEntityLifecycleAcrossArchetypes()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());
        Archetype archetype = world.Archetypes.GetOrCreate([positionId]);

        Entity entity = world.EntityStore.Allocate();
        int slot = archetype.AddEntity(entity);
        world.EntityStore.SetLocation(entity, archetype, slot);

        Assert.True(world.Entities.IsValid(entity));
        Assert.True(world.Entities.IsAlive(entity));
        Assert.Equal(1, world.Entities.AliveCount);
        Assert.True(world.EntityStore.TryGetLocation(entity, out Archetype locatedArchetype, out int locatedSlot));
        Assert.Same(archetype, locatedArchetype);
        Assert.Equal(slot, locatedSlot);

        bool removed = archetype.RemoveAtSwap(slot, out Entity moved, out int movedSlot);
        Assert.True(removed);
        Assert.Equal(Entity.Null, moved);
        Assert.Equal(-1, movedSlot);

        world.EntityStore.Free(entity);

        Assert.False(world.Entities.IsAlive(entity));
        Assert.False(world.Entities.IsValid(entity));
        Assert.Equal(0, world.Entities.AliveCount);
    }

    private static void Entity_ReusedSlotsIncrementVersion_AndOldHandlesStayDead()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());
        var signature = new ArchetypeSignature([positionId]);

        Entity first = world.CreateDefault(signature);
        Assert.True(world.SetComponent(first, new Position(10)));
        world.Destroy(first);

        Assert.False(world.Entities.IsAlive(first));
        Assert.False(world.Entities.IsValid(first));
        Assert.False(world.SetComponent(first, new Position(99)));
        Assert.False(world.TryGetComponent(first, out Position _));

        Entity second = world.CreateDefault(signature);
        Assert.Equal(first.Index, second.Index);
        Assert.True(second.Version > first.Version);
        Assert.True(first != second);
        Assert.True(world.Entities.IsAlive(second));
        Assert.True(world.Entities.IsValid(second));
        Assert.False(world.Entities.IsAlive(first));
        Assert.False(world.Entities.IsValid(first));
    }

    private static void ComponentMask_IntersectionsAndSupersetsWork()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        ComponentId accelerationId = builder.RegisterComponent<Acceleration>();
        builder.Build();

        ComponentMask movement = ComponentMask.FromIds([positionId, velocityId]);
        ComponentMask velocityOnly = ComponentMask.FromIds([velocityId]);
        ComponentMask accelerationOnly = ComponentMask.FromIds([accelerationId]);

        Assert.True(movement.Intersects(velocityOnly));
        Assert.False(movement.Intersects(accelerationOnly));
        Assert.True(movement.IsSupersetOf(velocityOnly));
        Assert.False(velocityOnly.IsSupersetOf(movement));
    }

    private static void SystemSchedule_FromDescriptorAndSettings_UsesNoPriorityField()
    {
        var settings = new PeriodOverrideSettings(8, 3, SystemStage.WorldSim);
        var descriptor = new SystemDescriptor(
            "TestSystem",
            SystemStage.AI,
            defaultPeriodTicks: 4,
            defaultStartOffset: 1,
            ComponentMask.Empty,
            ComponentMask.Empty,
            manual: true);

        SystemSchedule schedule = SystemSchedule.FromDescriptor(descriptor);
        settings.Apply(ref schedule);

        Assert.True(schedule.Enabled);
        Assert.Equal(8, schedule.PeriodTicks);
        Assert.Equal(3, schedule.StartOffset);
        Assert.Equal(SystemStage.WorldSim, schedule.Stage);
    }

    private static void ExplicitSetComponent_MarksDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());
        Entity entity = world.CreateDefault(new ArchetypeSignature([positionId]));

        bool updated = world.SetComponent(entity, new Position(42));

        Assert.True(updated);
        Assert.True(world.TryGetComponent(entity, out Position value));
        Assert.Equal(new Position(42), value);

        Span<Entity> dirty = stackalloc Entity[8];
        Assert.Equal(1, world.CopyDirtyTo<Position>(dirty));
        Assert.Equal(entity, dirty[0]);
        Assert.Equal(1, world.DrainDirtyTo(dirty));
        Assert.Equal(entity, dirty[0]);
        Assert.Equal(0, world.DrainDirtyTo(dirty));
    }

    private static void PatchComponent_AndSetOrAddComponent_KeepWritesExplicitAndDirtyAware()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());
        Entity entity = world.CreateDefault(new ArchetypeSignature([positionId]));

        Assert.True(world.SetComponent(entity, new Position(10)));
        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);

        bool patched = world.PatchComponent<Position>(entity, static (ref Position position) =>
        {
            position = new Position(position.X + 5);
        });

        Assert.True(patched);
        Assert.Equal(new Position(15), world.GetComponent<Position>(entity));
        Assert.Equal(1, world.DrainDirtyTo<Position>(dirty));
        Assert.Equal(entity, dirty[0]);

        bool missingPatch = world.PatchComponent<Velocity>(entity, static (ref Velocity velocity) =>
        {
            velocity = new Velocity(velocity.X + 1);
        });

        Assert.False(missingPatch);
        Assert.Equal(0, world.DrainDirtyTo<Velocity>(dirty));

        world.SetOrAddComponent(entity, new Velocity(33));
        world.ProcessStructuralQueues();
        Assert.Equal(new Velocity(33), world.GetComponent<Velocity>(entity));
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));

        world.SetOrAddComponent(entity, new Velocity(44));
        Assert.Equal(new Velocity(44), world.GetComponent<Velocity>(entity));
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));

        world.RemoveComponent<Velocity>(entity);
        world.ProcessStructuralQueues();
        Assert.False(world.HasComponent<Velocity>(entity));
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));
    }

    private static void StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity entity = world.CreateDefault(new ArchetypeSignature([positionId]));
        Assert.True(world.SetComponent(entity, new Position(7)));
        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);

        world.EnqueueAdd(entity, new Velocity(9));
        world.ProcessStructuralQueues();

        Assert.True(world.HasComponent<Position>(entity));
        Assert.True(world.HasComponent<Velocity>(entity));
        Assert.True(world.TryGetComponent(entity, out Position positionAfterAdd));
        Assert.True(world.TryGetComponent(entity, out Velocity velocityAfterAdd));
        Assert.Equal(new Position(7), positionAfterAdd);
        Assert.Equal(new Velocity(9), velocityAfterAdd);
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));
        Assert.Equal(entity, dirty[0]);

        world.EnqueueRemove<Velocity>(entity);
        world.ProcessStructuralQueues();

        Assert.True(world.HasComponent<Position>(entity));
        Assert.False(world.HasComponent<Velocity>(entity));
        Assert.True(world.TryGetComponent(entity, out Position positionAfterRemove));
        Assert.Equal(new Position(7), positionAfterRemove);
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));
        Assert.Equal(entity, dirty[0]);

        world.EnqueueDestroy(entity);
        world.ProcessStructuralQueues();

        Assert.False(world.Entities.IsAlive(entity));
        Assert.Equal(1, world.DrainDirtyTo(dirty));
        Assert.Equal(entity, dirty[0]);
    }

    private static void StructuralQueue_BatchedAddRemoveAndDestroy_WorkAcrossMultipleEntities()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity third = world.CreateDefault(new ArchetypeSignature([positionId]));

        Assert.True(world.SetComponent(first, new Position(1)));
        Assert.True(world.SetComponent(second, new Position(2)));
        Assert.True(world.SetComponent(third, new Position(3)));

        Span<Entity> dirty = stackalloc Entity[16];
        world.DrainDirtyTo(dirty);

        Entity[] targets = [first, second, third];
        Velocity[] values = [new Velocity(11), new Velocity(12), new Velocity(13)];

        world.EnqueueAdd<Velocity>(targets, values);
        world.ProcessStructuralQueues();

        for (int i = 0; i < targets.Length; i++)
        {
            Assert.True(world.TryGetComponent(targets[i], out Velocity velocity));
            Assert.Equal(values[i], velocity);
        }

        Assert.Equal(3, world.DrainDirtyTo<Velocity>(dirty));

        world.EnqueueRemove<Velocity>(targets);
        world.ProcessStructuralQueues();

        for (int i = 0; i < targets.Length; i++)
            Assert.False(world.HasComponent<Velocity>(targets[i]));

        Assert.Equal(3, world.DrainDirtyTo<Velocity>(dirty));

        world.EnqueueDestroy(targets);
        world.ProcessStructuralQueues();

        for (int i = 0; i < targets.Length; i++)
            Assert.False(world.Entities.IsAlive(targets[i]));
    }

    private static void StructuralQueue_DestroyWholeArchetypeBatch_KeepsDirtyAndClearsAllRows()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity third = world.CreateDefault(new ArchetypeSignature([positionId]));

        Assert.True(world.SetComponent(first, new Position(1)));
        Assert.True(world.SetComponent(second, new Position(2)));
        Assert.True(world.SetComponent(third, new Position(3)));

        Span<Entity> dirty = stackalloc Entity[16];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);

        Entity[] all = [first, second, third];
        world.EnqueueDestroy(all);
        world.ProcessStructuralQueues();

        Assert.Equal(0, world.Entities.AliveCount);
        Assert.False(world.Entities.IsAlive(first));
        Assert.False(world.Entities.IsAlive(second));
        Assert.False(world.Entities.IsAlive(third));
        Assert.Equal(3, world.DrainDirtyTo(dirty));
        AssertEntitySetsEqual(all, 3, dirty.ToArray());
    }

    private static void StructuralQueue_MixedOperations_UseDeterministicPhaseOrder()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity destroyAndAdd = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity removeThenAdd = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity untouched = world.CreateDefault(new ArchetypeSignature([positionId]));

        Assert.True(world.SetComponent(destroyAndAdd, new Position(1)));
        Assert.True(world.SetComponent(removeThenAdd, new Position(2)));
        Assert.True(world.SetComponent(removeThenAdd, new Velocity(3)));
        Assert.True(world.SetComponent(untouched, new Position(4)));

        Span<Entity> dirty = stackalloc Entity[16];
        world.DrainDirtyTo(dirty);

        world.EnqueueAdd(destroyAndAdd, new Velocity(50));
        world.EnqueueRemove<Velocity>(removeThenAdd);
        world.EnqueueAdd(removeThenAdd, new Velocity(60));
        world.EnqueueDestroy(destroyAndAdd);
        world.CreateDefault([new ArchetypeSignature([positionId])]);

        world.ProcessStructuralQueues();

        Assert.False(world.Entities.IsAlive(destroyAndAdd));
        Assert.True(world.Entities.IsAlive(removeThenAdd));
        Assert.True(world.Entities.IsAlive(untouched));
        Assert.Equal(3, world.Entities.AliveCount);

        Assert.True(world.TryGetComponent(removeThenAdd, out Velocity velocityAfterBarrier));
        Assert.Equal(new Velocity(60), velocityAfterBarrier);

        int positionOnlyArchetypeCount = 0;
        int positionOnlyEntities = 0;
        foreach (Archetype archetype in world.QueryArchetypes([positionId]))
        {
            if (!archetype.HasComponent(velocityId))
            {
                positionOnlyArchetypeCount++;
                positionOnlyEntities += archetype.Count;
            }
        }

        Assert.True(positionOnlyArchetypeCount >= 1);
        Assert.Equal(2, positionOnlyEntities);
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));
        Assert.Equal(removeThenAdd, dirty[0]);
    }

    private static void StructuralQueue_DestroySparseVictims_PreservesRemainingDataAndMarksDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());
        var signature = new ArchetypeSignature([positionId]);
        var entities = new Entity[32];
        var dirty = new Entity[64];

        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.CreateDefault(signature);
            Assert.True(world.SetComponent(entities[i], new Position(1000 + i)));
        }

        world.DrainDirtyTo(dirty);

        Entity[] victims = new Entity[entities.Length / 2];
        int write = 0;
        for (int i = entities.Length - 2; i >= 0; i -= 2)
            victims[write++] = entities[i];

        world.EnqueueDestroy(victims);
        world.ProcessStructuralQueues();

        Assert.Equal(victims.Length, world.DrainDirtyTo(dirty));
        AssertEntitySetsEqual(victims, victims.Length, dirty);

        for (int i = 0; i < victims.Length; i++)
        {
            Assert.False(world.Entities.IsAlive(victims[i]));
            Assert.False(world.Entities.IsValid(victims[i]));
            Assert.False(world.TryGetComponent(victims[i], out Position _));
        }

        for (int i = 1; i < entities.Length; i += 2)
        {
            Entity survivor = entities[i];
            Assert.True(world.Entities.IsAlive(survivor));
            Assert.True(world.TryGetComponent(survivor, out Position position));
            Assert.Equal(new Position(1000 + i), position);
        }
    }

    private static void QueryArchetypes_AndGetComponent_ExposeReadOnlyTraversal()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity withPosition = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity withBoth = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        world.CreateDefault(new ArchetypeSignature([]));

        Assert.True(world.SetComponent(withPosition, new Position(5)));
        Assert.True(world.SetComponent(withBoth, new Position(7)));
        Assert.True(world.SetComponent(withBoth, new Velocity(9)));

        Assert.Equal(new Position(5), world.GetComponent<Position>(withPosition));
        Assert.Equal(new Velocity(9), world.GetComponent<Velocity>(withBoth));
        Assert.Throws<InvalidOperationException>(() => world.GetComponent<Velocity>(withPosition));
        Assert.Throws<InvalidOperationException>(() => world.GetComponent<Position>(Entity.Null));

        int archetypesWithPosition = 0;
        int entitiesSeen = 0;
        foreach (Archetype archetype in world.QueryArchetypes([positionId]))
        {
            archetypesWithPosition++;
            entitiesSeen += archetype.Count;
            Assert.True(archetype.HasComponent(positionId));
        }

        Assert.Equal(2, archetypesWithPosition);
        Assert.Equal(2, entitiesSeen);

        int allArchetypes = 0;
        foreach (Archetype _ in world.QueryArchetypes([]))
            allArchetypes++;

        Assert.Equal(3, allArchetypes);
    }

    private static void ReadOnlyQuery_AndChunkIteration_FilterByArchetype()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        ComponentId accelerationId = builder.RegisterComponent<Acceleration>();
        var world = new World(builder.Build());

        Entity moving = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity accelerating = world.CreateDefault(new ArchetypeSignature([positionId, accelerationId]));
        Entity staticOnly = world.CreateDefault(new ArchetypeSignature([positionId]));

        Assert.True(world.SetComponent(moving, new Position(10)));
        Assert.True(world.SetComponent(moving, new Velocity(4)));
        Assert.True(world.SetComponent(accelerating, new Position(20)));
        Assert.True(world.SetComponent(accelerating, new Acceleration(3)));
        Assert.True(world.SetComponent(staticOnly, new Position(30)));
        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);

        int positionSum = 0;
        int velocityPairCount = world.Query<Position, Velocity>((Entity entity, in Position position, in Velocity velocity) =>
        {
            Assert.Equal(moving, entity);
            positionSum += position.X + velocity.X;
        });

        Assert.Equal(1, velocityPairCount);
        Assert.Equal(14, positionSum);

        int visitedPositions = 0;
        int totalPositionX = 0;
        int chunkCount = 0;
        foreach (World.QueryChunk<Position> chunk in world.QueryChunks<Position>())
        {
            chunkCount++;
            for (int i = 0; i < chunk.Entities.Length; i++)
            {
                visitedPositions++;
                totalPositionX += chunk.Component1[i].X;
            }
        }

        Assert.Equal(3, visitedPositions);
        Assert.Equal(60, totalPositionX);
        Assert.Equal(3, chunkCount);

        int readChunkCount = 0;
        long readChunkSum = 0;
        int matchedByReadChunks = world.ReadChunks<Position, Velocity>((ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, ReadOnlySpan<Velocity> velocities) =>
        {
            readChunkCount++;
            for (int i = 0; i < entities.Length; i++)
                readChunkSum += entities[i].Index + positions[i].X + velocities[i].X;
        });

        Assert.Equal(1, readChunkCount);
        Assert.Equal(1, matchedByReadChunks);
        Assert.Equal((long)moving.Index + 14L, readChunkSum);

        int tripleCount = world.Query<Position, Velocity, Acceleration>((Entity _, in Position _, in Velocity _, in Acceleration _) =>
        {
            throw new InvalidOperationException("No entity should match all three components.");
        });

        Assert.Equal(0, tripleCount);
    }

    private static void QueryCaches_RefreshWhenStructuralChangesCreateNewArchetypes()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity entity = world.CreateDefault(new ArchetypeSignature([positionId]));
        Assert.True(world.SetComponent(entity, new Position(10)));

        int initialMatched = world.Query<Position, Velocity>((Entity _, in Position _, in Velocity _) =>
        {
            throw new InvalidOperationException("No entity should match before velocity is added.");
        });

        Assert.Equal(0, initialMatched);

        int initialArchetypes = 0;
        foreach (Archetype _ in world.QueryArchetypes([positionId, velocityId]))
            initialArchetypes++;

        Assert.Equal(0, initialArchetypes);

        world.EnqueueAdd(entity, new Velocity(5));
        world.ProcessStructuralQueues();

        int matchedAfterAdd = 0;
        int checksum = 0;
        world.Query<Position, Velocity>((Entity seen, in Position position, in Velocity velocity) =>
        {
            matchedAfterAdd++;
            Assert.Equal(entity, seen);
            checksum += position.X + velocity.X;
        });

        Assert.Equal(1, matchedAfterAdd);
        Assert.Equal(15, checksum);

        int archetypesAfterAdd = 0;
        foreach (Archetype _ in world.QueryArchetypes([positionId, velocityId]))
            archetypesAfterAdd++;

        Assert.Equal(1, archetypesAfterAdd);
    }

    private static void PatchQuery_UpdatesMatchedRowsAndMarksDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Assert.True(world.SetComponent(first, new Position(10)));
        Assert.True(world.SetComponent(first, new Velocity(1)));
        Assert.True(world.SetComponent(second, new Position(20)));
        Assert.True(world.SetComponent(second, new Velocity(2)));

        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);

        int matched = world.PatchQuery<Position, Velocity, Position>((Entity _, in Position position, in Velocity velocity, ref Position write) =>
        {
            write = new Position(position.X + velocity.X);
        });

        Assert.Equal(2, matched);
        Assert.Equal(new Position(11), world.GetComponent<Position>(first));
        Assert.Equal(new Position(22), world.GetComponent<Position>(second));
        Assert.Equal(2, world.DrainDirtyTo<Position>(dirty));
        Assert.Equal(2, world.DrainDirtyTo(dirty));
        Assert.Equal(0, world.DrainDirtyTo<Velocity>(dirty));
    }

    private static void PatchChunks_UpdatesChunkRowsAndMarksDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        ComponentId accelerationId = builder.RegisterComponent<Acceleration>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity third = world.CreateDefault(new ArchetypeSignature([positionId, velocityId, accelerationId]));

        Assert.True(world.SetComponent(first, new Position(10)));
        Assert.True(world.SetComponent(first, new Velocity(1)));
        Assert.True(world.SetComponent(second, new Position(20)));
        Assert.True(world.SetComponent(second, new Velocity(2)));
        Assert.True(world.SetComponent(third, new Position(30)));
        Assert.True(world.SetComponent(third, new Velocity(3)));
        Assert.True(world.SetComponent(third, new Acceleration(4)));

        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);
        world.DrainDirtyTo<Acceleration>(dirty);

        int writeOnlyChunks = 0;
        int writeOnlyMatched = world.PatchChunks<Position>((ReadOnlySpan<Entity> entities, Span<Position> writes) =>
        {
            writeOnlyChunks++;
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Position(writes[i].X + (int)entities[i].Index);
        });

        Assert.Equal(3, writeOnlyMatched);
        Assert.Equal(2, writeOnlyChunks);
        Assert.Equal(new Position(10 + (int)first.Index), world.GetComponent<Position>(first));
        Assert.Equal(new Position(20 + (int)second.Index), world.GetComponent<Position>(second));
        Assert.Equal(new Position(30 + (int)third.Index), world.GetComponent<Position>(third));
        Assert.Equal(3, world.DrainDirtyTo<Position>(dirty));
        Assert.Equal(3, world.DrainDirtyTo(dirty));

        int readWriteChunks = 0;
        int readWriteMatched = world.PatchChunks<Position, Velocity>((ReadOnlySpan<Entity> _, ReadOnlySpan<Position> reads, Span<Velocity> writes) =>
        {
            readWriteChunks++;
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Velocity(reads[i].X + writes[i].X);
        });

        Assert.Equal(3, readWriteMatched);
        Assert.Equal(2, readWriteChunks);
        Assert.Equal(new Velocity(world.GetComponent<Position>(first).X + 1), world.GetComponent<Velocity>(first));
        Assert.Equal(new Velocity(world.GetComponent<Position>(second).X + 2), world.GetComponent<Velocity>(second));
        Assert.Equal(new Velocity(world.GetComponent<Position>(third).X + 3), world.GetComponent<Velocity>(third));
        Assert.Equal(3, world.DrainDirtyTo<Velocity>(dirty));
        Assert.Equal(3, world.DrainDirtyTo(dirty));

        int readReadWriteChunks = 0;
        int readReadWriteMatched = world.PatchChunks<Position, Velocity, Acceleration>((ReadOnlySpan<Entity> _, ReadOnlySpan<Position> positions, ReadOnlySpan<Velocity> velocities, Span<Acceleration> writes) =>
        {
            readReadWriteChunks++;
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Acceleration(positions[i].X + velocities[i].X);
        });

        Assert.Equal(1, readReadWriteMatched);
        Assert.Equal(1, readReadWriteChunks);
        Assert.Equal(new Acceleration(world.GetComponent<Position>(third).X + world.GetComponent<Velocity>(third).X), world.GetComponent<Acceleration>(third));
        Assert.Equal(1, world.DrainDirtyTo<Acceleration>(dirty));
        Assert.Equal(1, world.DrainDirtyTo(dirty));
        Assert.Equal(0, world.DrainDirtyTo<Position>(dirty));
        Assert.Equal(0, world.DrainDirtyTo<Velocity>(dirty));
    }

    private static void PatchChunks_PreparedPlanCache_RefreshesAfterStructuralChanges()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());
        var cache = new World.PatchChunkPlanCache<Position, Velocity>();

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Assert.True(world.SetComponent(first, new Position(10)));
        Assert.True(world.SetComponent(first, new Velocity(1)));

        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);

        int firstPass = world.PatchChunks(cache, (ReadOnlySpan<Entity> _, ReadOnlySpan<Position> reads, Span<Velocity> writes) =>
        {
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Velocity(reads[i].X + 5);
        });

        Assert.Equal(1, firstPass);
        Assert.Equal(new Velocity(15), world.GetComponent<Velocity>(first));
        Assert.Equal(1, world.DrainDirtyTo<Velocity>(dirty));

        Entity second = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Assert.True(world.SetComponent(second, new Position(20)));
        Assert.True(world.SetComponent(second, new Velocity(2)));
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);

        int secondPass = world.PatchChunks(cache, (ReadOnlySpan<Entity> _, ReadOnlySpan<Position> reads, Span<Velocity> writes) =>
        {
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Velocity(reads[i].X + 7);
        });

        Assert.Equal(2, secondPass);
        Assert.Equal(new Velocity(17), world.GetComponent<Velocity>(first));
        Assert.Equal(new Velocity(27), world.GetComponent<Velocity>(second));
        Assert.Equal(2, world.DrainDirtyTo<Velocity>(dirty));
    }

    private static void PatchChunks_RepeatedUniqueChunkMarks_DoNotDuplicateDirtyEntities()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Assert.True(world.SetComponent(first, new Position(1)));
        Assert.True(world.SetComponent(second, new Position(2)));
        Assert.True(world.SetComponent(first, new Velocity(10)));
        Assert.True(world.SetComponent(second, new Velocity(20)));

        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);

        world.PatchChunks<Velocity>((ReadOnlySpan<Entity> _, Span<Velocity> writes) =>
        {
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Velocity(writes[i].X + 1);
        });

        world.PatchChunks<Velocity>((ReadOnlySpan<Entity> _, Span<Velocity> writes) =>
        {
            for (int i = 0; i < writes.Length; i++)
                writes[i] = new Velocity(writes[i].X + 1);
        });

        int dirtyCount = world.DrainDirtyTo<Velocity>(dirty);
        Assert.Equal(2, dirtyCount);
        Assert.True(dirty[..dirtyCount].Contains(first));
        Assert.True(dirty[..dirtyCount].Contains(second));
    }

    private static void PatchChunksSparse_MarksOnlyTouchedRowsDirty()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity first = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity second = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity third = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Assert.True(world.SetComponent(first, new Position(10)));
        Assert.True(world.SetComponent(second, new Position(20)));
        Assert.True(world.SetComponent(third, new Position(30)));
        Assert.True(world.SetComponent(first, new Velocity(1)));
        Assert.True(world.SetComponent(second, new Velocity(2)));
        Assert.True(world.SetComponent(third, new Velocity(3)));

        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);
        world.DrainDirtyTo<Position>(dirty);
        world.DrainDirtyTo<Velocity>(dirty);

        int touched = world.PatchChunksSparse<Position, Velocity>((ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, Span<Velocity> writes, Span<int> dirtyIndices) =>
        {
            int count = 0;
            for (int i = 0; i < writes.Length; i++)
            {
                if (positions[i].X == 20)
                    continue;

                writes[i] = new Velocity(positions[i].X + 100);
                dirtyIndices[count++] = i;
            }

            return count;
        });

        Assert.Equal(2, touched);
        Assert.Equal(new Velocity(110), world.GetComponent<Velocity>(first));
        Assert.Equal(new Velocity(2), world.GetComponent<Velocity>(second));
        Assert.Equal(new Velocity(130), world.GetComponent<Velocity>(third));

        int dirtyCount = world.DrainDirtyTo<Velocity>(dirty);
        Assert.Equal(2, dirtyCount);
        Assert.True(dirty[..dirtyCount].Contains(first));
        Assert.True(dirty[..dirtyCount].Contains(third));
        Assert.False(dirty[..dirtyCount].Contains(second));
    }

    private static void PatchChunksSparse_InvalidDirtyIndex_ThrowsArgumentException()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([positionId]);
        world.CreateDefault(signature);

        Assert.Throws<ArgumentException>(() =>
            world.PatchChunksSparse<Position>((ReadOnlySpan<Entity> entities, Span<Position> writes, Span<int> dirtyIndices) =>
            {
                dirtyIndices[0] = entities.Length;
                return 1;
            }));
    }

    private static void ManualWorkerPool_RejectsReentrantParallelFor()
    {
        using var pool = new ManualWorkerPool(1);
        try
        {
            pool.ParallelFor(2, i =>
            {
                if (i == 0)
                    pool.ParallelFor(2, static _ => { });
            });
            throw new InvalidOperationException("Expected reentrant ParallelFor to fail.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.Equal("ManualWorkerPool.ParallelFor is not reentrant.", ex.Message);
        }
        catch (AggregateException ex)
        {
            Assert.True(ex.InnerExceptions.Count > 0);
            Assert.True(ex.InnerExceptions[0] is InvalidOperationException invalid
                && invalid.Message == "ManualWorkerPool.ParallelFor is not reentrant.");
        }
    }

    private static void ComponentColumnCopier_RejectsMismatchedColumns()
    {
        var source = new ComponentColumn<Position>(1);
        var destination = new ComponentColumn<Velocity>(1);

        Assert.Throws<InvalidCastException>(() =>
            ComponentColumnCopierCache<Position>.Copy(source, destination, 0, 0));
    }

    private static void SystemContext_ParallelFor_KeepsChunkWritesDeterministic()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        EcsSchema schema = builder.Build();

        World serialWorld = CreateParallelChunkTestWorld(schema, positionId, velocityId, SystemExecutionMode.Serial);
        World parallelWorld = CreateParallelChunkTestWorld(schema, positionId, velocityId, SystemExecutionMode.ParallelBatches);

        serialWorld.Tick(0.016f);
        parallelWorld.Tick(0.016f);

        for (int i = 0; i < 1024; i++)
        {
            Entity entity = new((uint)i, 1u);
            Assert.Equal(serialWorld.GetComponent<Position>(entity), parallelWorld.GetComponent<Position>(entity));
        }

        var serialDirty = new Entity[2048];
        var parallelDirty = new Entity[2048];
        AssertEntitySetsEqual(
            Enumerable.Range(0, 1024).Select(i => new Entity((uint)i, 1u)).ToArray(),
            serialWorld.DrainDirtyTo<Position>(serialDirty),
            serialDirty);
        AssertEntitySetsEqual(
            Enumerable.Range(0, 1024).Select(i => new Entity((uint)i, 1u)).ToArray(),
            parallelWorld.DrainDirtyTo<Position>(parallelDirty),
            parallelDirty);
        Assert.Equal(1024, serialWorld.DrainDirtyTo(serialDirty));
        Assert.Equal(1024, parallelWorld.DrainDirtyTo(parallelDirty));
    }

    private static void SystemContext_ParallelSparseChunkWrites_AreSafeInSerialMode()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        EcsSchema schema = builder.Build();

        var world = new World(schema);
        var signature = new ArchetypeSignature([positionId, velocityId]);
        for (int i = 0; i < 1024; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new Position(i));
            world.SetComponent(entity, new Velocity(i + 1));
        }

        Span<Entity> drain = stackalloc Entity[2048];
        world.DrainDirtyTo(drain);
        world.DrainDirtyTo<Position>(drain);
        world.DrainDirtyTo<Velocity>(drain);

        world.SetSystemExecutionMode(SystemExecutionMode.Serial);
        world.RegisterSystems(new ParallelSparsePositionSystem(
            new SystemDescriptor(
                "ParallelSparsePosition",
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId]),
                ComponentMask.FromIds([positionId]))));
        world.SealSystems();

        world.Tick(0.016f);

        for (int i = 0; i < 1024; i++)
        {
            Entity entity = new((uint)i, 1u);
            Position position = world.GetComponent<Position>(entity);
            int expected = (i & 1) == 0 ? i + (i + 1) : i;
            Assert.Equal(new Position(expected), position);
        }

        var dirty = new Entity[1024];
        Assert.Equal(512, world.DrainDirtyTo<Position>(dirty));
        Assert.Equal(512, world.DrainDirtyTo(dirty));
    }

    private static World CreateParallelChunkTestWorld(EcsSchema schema, ComponentId positionId, ComponentId velocityId, SystemExecutionMode mode)
    {
        var world = new World(schema);
        var signature = new ArchetypeSignature([positionId, velocityId]);
        for (int i = 0; i < 1024; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new Position(i));
            world.SetComponent(entity, new Velocity(i + 1));
        }

        Span<Entity> drain = stackalloc Entity[2048];
        world.DrainDirtyTo(drain);
        world.DrainDirtyTo<Position>(drain);
        world.DrainDirtyTo<Velocity>(drain);

        world.SetSystemExecutionMode(mode);
        world.RegisterSystems(new ParallelChunkPositionSystem(
            new SystemDescriptor(
                "ParallelChunkPosition",
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId]),
                ComponentMask.FromIds([positionId]))));
        world.SealSystems();
        return world;
    }

    private static void SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var log = new List<string>();

        var movement = new MovementLoggingSystem(
            "Movement",
            new SystemDescriptor("Movement", SystemStage.Movement, 1, 0, ComponentMask.Empty, ComponentMask.Empty),
            log);

        var ai = new AiLoggingSystem(
            "AI",
            new SystemDescriptor("AI", SystemStage.AI, 2, 1, ComponentMask.Empty, ComponentMask.Empty),
            log);

        var manual = new ManualLoggingSystem(
            "Manual",
            new SystemDescriptor("Manual", SystemStage.Input, 1, 0, ComponentMask.Empty, ComponentMask.Empty, manual: true),
            log);

        world.RegisterSystems(ai, movement, manual);
        world.SealSystems();

        world.Tick(0.016f);
        Assert.SequenceEqual(["Movement@0"], log);

        world.Tick(0.016f);
        Assert.SequenceEqual(["Movement@0", "Movement@1", "AI@1"], log);

        world.RunManualSystem<MovementLoggingSystem>(0.016f);
        Assert.SequenceEqual(["Movement@0", "Movement@1", "AI@1"], log);

        world.RunManualSystem(manual.Id, 0.016f);
        Assert.SequenceEqual(["Movement@0", "Movement@1", "AI@1", "Manual@2"], log);
    }

    private static void SystemManager_DueTicks_WrapAcrossUintBoundary()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var manager = new SystemManager();
        int ticksRun = 0;

        manager.Register(new CountingSystem(
            new SystemDescriptor("WrapCounter", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
            () => ticksRun++));
        manager.Seal();

        manager.Tick(world, uint.MaxValue - 1, 0.016f, world.Commands);
        manager.Tick(world, uint.MaxValue, 0.016f, world.Commands);
        manager.Tick(world, 0, 0.016f, world.Commands);
        manager.Tick(world, 1, 0.016f, world.Commands);
        manager.Tick(world, 2, 0.016f, world.Commands);
        manager.Tick(world, 3, 0.016f, world.Commands);
        manager.Tick(world, 4, 0.016f, world.Commands);

        Assert.Equal(2, ticksRun);
    }

    private static void SystemManager_RequiresOverridesRegistrationOrderWithinStage()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var log = new List<string>();

        var dependent = new DependentLoggingSystem(
            "Dependent",
            new SystemDescriptor(
                "Dependent",
                SystemStage.AI,
                1,
                0,
                ComponentMask.Empty,
                ComponentMask.Empty,
                requires: [DependencyLoggingSystemType]),
            log);

        var dependency = new DependencyLoggingSystem(
            "Dependency",
            new SystemDescriptor("Dependency", SystemStage.AI, 1, 0, ComponentMask.Empty, ComponentMask.Empty),
            log);

        world.RegisterSystems(dependent, dependency);
        world.SealSystems();
        world.Tick(0.016f);

        Assert.SequenceEqual(["Dependency@0", "Dependent@0"], log);
    }

    private static void SystemManager_BatchesNonConflictingSystemsTogether()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        world.RegisterSystems(
            new PositionWriterSystem(new SystemDescriptor(
                "PositionWriter",
                SystemStage.AI,
                1,
                0,
                ComponentMask.Empty,
                ComponentMask.FromIds([positionId]))),
            new PositionReaderSystem(new SystemDescriptor(
                "PositionReader",
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId]),
                ComponentMask.Empty)),
            new VelocityWriterSystem(new SystemDescriptor(
                "VelocityWriter",
                SystemStage.AI,
                1,
                0,
                ComponentMask.Empty,
                ComponentMask.FromIds([velocityId]))));

        world.SealSystems();

        SystemId[][] batches = world.GetStageBatchesSnapshot(SystemStage.AI);

        Assert.Equal(2, batches.Length);
        Assert.Equal(2, batches[0].Length);
        Assert.Equal(1, batches[1].Length);
        Assert.Equal(0, batches[0][0].Value);
        Assert.Equal(2, batches[0][1].Value);
        Assert.Equal(1, batches[1][0].Value);
    }

    private static void World_DefaultPhaseStaggering_SpreadsNonCriticalSamePeriodSystems()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var log = new List<string>();

        world.RegisterSystems(
            new AiLoggingSystem(
                "AiA",
                new SystemDescriptor("AiA", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new AiLoggingSystemTwo(
                "AiB",
                new SystemDescriptor("AiB", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new AiLoggingSystemThree(
                "AiC",
                new SystemDescriptor("AiC", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log));

        world.SealSystems();

        int[] offsets = world.GetSystemStartOffsetsSnapshot();
        Assert.Equal(0, offsets[0]);
        Assert.Equal(1, offsets[1]);
        Assert.Equal(2, offsets[2]);

        for (int i = 0; i < 4; i++)
            world.Tick(0.016f);

        Assert.SequenceEqual(["AiA@0", "AiB@1", "AiC@2"], log);
    }

    private static void World_DefaultPhaseStaggering_KeepsMovementSamePeriodSystemsAligned()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var log = new List<string>();

        world.RegisterSystems(
            new MovementLoggingSystem(
                "MovementA",
                new SystemDescriptor("MovementA", SystemStage.Movement, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new MovementLoggingSystemTwo(
                "MovementB",
                new SystemDescriptor("MovementB", SystemStage.Movement, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new MovementLoggingSystemThree(
                "MovementC",
                new SystemDescriptor("MovementC", SystemStage.Movement, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log));

        world.SealSystems();

        int[] offsets = world.GetSystemStartOffsetsSnapshot();
        Assert.Equal(0, offsets[0]);
        Assert.Equal(0, offsets[1]);
        Assert.Equal(0, offsets[2]);

        for (int i = 0; i < 4; i++)
            world.Tick(0.016f);

        Assert.SequenceEqual(["MovementA@0", "MovementB@0", "MovementC@0"], log);
    }

    private static void World_ConfigurePhaseStaggeringFalse_KeepsSamePeriodSystemsAligned()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        world.ConfigurePhaseStaggering(enabled: false);
        var log = new List<string>();

        world.RegisterSystems(
            new AiLoggingSystem(
                "AiA",
                new SystemDescriptor("AiA", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new AiLoggingSystemTwo(
                "AiB",
                new SystemDescriptor("AiB", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log),
            new AiLoggingSystemThree(
                "AiC",
                new SystemDescriptor("AiC", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                log));

        world.SealSystems();

        int[] offsets = world.GetSystemStartOffsetsSnapshot();
        Assert.Equal(0, offsets[0]);
        Assert.Equal(0, offsets[1]);
        Assert.Equal(0, offsets[2]);

        for (int i = 0; i < 4; i++)
            world.Tick(0.016f);

        Assert.SequenceEqual(["AiA@0", "AiB@0", "AiC@0"], log);
    }

    private static void SystemManager_PhaseStaggering_AssignsOffsetsOnlyToNonCriticalStages()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        world.ConfigurePhaseStaggering(enabled: true, maxNonCriticalStagger: 3);

        world.RegisterSystems(
            new MovementLoggingSystem(
                "MovementA",
                new SystemDescriptor("MovementA", SystemStage.Movement, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>()),
            new AiLoggingSystem(
                "AiA",
                new SystemDescriptor("AiA", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>()),
            new AiLoggingSystemTwo(
                "AiB",
                new SystemDescriptor("AiB", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>()),
            new AiLoggingSystemThree(
                "AiC",
                new SystemDescriptor("AiC", SystemStage.AI, 4, 0, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>()),
            new WorldSimLoggingSystem(
                "WorldSimA",
                new SystemDescriptor("WorldSimA", SystemStage.WorldSim, 4, 2, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>()));

        world.SealSystems();

        int[] offsets = world.GetSystemStartOffsetsSnapshot();

        Assert.Equal(0, offsets[0]);
        Assert.Equal(0, offsets[1]);
        Assert.Equal(1, offsets[2]);
        Assert.Equal(2, offsets[3]);
        Assert.Equal(2, offsets[4]);
    }

    private static void SystemManager_ParallelMode_RunsNonConflictingBatchMembers()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());
        world.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);

        int positionRuns = 0;
        int velocityRuns = 0;

        world.RegisterSystems(
            new CountingSystem(
                new SystemDescriptor(
                    "ParallelPosition",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.Empty,
                    ComponentMask.FromIds([positionId])),
                () => Interlocked.Increment(ref positionRuns)),
            new CountingSystemTwo(
                new SystemDescriptor(
                    "ParallelVelocity",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.Empty,
                    ComponentMask.FromIds([velocityId])),
                () => Interlocked.Increment(ref velocityRuns)));

        world.SealSystems();
        world.Tick(0.016f);

        Assert.Equal(1, positionRuns);
        Assert.Equal(1, velocityRuns);
    }

    private static void SystemManager_ParallelMode_DeferredStructuralCommands_AreCollectedSafely()
    {
        const int entityCount = 256;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());
        world.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);

        var emptySignature = new ArchetypeSignature(ReadOnlySpan<ComponentId>.Empty);
        var positionTargets = new Entity[entityCount];
        var velocityTargets = new Entity[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            positionTargets[i] = world.CreateDefault(emptySignature);
            velocityTargets[i] = world.CreateDefault(emptySignature);
        }

        world.RegisterSystems(
            new ContextActionSystem<ParallelStructuralPositionTag>(
                new SystemDescriptor(
                    "ParallelStructuralPosition",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.Empty,
                    ComponentMask.FromIds([positionId])),
                (in SystemContext ctx) =>
                {
                    for (int i = 0; i < positionTargets.Length; i++)
                    {
                        ctx.Commands.EnqueueAdd(positionTargets[i], new Position(i + 1));
                        if ((i & 31) == 0)
                            Thread.Yield();
                    }
                }),
            new ContextActionSystem<ParallelStructuralVelocityTag>(
                new SystemDescriptor(
                    "ParallelStructuralVelocity",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.Empty,
                    ComponentMask.FromIds([velocityId])),
                (in SystemContext ctx) =>
                {
                    for (int i = 0; i < velocityTargets.Length; i++)
                    {
                        ctx.Commands.EnqueueAdd(velocityTargets[i], new Velocity((i + 1) * 10));
                        if ((i & 31) == 0)
                            Thread.Yield();
                    }
                }));

        world.SealSystems();
        world.Tick(0.016f);

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(new Position(i + 1), world.GetComponent<Position>(positionTargets[i]));
            Assert.Equal(new Velocity((i + 1) * 10), world.GetComponent<Velocity>(velocityTargets[i]));
        }

        var dirtyPositions = new Entity[entityCount];
        var dirtyVelocities = new Entity[entityCount];
        Assert.Equal(entityCount, world.DrainDirtyTo<Position>(dirtyPositions));
        Assert.Equal(entityCount, world.DrainDirtyTo<Velocity>(dirtyVelocities));
    }

    private static void SystemManager_ParallelMode_ExplicitWrites_MatchSerialOutcomeForDisjointMasks()
    {
        const int entityCount = 128;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        EcsSchema schema = builder.Build();

        World serialWorld = CreateSeededDualComponentWorld(schema, entityCount, out Entity[] serialEntities);
        World parallelWorld = CreateSeededDualComponentWorld(schema, entityCount, out Entity[] parallelEntities);
        parallelWorld.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);

        RegisterExplicitWriteParitySystems(serialWorld, serialEntities, positionId, velocityId);
        RegisterExplicitWriteParitySystems(parallelWorld, parallelEntities, positionId, velocityId);

        serialWorld.SealSystems();
        parallelWorld.SealSystems();

        serialWorld.Tick(0.016f);
        parallelWorld.Tick(0.016f);

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(serialWorld.GetComponent<Position>(serialEntities[i]), parallelWorld.GetComponent<Position>(parallelEntities[i]));
            Assert.Equal(serialWorld.GetComponent<Velocity>(serialEntities[i]), parallelWorld.GetComponent<Velocity>(parallelEntities[i]));
        }

        var serialDirty = new Entity[entityCount];
        var parallelDirty = new Entity[entityCount];

        Assert.Equal(entityCount, serialWorld.DrainDirtyTo<Position>(serialDirty));
        Assert.Equal(entityCount, parallelWorld.DrainDirtyTo<Position>(parallelDirty));
        Assert.Equal(entityCount, serialWorld.DrainDirtyTo<Velocity>(serialDirty));
        Assert.Equal(entityCount, parallelWorld.DrainDirtyTo<Velocity>(parallelDirty));
        Assert.Equal(entityCount, serialWorld.DrainDirtyTo(serialDirty));
        Assert.Equal(entityCount, parallelWorld.DrainDirtyTo(parallelDirty));
    }

    private static void World_RandomizedMutationFuzz_MatchesReferenceModel()
    {
        const int initialEntities = 32;
        const int stepsPerSeed = 300;

        for (int seed = 1; seed <= 4; seed++)
        {
            var builder = new EcsSchemaBuilder();
            ComponentId positionId = builder.RegisterComponent<Position>();
            builder.RegisterComponent<Velocity>();
            var world = new World(builder.Build());
            var model = new ReferenceWorldModel();
            var random = new Random(seed);
            var entities = new List<Entity>(initialEntities * 2);

            var positionSignature = new ArchetypeSignature([positionId]);
            for (int i = 0; i < initialEntities; i++)
            {
                Entity entity = world.CreateDefault(positionSignature);
                int position = seed * 1_000 + i;
                Assert.True(world.SetComponent(entity, new Position(position)));

                bool startWithVelocity = (i & 1) == 0;
                if (startWithVelocity)
                    world.EnqueueAdd(entity, new Velocity(position / 2));

                model.Create(entity, position);
                if (startWithVelocity)
                    model.EnqueueAddVelocity(entity, position / 2);

                entities.Add(entity);
            }

            world.ProcessStructuralQueues();
            model.ProcessStructuralQueues();
            AssertWorldMatchesModel(world, model, entities);
            AssertDirtySetsMatchAndClear(world, model);

            for (int step = 0; step < stepsPerSeed; step++)
            {
                int operation = random.Next(8);
                Entity? target = TryPickRandomLiveEntity(model, random);

                switch (operation)
                {
                    case 0:
                    {
                        Entity entity = world.CreateDefault(positionSignature);
                        int position = random.Next(10_000);
                        Assert.True(world.SetComponent(entity, new Position(position)));
                        model.Create(entity, position);
                        entities.Add(entity);
                        break;
                    }

                    case 1 when target.HasValue:
                    {
                        int position = random.Next(10_000);
                        Assert.True(world.SetComponent(target.Value, new Position(position)));
                        model.SetPosition(target.Value, position);
                        break;
                    }

                    case 2 when target.HasValue:
                    {
                        int delta = random.Next(1, 17);
                        Assert.True(world.PatchComponent<Position>(target.Value, (ref Position value) =>
                        {
                            value = new Position(value.X + delta);
                        }));
                        model.PatchPosition(target.Value, delta);
                        break;
                    }

                    case 3 when target.HasValue:
                    {
                        int velocity = random.Next(10_000);
                        world.EnqueueAdd(target.Value, new Velocity(velocity));
                        model.EnqueueAddVelocity(target.Value, velocity);
                        break;
                    }

                    case 4 when target.HasValue:
                    {
                        world.EnqueueRemove<Velocity>(target.Value);
                        model.EnqueueRemoveVelocity(target.Value);
                        break;
                    }

                    case 5 when target.HasValue:
                    {
                        world.EnqueueDestroy(target.Value);
                        model.EnqueueDestroy(target.Value);
                        break;
                    }

                    case 6:
                    {
                        world.ProcessStructuralQueues();
                        model.ProcessStructuralQueues();
                        AssertWorldMatchesModel(world, model, entities);
                        AssertDirtySetsMatchAndClear(world, model);
                        break;
                    }

                    default:
                    {
                        AssertWorldMatchesModel(world, model, entities);
                        break;
                    }
                }

                if ((step % 19) == 0)
                {
                    world.ProcessStructuralQueues();
                    model.ProcessStructuralQueues();
                    AssertWorldMatchesModel(world, model, entities);
                    AssertDirtySetsMatchAndClear(world, model);
                }
                else if ((step % 23) == 0)
                {
                    AssertWorldMatchesModel(world, model, entities);
                }
            }

            world.ProcessStructuralQueues();
            model.ProcessStructuralQueues();
            AssertWorldMatchesModel(world, model, entities);
            AssertDirtySetsMatchAndClear(world, model);
        }
    }

    private static void World_SwapRemoveStress_UpdatesMovedEntityLocationsCorrectly()
    {
        const int entityCount = 96;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());
        var signature = new ArchetypeSignature([positionId, velocityId]);
        var live = new List<Entity>(entityCount);
        var expected = new Dictionary<Entity, (Position Position, Velocity Velocity)>(entityCount);

        for (int i = 0; i < entityCount; i++)
        {
            Entity entity = world.CreateDefault(signature);
            Position position = new(i + 10);
            Velocity velocity = new((i + 1) * 7);
            Assert.True(world.SetComponent(entity, position));
            Assert.True(world.SetComponent(entity, velocity));
            live.Add(entity);
            expected[entity] = (position, velocity);
        }

        for (int round = 0; round < entityCount / 2; round++)
        {
            int removeIndex = live.Count / 2;
            Entity victim = live[removeIndex];
            world.Destroy(victim);
            live.RemoveAt(removeIndex);
            expected.Remove(victim);

            Assert.False(world.Entities.IsAlive(victim));
            Assert.False(world.Entities.IsValid(victim));
            Assert.False(world.TryGetComponent(victim, out Position _));
            Assert.False(world.TryGetComponent(victim, out Velocity _));

            for (int i = 0; i < live.Count; i++)
            {
                Entity survivor = live[i];
                Assert.True(world.Entities.IsAlive(survivor));
                Assert.True(world.TryGetComponent(survivor, out Position position));
                Assert.True(world.TryGetComponent(survivor, out Velocity velocity));
                Assert.Equal(expected[survivor].Position, position);
                Assert.Equal(expected[survivor].Velocity, velocity);
            }
        }

        Assert.Equal(live.Count, world.Entities.AliveCount);
    }

    private static void World_HeavyArchetypeMove_PreservesAllComponentData()
    {
        const int entityCount = 256;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        builder.RegisterComponent<Acceleration>();
        var world = new World(builder.Build());
        var positionOnly = new ArchetypeSignature([positionId]);
        var entities = new Entity[entityCount];
        var velocityValues = new Velocity[entityCount];
        var accelerationValues = new Acceleration[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            Entity entity = world.CreateDefault(positionOnly);
            entities[i] = entity;
            Assert.True(world.SetComponent(entity, new Position(1_000 + i)));
            velocityValues[i] = new Velocity(2_000 + i);
            accelerationValues[i] = new Acceleration(3_000 + i);
        }

        world.EnqueueAdd<Velocity>((ReadOnlySpan<Entity>)entities, (ReadOnlySpan<Velocity>)velocityValues);
        world.ProcessStructuralQueues();

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(new Position(1_000 + i), world.GetComponent<Position>(entities[i]));
            Assert.Equal(velocityValues[i], world.GetComponent<Velocity>(entities[i]));
            Assert.False(world.HasComponent<Acceleration>(entities[i]));
        }

        world.EnqueueAdd<Acceleration>((ReadOnlySpan<Entity>)entities, (ReadOnlySpan<Acceleration>)accelerationValues);
        world.ProcessStructuralQueues();

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(new Position(1_000 + i), world.GetComponent<Position>(entities[i]));
            Assert.Equal(velocityValues[i], world.GetComponent<Velocity>(entities[i]));
            Assert.Equal(accelerationValues[i], world.GetComponent<Acceleration>(entities[i]));
        }

        world.EnqueueRemove<Velocity>(entities);
        world.ProcessStructuralQueues();

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(new Position(1_000 + i), world.GetComponent<Position>(entities[i]));
            Assert.False(world.HasComponent<Velocity>(entities[i]));
            Assert.Equal(accelerationValues[i], world.GetComponent<Acceleration>(entities[i]));
        }

        world.EnqueueRemove<Acceleration>(entities);
        world.ProcessStructuralQueues();

        for (int i = 0; i < entityCount; i++)
        {
            Assert.Equal(new Position(1_000 + i), world.GetComponent<Position>(entities[i]));
            Assert.False(world.HasComponent<Velocity>(entities[i]));
            Assert.False(world.HasComponent<Acceleration>(entities[i]));
        }
    }

    private static void World_CreateDestroyChurn_DoesNotResurrectStaleEntities()
    {
        const int rounds = 24;
        const int batchSize = 48;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        var world = new World(builder.Build());
        var signature = new ArchetypeSignature([positionId]);
        var live = new List<Entity>(batchSize * 2);
        var stale = new List<Entity>(rounds * batchSize);

        for (int round = 0; round < rounds; round++)
        {
            for (int i = 0; i < batchSize; i++)
            {
                Entity entity = world.CreateDefault(signature);
                Assert.True(world.SetComponent(entity, new Position(round * 1_000 + i)));
                live.Add(entity);
            }

            for (int i = live.Count - 1; i >= 0; i -= 2)
            {
                Entity entity = live[i];
                world.Destroy(entity);
                stale.Add(entity);
                live.RemoveAt(i);
            }

            foreach (Entity staleEntity in stale)
            {
                Assert.False(world.Entities.IsAlive(staleEntity));
                Assert.False(world.Entities.IsValid(staleEntity));
                Assert.False(world.TryGetComponent(staleEntity, out Position _));
            }

            foreach (Entity entity in live)
            {
                Assert.True(world.Entities.IsAlive(entity));
                Assert.True(world.Entities.IsValid(entity));
                Assert.True(world.TryGetComponent(entity, out Position _));
            }
        }

        for (int i = 0; i < batchSize * 3; i++)
        {
            Entity entity = world.CreateDefault(signature);
            Assert.True(world.SetComponent(entity, new Position(50_000 + i)));
            live.Add(entity);
        }

        foreach (Entity staleEntity in stale)
        {
            Assert.False(world.Entities.IsAlive(staleEntity));
            Assert.False(world.Entities.IsValid(staleEntity));
        }
    }

    private static void SystemManager_SerialAndParallel_MatchAcrossManyTicks()
    {
        const int entityCount = 128;
        const int ticks = 180;

        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        EcsSchema schema = builder.Build();

        World serialWorld = CreateSeededDualComponentWorld(schema, entityCount, out Entity[] serialEntities);
        World parallelWorld = CreateSeededDualComponentWorld(schema, entityCount, out Entity[] parallelEntities);
        parallelWorld.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);

        RegisterDeterminismSoakSystems(serialWorld, positionId, velocityId);
        RegisterDeterminismSoakSystems(parallelWorld, positionId, velocityId);

        serialWorld.SealSystems();
        parallelWorld.SealSystems();

        var serialDirty = new Entity[entityCount];
        var parallelDirty = new Entity[entityCount];

        for (int tick = 0; tick < ticks; tick++)
        {
            serialWorld.Tick(0.016f);
            parallelWorld.Tick(0.016f);

            for (int i = 0; i < entityCount; i++)
            {
                Assert.Equal(serialWorld.GetComponent<Position>(serialEntities[i]), parallelWorld.GetComponent<Position>(parallelEntities[i]));
                Assert.Equal(serialWorld.GetComponent<Velocity>(serialEntities[i]), parallelWorld.GetComponent<Velocity>(parallelEntities[i]));
            }

            AssertEntitySetsEqual(
                serialWorld.DrainDirtyTo<Position>(serialDirty),
                serialDirty,
                parallelWorld.DrainDirtyTo<Position>(parallelDirty),
                parallelDirty);

            AssertEntitySetsEqual(
                serialWorld.DrainDirtyTo<Velocity>(serialDirty),
                serialDirty,
                parallelWorld.DrainDirtyTo<Velocity>(parallelDirty),
                parallelDirty);

            AssertEntitySetsEqual(
                serialWorld.DrainDirtyTo(serialDirty),
                serialDirty,
                parallelWorld.DrainDirtyTo(parallelDirty),
                parallelDirty);
        }
    }

    private static void World_ProfilingSnapshot_CapturesTickPhaseCounts()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build())
        {
            ProfilingEnabled = true,
        };

        Entity target = world.CreateDefault(new ArchetypeSignature([positionId]));
        Span<Entity> dirty = stackalloc Entity[8];
        world.DrainDirtyTo(dirty);

        world.EnqueueCreateDefault(new ArchetypeSignature([positionId]));
        world.RegisterSystem(new ContextActionSystem<DeterminismVelocityTag>(
            new SystemDescriptor(
                "ProfileAdd",
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId]),
                ComponentMask.Empty),
            (in SystemContext ctx) =>
            {
                ctx.Commands.EnqueueAdd(target, new Velocity(77));
            }));

        world.SealSystems();
        world.Tick(0.016f);

        WorldProfileSnapshot profile = world.LastProfile;
        Assert.Equal((uint)0, profile.Tick);
        Assert.Equal(1, profile.PreSystems.CreateDefaultCount);
        Assert.Equal(0, profile.PreSystems.AddCount);
        Assert.Equal(1, profile.PostSystems.AddCount);
        Assert.Equal(0, profile.PostSystems.DestroyCount);
        Assert.True(world.HasComponent<Velocity>(target));
        Assert.Equal(new Velocity(77), world.GetComponent<Velocity>(target));
    }

    private static void World_Lifecycle_AutoInitializesSealsAndShutdownBlocksFurtherUse()
    {
        var world = new World(new EcsSchemaBuilder().Build());
        var system = new LifecycleTrackingSystem(new SystemDescriptor(
            "Lifecycle",
            SystemStage.AI,
            1,
            0,
            ComponentMask.Empty,
            ComponentMask.Empty));

        world.RegisterSystem(system);
        Assert.Equal(WorldLifecycleState.Created, world.State);

        world.Tick(0.016f);

        Assert.Equal(1, system.InitializeCalls);
        Assert.Equal(1, system.UpdateCalls);
        Assert.Equal(WorldLifecycleState.Running, world.State);
        Assert.True(world.IsSealed);
        Assert.True(world.IsInitialized);

        Assert.Throws<InvalidOperationException>(() => world.RegisterSystem(
            new MovementLoggingSystem(
                "Late",
                new SystemDescriptor("Late", SystemStage.AI, 1, 0, ComponentMask.Empty, ComponentMask.Empty),
                new List<string>())));

        world.Shutdown();

        Assert.Equal(1, system.ShutdownCalls);
        Assert.Equal(WorldLifecycleState.Shutdown, world.State);
        Assert.False(world.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));
        Assert.Throws<InvalidOperationException>(() => world.EnqueueDestroy(Entity.Null));
        Assert.Throws<InvalidOperationException>(() => world.SetComponent(Entity.Null, new Position(1)));
    }

    private static void World_SaveLoadAndSaveAllLoadAll_UsePersistenceAndSystemHooks()
    {
        var recorder = new List<string>();
        var services = new EcsWorldServices
        {
            PersistenceAdapter = new RecordingPersistenceAdapter(recorder),
        };

        var world = new World(new EcsSchemaBuilder().Build(), services);
        world.RegisterSystems(
            new SerializableSystem("First", recorder),
            new SerializableSystemTwo("Second", recorder));

        var writer = new TestDataWriter();
        var reader = new TestDataReader();

        world.SaveAll(writer);
        world.LoadAll(reader);
        world.Save(writer);
        world.Load(reader);

        Assert.SequenceEqual(
            [
                "Serialize:First",
                "Serialize:Second",
                "Deserialize:First",
                "Deserialize:Second",
                "Adapter:Save",
                "Adapter:Load",
            ],
            recorder);

        var noAdapterWorld = new World(new EcsSchemaBuilder().Build());
        Assert.Throws<InvalidOperationException>(() => noAdapterWorld.Save(writer));
        Assert.Throws<InvalidOperationException>(() => noAdapterWorld.Load(reader));
    }

    private static void WorldPersistenceView_CapturesFilteredReadOnlyArchetypeSet()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<Position>();
        ComponentId velocityId = builder.RegisterComponent<Velocity>();
        var world = new World(builder.Build());

        Entity positionOnly = world.CreateDefault(new ArchetypeSignature([positionId]));
        Entity positionAndVelocity = world.CreateDefault(new ArchetypeSignature([positionId, velocityId]));
        Entity velocityOnly = world.CreateDefault(new ArchetypeSignature([velocityId]));

        Assert.True(world.SetComponent(positionOnly, new Position(10)));
        Assert.True(world.SetComponent(positionAndVelocity, new Position(20)));
        Assert.True(world.SetComponent(positionAndVelocity, new Velocity(30)));
        Assert.True(world.SetComponent(velocityOnly, new Velocity(40)));

        world.Destroy(velocityOnly);

        var filtered = new WorldPersistenceView(world, [positionId]);

        Assert.Equal(2, filtered.ArchetypeCount);
        Assert.Equal(2, filtered.EntityCount);

        int totalPosition = 0;
        for (int i = 0; i < filtered.Archetypes.Count; i++)
        {
            PersistenceArchetypeView archetype = filtered.Archetypes[i];
            Assert.True(archetype.HasComponent(positionId));

            ReadOnlySpan<Position> positions = archetype.GetComponentSpan<Position>(positionId);
            for (int j = 0; j < positions.Length; j++)
                totalPosition += positions[j].X;
        }

        Assert.Equal(30, totalPosition);

        var withEmpty = new WorldPersistenceView(world, ReadOnlySpan<ComponentId>.Empty, excludeEmpty: false);
        Assert.Equal(3, withEmpty.ArchetypeCount);
        Assert.Equal(2, withEmpty.EntityCount);
    }

    private static World CreateSeededDualComponentWorld(EcsSchema schema, int entityCount, out Entity[] entities)
    {
        var world = new World(schema);
        ComponentId positionId = schema.GetComponentId<Position>();
        ComponentId velocityId = schema.GetComponentId<Velocity>();
        var signature = new ArchetypeSignature([positionId, velocityId]);
        entities = new Entity[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new Position(i));
            world.SetComponent(entity, new Velocity(i * 10));
            entities[i] = entity;
        }

        var drain = new Entity[Math.Max(1, entityCount)];
        world.DrainDirtyTo(drain);
        world.DrainDirtyTo<Position>(drain);
        world.DrainDirtyTo<Velocity>(drain);
        return world;
    }

    private static void RegisterExplicitWriteParitySystems(
        World world,
        Entity[] entities,
        ComponentId positionId,
        ComponentId velocityId)
    {
        world.RegisterSystems(
            new ContextActionSystem<ParallelExplicitPositionTag>(
                new SystemDescriptor(
                    "ParallelExplicitPosition",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.Empty,
                    ComponentMask.FromIds([positionId])),
                (in SystemContext ctx) =>
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ctx.World.PatchComponent<Position>(entities[i], static (ref Position position) =>
                        {
                            position = new Position(position.X + 3);
                        });

                        if ((i & 31) == 0)
                            Thread.Yield();
                    }
                }),
            new ContextActionSystem<ParallelExplicitVelocityTag>(
                new SystemDescriptor(
                    "ParallelExplicitVelocity",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.FromIds([velocityId]),
                    ComponentMask.FromIds([velocityId])),
                (in SystemContext ctx) =>
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Velocity velocity = ctx.World.GetComponent<Velocity>(entities[i]);
                        Assert.True(ctx.World.SetComponent(entities[i], new Velocity(velocity.X + 7)));

                        if ((i & 31) == 0)
                            Thread.Yield();
                    }
                }));
    }

    private static void RegisterDeterminismSoakSystems(World world, ComponentId positionId, ComponentId velocityId)
    {
        world.RegisterSystems(
            new ContextActionSystem<DeterminismPositionTag>(
                new SystemDescriptor(
                    "DeterminismPosition",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.FromIds([positionId, velocityId]),
                    ComponentMask.FromIds([positionId])),
                (in SystemContext ctx) =>
                {
                    ctx.World.PatchQuery<Position, Velocity, Position>((Entity _, in Position position, in Velocity velocity, ref Position write) =>
                    {
                        write = new Position(position.X + velocity.X + 1);
                    });
                }),
            new ContextActionSystem<DeterminismVelocityTag>(
                new SystemDescriptor(
                    "DeterminismVelocity",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.FromIds([positionId, velocityId]),
                    ComponentMask.FromIds([velocityId])),
                (in SystemContext ctx) =>
                {
                    int delta = (int)(ctx.Tick % 5) + 2;
                    ctx.World.PatchQuery<Position, Velocity, Velocity>((Entity _, in Position position, in Velocity velocity, ref Velocity write) =>
                    {
                        write = new Velocity(velocity.X + delta + (position.X & 1));
                    });
                }),
            new ContextActionSystem<DeterminismReadTag>(
                new SystemDescriptor(
                    "DeterminismRead",
                    SystemStage.AI,
                    1,
                    0,
                    ComponentMask.FromIds([positionId, velocityId]),
                    ComponentMask.Empty),
                (in SystemContext ctx) =>
                {
                    int checksum = 0;
                    ctx.World.Query<Position, Velocity>((Entity _, in Position position, in Velocity velocity) =>
                    {
                        checksum ^= position.X + velocity.X;
                    });

                    _ = checksum;
                }));
    }

    private static Entity? TryPickRandomLiveEntity(ReferenceWorldModel model, Random random)
    {
        IReadOnlyList<Entity> live = model.LiveEntities;
        if (live.Count == 0)
            return null;

        return live[random.Next(live.Count)];
    }

    private static void AssertWorldMatchesModel(World world, ReferenceWorldModel model, IReadOnlyList<Entity> entities)
    {
        Assert.Equal(model.LiveCount, world.Entities.AliveCount);

        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            if (!model.TryGet(entity, out ModelEntityState state))
            {
                Assert.False(world.Entities.IsAlive(entity));
                continue;
            }

            Assert.True(world.Entities.IsAlive(entity));
            Assert.True(world.TryGetComponent(entity, out Position position));
            Assert.Equal(new Position(state.PositionX), position);

            bool hasVelocity = world.TryGetComponent(entity, out Velocity velocity);
            Assert.Equal(state.HasVelocity, hasVelocity);
            if (state.HasVelocity)
                Assert.Equal(new Velocity(state.VelocityX), velocity);
        }
    }

    private static void AssertDirtySetsMatchAndClear(World world, ReferenceWorldModel model)
    {
        var allDirty = new Entity[Math.Max(1, model.KnownEntityCount + 8)];
        var positionDirty = new Entity[Math.Max(1, model.KnownEntityCount + 8)];
        var velocityDirty = new Entity[Math.Max(1, model.KnownEntityCount + 8)];

        AssertEntitySetsEqual(model.DirtyAll, world.DrainDirtyTo(allDirty), allDirty);
        AssertEntitySetsEqual(model.DirtyPosition, world.DrainDirtyTo<Position>(positionDirty), positionDirty);
        AssertEntitySetsEqual(model.DirtyVelocity, world.DrainDirtyTo<Velocity>(velocityDirty), velocityDirty);
        model.ClearDirty();
    }

    private static void AssertEntitySetsEqual(IReadOnlyCollection<Entity> expected, int actualCount, Entity[] actualBuffer)
    {
        var expectedOrdered = expected
            .Select(static entity => entity.Packed)
            .OrderBy(static packed => packed)
            .ToArray();

        ulong[] actualOrdered = actualBuffer
            .AsSpan(0, actualCount)
            .ToArray()
            .Select(static entity => entity.Packed)
            .OrderBy(static packed => packed)
            .ToArray();

        Assert.Equal(expectedOrdered.Length, actualOrdered.Length);
        for (int i = 0; i < expectedOrdered.Length; i++)
            Assert.Equal(expectedOrdered[i], actualOrdered[i]);
    }

    private static void AssertEntitySetsEqual(int leftCount, Entity[] leftBuffer, int rightCount, Entity[] rightBuffer)
    {
        ulong[] leftOrdered = leftBuffer
            .AsSpan(0, leftCount)
            .ToArray()
            .Select(static entity => entity.Packed)
            .OrderBy(static packed => packed)
            .ToArray();

        ulong[] rightOrdered = rightBuffer
            .AsSpan(0, rightCount)
            .ToArray()
            .Select(static entity => entity.Packed)
            .OrderBy(static packed => packed)
            .ToArray();

        Assert.Equal(leftOrdered.Length, rightOrdered.Length);
        for (int i = 0; i < leftOrdered.Length; i++)
            Assert.Equal(leftOrdered[i], rightOrdered[i]);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            Environment.ExitCode = 1;
            throw;
        }
    }

    private readonly record struct Position(int X) : IComponent<Position>
    {
    }

    private readonly record struct Velocity(int X) : IComponent<Velocity>
    {
    }

    private struct NotSelfComponent : IComponent<Position>
    {
    }

    private readonly record struct Acceleration(int X) : IComponent<Acceleration>
    {
    }

    private delegate void SystemContextAction(in SystemContext ctx);

    private sealed class PeriodOverrideSettings : SystemSettings
    {
        private readonly int _period;
        private readonly int _offset;
        private readonly SystemStage _stage;

        public PeriodOverrideSettings(int period, int offset, SystemStage stage)
        {
            _period = period;
            _offset = offset;
            _stage = stage;
        }

        public override void Apply(ref SystemSchedule schedule)
        {
            schedule.PeriodTicks = _period;
            schedule.StartOffset = _offset;
            schedule.Stage = _stage;
        }
    }

    private sealed class NoOpPrefabResolver : IEcsPrefabResolver
    {
    }

    private sealed class NoOpPersistenceAdapter : IEcsPersistenceAdapter
    {
        public void Save(World world, IDataWriter writer)
        {
        }

        public void Load(World world, IDataReader reader)
        {
        }
    }

    private sealed class RecordingPersistenceAdapter : IEcsPersistenceAdapter
    {
        private readonly List<string> _log;

        public RecordingPersistenceAdapter(List<string> log)
        {
            _log = log;
        }

        public void Save(World world, IDataWriter writer)
        {
            _log.Add("Adapter:Save");
        }

        public void Load(World world, IDataReader reader)
        {
            _log.Add("Adapter:Load");
        }
    }

    private sealed class TestDataWriter : IDataWriter
    {
    }

    private sealed class TestDataReader : IDataReader
    {
    }

    private abstract class LoggingSystem : SystemBase
    {
        private readonly string _label;
        private readonly List<string> _log;

        public LoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
        {
            _label = label;
            Descriptor = descriptor;
            _log = log;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            _log.Add($"{_label}@{ctx.Tick}");
        }
    }

    private sealed class MovementLoggingSystem : LoggingSystem
    {
        public MovementLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class MovementLoggingSystemTwo : LoggingSystem
    {
        public MovementLoggingSystemTwo(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class MovementLoggingSystemThree : LoggingSystem
    {
        public MovementLoggingSystemThree(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class AiLoggingSystem : LoggingSystem
    {
        public AiLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class AiLoggingSystemTwo : LoggingSystem
    {
        public AiLoggingSystemTwo(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class AiLoggingSystemThree : LoggingSystem
    {
        public AiLoggingSystemThree(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class ManualLoggingSystem : LoggingSystem
    {
        public ManualLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class WorldSimLoggingSystem : LoggingSystem
    {
        public WorldSimLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private static readonly Type DependencyLoggingSystemType = typeof(DependencyLoggingSystem);

    private sealed class DependencyLoggingSystem : LoggingSystem
    {
        public DependencyLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private sealed class DependentLoggingSystem : LoggingSystem
    {
        public DependentLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
        {
        }
    }

    private abstract class DescriptorOnlySystem : SystemBase
    {
        protected DescriptorOnlySystem(SystemDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
        }
    }

    private sealed class PositionWriterSystem : DescriptorOnlySystem
    {
        public PositionWriterSystem(SystemDescriptor descriptor) : base(descriptor)
        {
        }
    }

    private sealed class PositionReaderSystem : DescriptorOnlySystem
    {
        public PositionReaderSystem(SystemDescriptor descriptor) : base(descriptor)
        {
        }
    }

    private sealed class VelocityWriterSystem : DescriptorOnlySystem
    {
        public VelocityWriterSystem(SystemDescriptor descriptor) : base(descriptor)
        {
        }
    }

    private abstract class CountingSystemBase : SystemBase
    {
        private readonly Action _action;

        protected CountingSystemBase(SystemDescriptor descriptor, Action action)
        {
            Descriptor = descriptor;
            _action = action;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            _action();
        }
    }

    private sealed class CountingSystem : CountingSystemBase
    {
        public CountingSystem(SystemDescriptor descriptor, Action action) : base(descriptor, action)
        {
        }
    }

    private sealed class CountingSystemTwo : CountingSystemBase
    {
        public CountingSystemTwo(SystemDescriptor descriptor, Action action) : base(descriptor, action)
        {
        }
    }

    private sealed class ContextActionSystem<TTag> : SystemBase
    {
        private readonly SystemContextAction _action;

        public ContextActionSystem(SystemDescriptor descriptor, SystemContextAction action)
        {
            Descriptor = descriptor;
            _action = action;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            _action(in ctx);
        }
    }

    private sealed class ParallelChunkPositionSystem : SystemBase
    {
        private const int BlockSize = 128;

        public ParallelChunkPositionSystem(SystemDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.PatchChunksParallel<Velocity, Position>(BlockSize, (ReadOnlySpan<Entity> _, ReadOnlySpan<Velocity> velocities, Span<Position> writes) =>
            {
                for (int i = 0; i < writes.Length; i++)
                {
                    writes[i] = new Position(writes[i].X + velocities[i].X);
                }
            });
        }
    }

    private sealed class ParallelSparsePositionSystem : SystemBase
    {
        private const int BlockSize = 128;

        public ParallelSparsePositionSystem(SystemDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.PatchChunksParallelSparse<Velocity, Position>(BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<Velocity> velocities, Span<Position> writes, Span<int> dirtyIndices) =>
            {
                int dirtyCount = 0;
                for (int i = 0; i < writes.Length; i++)
                {
                    if (((int)entities[i].Index & 1) != 0)
                        continue;

                    writes[i] = new Position(writes[i].X + velocities[i].X);
                    dirtyIndices[dirtyCount++] = i;
                }

                return dirtyCount;
            });
        }
    }

    private sealed class ParallelStructuralPositionTag
    {
    }

    private sealed class ParallelStructuralVelocityTag
    {
    }

    private sealed class ParallelExplicitPositionTag
    {
    }

    private sealed class ParallelExplicitVelocityTag
    {
    }

    private sealed class DeterminismPositionTag
    {
    }

    private sealed class DeterminismVelocityTag
    {
    }

    private sealed class DeterminismReadTag
    {
    }

    private sealed class LifecycleTrackingSystem : SystemBase
    {
        public LifecycleTrackingSystem(SystemDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public int InitializeCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int ShutdownCalls { get; private set; }

        public override SystemDescriptor Descriptor { get; }

        public override void OnInitialize(World world)
        {
            InitializeCalls++;
        }

        public override void Update(in SystemContext ctx)
        {
            UpdateCalls++;
        }

        public override void OnShutdown(World world)
        {
            ShutdownCalls++;
        }
    }

    private abstract class SerializableSystemBase : SystemBase
    {
        private readonly string _label;
        private readonly List<string> _log;

        protected SerializableSystemBase(string label, List<string> log)
        {
            _label = label;
            _log = log;
        }

        public override SystemDescriptor Descriptor =>
            new(_label, SystemStage.AI, 1, 0, ComponentMask.Empty, ComponentMask.Empty);

        public override void Update(in SystemContext ctx)
        {
        }

        public override void Serialize(IDataWriter writer)
        {
            _log.Add($"Serialize:{_label}");
        }

        public override void Deserialize(IDataReader reader)
        {
            _log.Add($"Deserialize:{_label}");
        }
    }

    private sealed class SerializableSystem : SerializableSystemBase
    {
        public SerializableSystem(string label, List<string> log) : base(label, log)
        {
        }
    }

    private sealed class SerializableSystemTwo : SerializableSystemBase
    {
        public SerializableSystemTwo(string label, List<string> log) : base(label, log)
        {
        }
    }

    private sealed class ReferenceWorldModel
    {
        private readonly Dictionary<Entity, ModelEntityState> _entities = new();
        private readonly List<Entity> _liveEntities = new();
        private readonly List<(Entity Entity, int VelocityX)> _pendingVelocityAdds = new();
        private readonly List<Entity> _pendingVelocityRemoves = new();
        private readonly List<Entity> _pendingDestroys = new();

        public IReadOnlyList<Entity> LiveEntities => _liveEntities;
        public int LiveCount => _liveEntities.Count;
        public int KnownEntityCount => _entities.Count;
        public HashSet<Entity> DirtyAll { get; } = new();
        public HashSet<Entity> DirtyPosition { get; } = new();
        public HashSet<Entity> DirtyVelocity { get; } = new();

        public void Create(Entity entity, int positionX)
        {
            _entities[entity] = new ModelEntityState(positionX, HasVelocity: false, VelocityX: 0);
            _liveEntities.Add(entity);
            DirtyAll.Add(entity);
            DirtyPosition.Add(entity);
        }

        public bool TryGet(Entity entity, out ModelEntityState state)
        {
            if (_entities.TryGetValue(entity, out state))
                return true;

            state = default;
            return false;
        }

        public void SetPosition(Entity entity, int positionX)
        {
            if (!_entities.TryGetValue(entity, out ModelEntityState state))
                return;

            state = state with { PositionX = positionX };
            _entities[entity] = state;
            DirtyAll.Add(entity);
            DirtyPosition.Add(entity);
        }

        public void PatchPosition(Entity entity, int delta)
        {
            if (!_entities.TryGetValue(entity, out ModelEntityState state))
                return;

            state = state with { PositionX = state.PositionX + delta };
            _entities[entity] = state;
            DirtyAll.Add(entity);
            DirtyPosition.Add(entity);
        }

        public void EnqueueAddVelocity(Entity entity, int velocityX)
        {
            _pendingVelocityAdds.Add((entity, velocityX));
        }

        public void EnqueueRemoveVelocity(Entity entity)
        {
            _pendingVelocityRemoves.Add(entity);
        }

        public void EnqueueDestroy(Entity entity)
        {
            _pendingDestroys.Add(entity);
        }

        public void ProcessStructuralQueues()
        {
            for (int i = 0; i < _pendingDestroys.Count; i++)
            {
                Entity entity = _pendingDestroys[i];
                if (!_entities.Remove(entity))
                    continue;

                _liveEntities.Remove(entity);
                DirtyAll.Add(entity);
            }

            for (int i = 0; i < _pendingVelocityRemoves.Count; i++)
            {
                Entity entity = _pendingVelocityRemoves[i];
                if (!_entities.TryGetValue(entity, out ModelEntityState state) || !state.HasVelocity)
                    continue;

                state = state with { HasVelocity = false, VelocityX = 0 };
                _entities[entity] = state;
                DirtyAll.Add(entity);
                DirtyVelocity.Add(entity);
            }

            for (int i = 0; i < _pendingVelocityAdds.Count; i++)
            {
                (Entity entity, int velocityX) = _pendingVelocityAdds[i];
                if (!_entities.TryGetValue(entity, out ModelEntityState state))
                    continue;

                state = state with { HasVelocity = true, VelocityX = velocityX };
                _entities[entity] = state;
                DirtyAll.Add(entity);
                DirtyVelocity.Add(entity);
            }

            _pendingDestroys.Clear();
            _pendingVelocityRemoves.Clear();
            _pendingVelocityAdds.Clear();
        }

        public void ClearDirty()
        {
            DirtyAll.Clear();
            DirtyPosition.Clear();
            DirtyVelocity.Clear();
        }
    }
}

internal readonly record struct ModelEntityState(int PositionX, bool HasVelocity, int VelocityX);

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected condition to be true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected condition to be false.");
    }

    public static void Same(object expected, object actual)
    {
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException("Expected references to be identical.");
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"Expected sequence count {expected.Count}, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException($"Sequence mismatch at {i}: expected '{expected[i]}', got '{actual[i]}'.");
        }
    }
}
