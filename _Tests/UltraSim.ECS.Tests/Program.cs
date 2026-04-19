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

    private struct Position : IComponent<Position>
    {
    }

    private struct Velocity : IComponent<Velocity>
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
}
