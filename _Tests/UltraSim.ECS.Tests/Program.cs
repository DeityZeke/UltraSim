using UltraSim.ECS;

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
        Run(nameof(StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty), StructuralQueue_AddRemoveDestroy_PreservesDataAndTracksDirty);
        Run(nameof(SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags), SystemManager_Tick_RespectsStageOrderDueNessAndManualFlags);

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

    private struct Acceleration : IComponent<Acceleration>
    {
    }

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

    private sealed class ManualLoggingSystem : LoggingSystem
    {
        public ManualLoggingSystem(string label, SystemDescriptor descriptor, List<string> log)
            : base(label, descriptor, log)
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
