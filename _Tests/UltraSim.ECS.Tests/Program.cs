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
        Run(nameof(EntityManager_TracksEntityLifecycleAcrossArchetypes), EntityManager_TracksEntityLifecycleAcrossArchetypes);
        Run(nameof(ComponentMask_IntersectionsAndSupersetsWork), ComponentMask_IntersectionsAndSupersetsWork);
        Run(nameof(SystemSchedule_FromDescriptorAndSettings_UsesNoPriorityField), SystemSchedule_FromDescriptorAndSettings_UsesNoPriorityField);
        Run(nameof(ExplicitSetComponent_MarksDirty), ExplicitSetComponent_MarksDirty);
        Run(nameof(PatchComponent_AndSetOrAddComponent_KeepWritesExplicitAndDirtyAware), PatchComponent_AndSetOrAddComponent_KeepWritesExplicitAndDirtyAware);
        Run(nameof(StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty), StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty);
        Run(nameof(StructuralQueue_BatchedAddRemoveAndDestroy_WorkAcrossMultipleEntities), StructuralQueue_BatchedAddRemoveAndDestroy_WorkAcrossMultipleEntities);
        Run(nameof(StructuralQueue_MixedOperations_UseDeterministicPhaseOrder), StructuralQueue_MixedOperations_UseDeterministicPhaseOrder);
        Run(nameof(QueryArchetypes_AndGetComponent_ExposeReadOnlyTraversal), QueryArchetypes_AndGetComponent_ExposeReadOnlyTraversal);
        Run(nameof(ReadOnlyQuery_AndChunkIteration_FilterByArchetype), ReadOnlyQuery_AndChunkIteration_FilterByArchetype);
        Run(nameof(PatchQuery_UpdatesMatchedRowsAndMarksDirty), PatchQuery_UpdatesMatchedRowsAndMarksDirty);
        Run(nameof(SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags), SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags);
        Run(nameof(SystemManager_RequiresOverridesRegistrationOrderWithinStage), SystemManager_RequiresOverridesRegistrationOrderWithinStage);
        Run(nameof(SystemManager_BatchesNonConflictingSystemsTogether), SystemManager_BatchesNonConflictingSystemsTogether);
        Run(nameof(SystemManager_PhaseStaggering_AssignsOffsetsOnlyToNonCriticalStages), SystemManager_PhaseStaggering_AssignsOffsetsOnlyToNonCriticalStages);
        Run(nameof(SystemManager_ParallelMode_RunsNonConflictingBatchMembers), SystemManager_ParallelMode_RunsNonConflictingBatchMembers);
        Run(nameof(SystemManager_ParallelMode_DeferredStructuralCommands_AreCollectedSafely), SystemManager_ParallelMode_DeferredStructuralCommands_AreCollectedSafely);
        Run(nameof(SystemManager_ParallelMode_ExplicitWrites_MatchSerialOutcomeForDisjointMasks), SystemManager_ParallelMode_ExplicitWrites_MatchSerialOutcomeForDisjointMasks);
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

        int tripleCount = world.Query<Position, Velocity, Acceleration>((Entity _, in Position _, in Velocity _, in Acceleration _) =>
        {
            throw new InvalidOperationException("No entity should match all three components.");
        });

        Assert.Equal(0, tripleCount);
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
}

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
