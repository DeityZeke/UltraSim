using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.ECS.Tests;

internal static class LifecycleAndPublicFilterTests
{
    private static int _passed;

    internal static void RunAll()
    {
        Run(nameof(Lifecycle_LayoutsCapacityAndImmediateTruthTableAreExact), Lifecycle_LayoutsCapacityAndImmediateTruthTableAreExact);
        Run(nameof(Lifecycle_CursorsPartialOverflowAndWrapAreExplicit), Lifecycle_CursorsPartialOverflowAndWrapAreExplicit);
        Run(nameof(Lifecycle_RandomizedRingMatchesReferenceAcrossWrap), Lifecycle_RandomizedRingMatchesReferenceAcrossWrap);
        Run(nameof(Lifecycle_ProductionAndScalarWholeSourceStreamsMatch), Lifecycle_ProductionAndScalarWholeSourceStreamsMatch);
        Run(nameof(Lifecycle_ProductionStrategiesMatchScalarStreams), Lifecycle_ProductionStrategiesMatchScalarStreams);
        Run(nameof(Lifecycle_NativeAndPinnedBackendsPublishEquivalentFacts), Lifecycle_NativeAndPinnedBackendsPublishEquivalentFacts);
        Run(nameof(Lifecycle_WarmedPublishAndReadAllocateZero), Lifecycle_WarmedPublishAndReadAllocateZero);
        Run(nameof(PublicFilter_RequiredExcludedTagsCompileIntoRoutes), PublicFilter_RequiredExcludedTagsCompileIntoRoutes);
        Run(nameof(PublicFilter_RejectsDefaultForeignInvalidAndContradictory), PublicFilter_RejectsDefaultForeignInvalidAndContradictory);
        Console.WriteLine($"Lifecycle/filter candidate tests passed: {_passed}.");
    }

    private static void Lifecycle_LayoutsCapacityAndImmediateTruthTableAreExact()
    {
        Assert.Equal(24, Unsafe.SizeOf<LifecycleRecord>());
        Assert.Equal(16, Unsafe.SizeOf<LifecycleCursor>());

        using var fixture = new Fixture(lifecycleCapacity: 32);
        LifecycleCursor cursor = fixture.World.CaptureLifecycleCursorFromBeginning();
        Assert.True(fixture.World.TryCreate(fixture.Source, out Entity entity));
        var value = new LayoutEnableLong { Value = 17 };
        Assert.True(fixture.World.TryWrite(entity, fixture.EnableLookup, value));
        Assert.True(fixture.World.TrySetEnabled(entity, fixture.EnableLookup, false));
        Assert.True(fixture.World.TryTransition(entity, fixture.SourceToDestination));
        Assert.True(fixture.World.TryTransition(entity, fixture.DestinationToSource));
        Assert.True(fixture.World.TryDestroy(entity));
        Assert.True(fixture.World.TryCreate(fixture.Source, out Entity replacement));

        Span<LifecycleRecord> records = stackalloc LifecycleRecord[8];
        LifecycleReadResult result = fixture.World.ReadLifecycle(ref cursor, records);
        Assert.Equal(LifecycleReadStatus.Success, result.Status);
        Assert.Equal(7, result.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
        Assert.Equal(entity, records[0].Entity);
        Assert.Equal(ComponentId.Invalid, records[0].ComponentId);

        ComponentId intId = LayoutTestSchema.GetComponentId<LayoutInt>();
        ComponentId tagId = LayoutTestSchema.GetComponentId<LayoutTag>();
        ComponentId firstAdded = intId.CompareTo(tagId) < 0 ? intId : tagId;
        ComponentId secondAdded = intId.CompareTo(tagId) < 0 ? tagId : intId;
        Assert.Equal(LifecycleKind.ComponentAdded, records[1].Kind);
        Assert.Equal(firstAdded, records[1].ComponentId);
        Assert.Equal(LifecycleKind.ComponentAdded, records[2].Kind);
        Assert.Equal(secondAdded, records[2].ComponentId);
        Assert.Equal(LifecycleKind.ComponentRemoved, records[3].Kind);
        Assert.Equal(firstAdded, records[3].ComponentId);
        Assert.Equal(LifecycleKind.ComponentRemoved, records[4].Kind);
        Assert.Equal(secondAdded, records[4].ComponentId);
        Assert.Equal(LifecycleKind.Destroyed, records[5].Kind);
        Assert.Equal(entity, records[5].Entity);
        Assert.Equal(LifecycleKind.Created, records[6].Kind);
        Assert.Equal(replacement.Index, records[6].Entity.Index);
        Assert.NotEqual(entity.Generation, records[6].Entity.Generation);
        Assert.Equal(0UL, result.RemainingCount);
        Assert.Equal(LifecycleReadStatus.NoData, fixture.World.ReadLifecycle(ref cursor, records).Status);

        WorldReservedPayloadSnapshot memory = fixture.World.GetReservedPayloadSnapshot();
        Assert.Equal(32L * Unsafe.SizeOf<LifecycleRecord>(), memory.LifecycleHistoryBytes);
        Assert.Equal(memory.KnownManagedPayloadBytes + memory.NativeChunkPayloadBytes, memory.TotalKnownPayloadBytes);
        var plannedOptions = new EcsWorldOptions(
            new EntityCapacity(16), 1, 0, lifecycleRecordCapacity: 32);
        WorldCapacityPlan planned = WorldCapacityPlanner.PlanFromOptions(
            plannedOptions, ReadOnlySpan<ArchetypeCapacityPlan>.Empty);
        Assert.Equal(memory.LifecycleHistoryBytes, planned.ManagedLifecycleBytes);
        Assert.Equal(
            planned.ManagedEntityMetadataBytes + planned.ManagedSchedulerBytes +
            planned.ManagedLifecycleBytes,
            planned.TotalManagedBytes);

        using var disabled = new Fixture(lifecycleCapacity: 0);
        Assert.Equal(0L, disabled.World.GetReservedPayloadSnapshot().LifecycleHistoryBytes);
        Assert.Equal(0, disabled.World.GetLifecycleHistorySnapshot().Capacity);
    }

    private static void Lifecycle_CursorsPartialOverflowAndWrapAreExplicit()
    {
        using (var fixture = new Fixture(lifecycleCapacity: 4))
        {
            LifecycleCursor first = fixture.World.CaptureLifecycleCursorFromBeginning();
            LifecycleCursor second = first;
            Assert.True(fixture.World.TryCreate(fixture.Source, out _));
            Assert.True(fixture.World.TryCreate(fixture.Source, out _));
            Assert.True(fixture.World.TryCreate(fixture.Source, out _));

            Span<LifecycleRecord> one = stackalloc LifecycleRecord[1];
            LifecycleReadResult partial = fixture.World.ReadLifecycle(ref first, one);
            Assert.Equal(LifecycleReadStatus.Success, partial.Status);
            Assert.Equal(1, partial.Count);
            Assert.Equal(2UL, partial.RemainingCount);
            Assert.Equal(1UL, first.NextSequence);
            var rest = new LifecycleRecord[4];
            Assert.Equal(2, fixture.World.ReadLifecycle(ref first, rest).Count);
            Assert.Equal(3, fixture.World.ReadLifecycle(ref second, rest).Count);

            LifecycleCursor lagging = fixture.World.CaptureLifecycleCursorFromBeginning();
            Assert.True(fixture.World.TryCreate(fixture.Source, out _));
            Assert.True(fixture.World.TryDestroy(rest[0].Entity));
            ulong unchanged = lagging.NextSequence;
            LifecycleReadResult overflow = fixture.World.ReadLifecycle(ref lagging, rest);
            Assert.Equal(LifecycleReadStatus.ResyncRequired, overflow.Status);
            Assert.Equal(unchanged, lagging.NextSequence);
            Assert.True(fixture.World.GetLifecycleHistorySnapshot().OverwrittenCount > 0);
            LifecycleCursor recovered = fixture.World.CaptureLifecycleCursorFromNow();
            Assert.True(fixture.World.TryCreate(fixture.Source, out _));
            LifecycleReadResult recoveredRead = fixture.World.ReadLifecycle(ref recovered, rest);
            Assert.Equal(LifecycleReadStatus.Success, recoveredRead.Status);
            Assert.Equal(1, recoveredRead.Count);

            using var foreign = new Fixture(lifecycleCapacity: 1);
            LifecycleCursor foreignCursor = foreign.World.CaptureLifecycleCursorFromNow();
            Assert.Throws<ArgumentException>(() => fixture.World.ReadLifecycle(ref foreignCursor, rest));
            LifecycleCursor invalid = default;
            Assert.Throws<ArgumentException>(() => fixture.World.ReadLifecycle(ref invalid, rest));
        }

        using (var wrap = new Fixture(lifecycleCapacity: 4, beginRun: false))
        {
            wrap.World.SetLifecycleNextSequenceForTest(ulong.MaxValue - 1);
            wrap.World.BeginRun();
            LifecycleCursor cursor = wrap.World.CaptureLifecycleCursorFromNow();
            Assert.True(wrap.World.TryCreate(wrap.Source, out Entity entity));
            Assert.True(wrap.World.TryDestroy(entity));
            Assert.True(wrap.World.TryCreate(wrap.Source, out _));
            Span<LifecycleRecord> records = stackalloc LifecycleRecord[3];
            Assert.Equal(3, wrap.World.ReadLifecycle(ref cursor, records).Count);
            Assert.Equal(ulong.MaxValue - 1, records[0].Sequence);
            Assert.Equal(ulong.MaxValue, records[1].Sequence);
            Assert.Equal(0UL, records[2].Sequence);
            Assert.Equal(1UL, cursor.NextSequence);
        }
    }

    private static void Lifecycle_ProductionAndScalarWholeSourceStreamsMatch()
    {
        const int count = 300;
        using var production = new Fixture(count + 2, count + 8, lifecycleCapacity: count * 3);
        using var scalar = new Fixture(count + 2, count + 8, lifecycleCapacity: count * 3);
        var productionEntities = new Entity[count];
        var scalarEntities = new Entity[count];
        for (int index = 0; index < count; index++)
        {
            Assert.True(production.World.TryCreate(production.Source, out productionEntities[index]));
            Assert.True(scalar.World.TryCreate(scalar.Source, out scalarEntities[index]));
        }
        LifecycleCursor productionCursor = production.World.CaptureLifecycleCursorFromNow();
        LifecycleCursor scalarCursor = scalar.World.CaptureLifecycleCursorFromNow();
        production.World.StructuralCommands.EnqueueTransition(productionEntities, production.SourceToDestination);
        scalar.World.StructuralCommands.EnqueueTransition(scalarEntities, scalar.SourceToDestination);
        production.World.PlaybackStructuralCommands();
        scalar.World.PlaybackStructuralCommandsScalarOracle();

        var productionRecords = new LifecycleRecord[count * 2];
        var scalarRecords = new LifecycleRecord[count * 2];
        LifecycleReadResult productionRead = production.World.ReadLifecycle(ref productionCursor, productionRecords);
        LifecycleReadResult scalarRead = scalar.World.ReadLifecycle(ref scalarCursor, scalarRecords);
        Assert.Equal(count * 2, productionRead.Count);
        Assert.Equal(productionRead.Count, scalarRead.Count);
        for (int index = 0; index < productionRead.Count; index++)
        {
            Assert.Equal(productionRecords[index].Kind, scalarRecords[index].Kind);
            Assert.Equal(productionRecords[index].ComponentId, scalarRecords[index].ComponentId);
            Assert.Equal(productionRecords[index].Entity.Index, scalarRecords[index].Entity.Index);
            Assert.Equal(productionRecords[index].Entity.Generation, scalarRecords[index].Entity.Generation);
        }
    }

    private static void Lifecycle_RandomizedRingMatchesReferenceAcrossWrap()
    {
        for (int seed = 0; seed < 32; seed++)
        {
            var random = new Random(unchecked((int)0x71FE_0000) + seed);
            int capacity = random.Next(1, 17);
            var history = new LifecycleHistory(capacity);
            WorldId worldId = WorldId.Create();
            ulong next = (seed & 1) == 0
                ? unchecked(ulong.MaxValue - (ulong)random.Next(0, 32))
                : unchecked((ulong)random.Next(0, 32));
            history.SetNextSequenceForTest(next);
            LifecycleCursor[] cursors =
            [
                history.CaptureFromBeginning(worldId),
                history.CaptureFromNow(worldId),
                history.CaptureFromBeginning(worldId),
            ];
            var retained = new List<LifecycleRecord>(capacity);
            var destination = new LifecycleRecord[8];

            for (int operation = 0; operation < 2_000; operation++)
            {
                if (random.Next(100) < 68)
                {
                    LifecycleKind kind = (LifecycleKind)(1 + random.Next(4));
                    ComponentId component = kind is LifecycleKind.ComponentAdded or LifecycleKind.ComponentRemoved
                        ? new ComponentId(unchecked((ushort)(random.Next(32) + 1)))
                        : ComponentId.Invalid;
                    Entity entity = new(
                        worldId,
                        unchecked((uint)random.Next(64)),
                        unchecked((uint)random.Next(1, 16)));
                    history.Publish(kind, entity, component);
                    if (retained.Count == capacity)
                        retained.RemoveAt(0);
                    retained.Add(new LifecycleRecord(next, kind, entity, component));
                    next = unchecked(next + 1);
                    continue;
                }

                int cursorIndex = random.Next(cursors.Length);
                LifecycleCursor cursor = cursors[cursorIndex];
                int spanLength = random.Next(destination.Length + 1);
                ulong oldest = unchecked(next - unchecked((ulong)retained.Count));
                ulong pending = unchecked(next - cursor.NextSequence);
                LifecycleReadStatus expectedStatus = pending == 0
                    ? LifecycleReadStatus.NoData
                    : pending > unchecked((ulong)retained.Count)
                        ? LifecycleReadStatus.ResyncRequired
                        : LifecycleReadStatus.Success;
                int expectedCount = expectedStatus == LifecycleReadStatus.Success
                    ? Math.Min(spanLength, checked((int)pending))
                    : 0;
                ulong originalCursor = cursor.NextSequence;
                LifecycleReadResult read = history.Read(ref cursor, destination.AsSpan(0, spanLength));
                Assert.Equal(expectedStatus, read.Status);
                Assert.Equal(expectedCount, read.Count);
                Assert.Equal(oldest, read.OldestRetainedSequence);
                Assert.Equal(next, read.NextPublishedSequence);
                if (expectedStatus == LifecycleReadStatus.Success)
                {
                    int offset = checked((int)unchecked(originalCursor - oldest));
                    for (int index = 0; index < expectedCount; index++)
                        Assert.Equal(retained[offset + index], destination[index]);
                    Assert.Equal(unchecked(originalCursor + unchecked((ulong)expectedCount)), cursor.NextSequence);
                    Assert.Equal(pending - unchecked((ulong)expectedCount), read.RemainingCount);
                }
                else
                {
                    Assert.Equal(originalCursor, cursor.NextSequence);
                }
                cursors[cursorIndex] = cursor;
                if (read.Status == LifecycleReadStatus.ResyncRequired && random.Next(2) == 0)
                    cursors[cursorIndex] = history.CaptureFromBeginning(worldId);
            }
        }
    }

    private static void Lifecycle_ProductionStrategiesMatchScalarStreams()
    {
        CompareStrategyStreams(600, static (fixture, entities) =>
            fixture.World.StructuralCommands.EnqueueTransition(
                entities.AsSpan(300, 300), fixture.SourceToDestination));
        CompareStrategyStreams(80, static (fixture, entities) =>
            fixture.World.StructuralCommands.EnqueueTransition(
                entities.AsSpan(0, 32), fixture.SourceToDestination));
        CompareStrategyStreams(80, static (fixture, entities) =>
        {
            Span<Entity> scattered = stackalloc Entity[20];
            for (int index = 0; index < scattered.Length; index++)
                scattered[index] = entities[index * 2];
            fixture.World.StructuralCommands.EnqueueTransition(
                scattered, fixture.SourceToDestination);
        });
        CompareStrategyStreams(300, static (fixture, entities) =>
            fixture.World.StructuralCommands.EnqueueDestroy(entities));
        CompareStrategyStreams(600, static (fixture, entities) =>
            fixture.World.StructuralCommands.EnqueueDestroy(entities.AsSpan(300, 300)));
        CompareStrategyStreams(128, static (fixture, entities) =>
        {
            Span<Entity> destroyed = stackalloc Entity[32];
            for (int index = 0; index < destroyed.Length; index++)
                destroyed[index] = entities[index * 2];
            fixture.World.StructuralCommands.EnqueueDestroy(destroyed);
            Span<ArchetypeHandle> archetypes = stackalloc ArchetypeHandle[32];
            Span<DeferredEntity> tokens = stackalloc DeferredEntity[32];
            archetypes.Fill(fixture.Source);
            fixture.World.StructuralCommands.EnqueueCreateDefault(archetypes, tokens);
        });
        CompareStrategyStreams(32, static (fixture, entities) =>
            fixture.World.StructuralCommands.EnqueueDestroy(entities.AsSpan(0, 1)));
    }

    private static void CompareStrategyStreams(
        int population,
        Action<Fixture, Entity[]> record)
    {
        const int lifecycleCapacity = 4096;
        using var production = new Fixture(
            population + 64, population + 64, lifecycleCapacity);
        using var scalar = new Fixture(
            population + 64, population + 64, lifecycleCapacity);
        var productionEntities = new Entity[population];
        var scalarEntities = new Entity[population];
        for (int index = 0; index < population; index++)
        {
            Assert.True(production.World.TryCreate(production.Source, out productionEntities[index]));
            Assert.True(scalar.World.TryCreate(scalar.Source, out scalarEntities[index]));
        }

        LifecycleCursor productionCursor = production.World.CaptureLifecycleCursorFromNow();
        LifecycleCursor scalarCursor = scalar.World.CaptureLifecycleCursorFromNow();
        record(production, productionEntities);
        record(scalar, scalarEntities);
        production.World.PlaybackStructuralCommands();
        scalar.World.PlaybackStructuralCommandsScalarOracle();

        var productionRecords = new LifecycleRecord[lifecycleCapacity];
        var scalarRecords = new LifecycleRecord[lifecycleCapacity];
        LifecycleReadResult productionRead = production.World.ReadLifecycle(
            ref productionCursor, productionRecords);
        LifecycleReadResult scalarRead = scalar.World.ReadLifecycle(
            ref scalarCursor, scalarRecords);
        Assert.Equal(productionRead.Status, scalarRead.Status);
        Assert.Equal(productionRead.Count, scalarRead.Count);
        for (int index = 0; index < productionRead.Count; index++)
        {
            Assert.Equal(productionRecords[index].Kind, scalarRecords[index].Kind);
            Assert.Equal(productionRecords[index].ComponentId, scalarRecords[index].ComponentId);
            Assert.Equal(productionRecords[index].Entity.Index, scalarRecords[index].Entity.Index);
            Assert.Equal(productionRecords[index].Entity.Generation, scalarRecords[index].Entity.Generation);
        }
    }

    private static void Lifecycle_WarmedPublishAndReadAllocateZero()
    {
        using var fixture = new Fixture(2, lifecycleCapacity: 8);
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[4];
        Assert.True(fixture.World.TryCreate(fixture.Source, out Entity warm));
        Assert.True(fixture.World.TryDestroy(warm));
        LifecycleCursor warmCursor = fixture.World.CaptureLifecycleCursorFromBeginning();
        _ = fixture.World.ReadLifecycle(ref warmCursor, records);

        LifecycleCursor cursor = fixture.World.CaptureLifecycleCursorFromNow();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(fixture.World.TryCreate(fixture.Source, out Entity entity));
        Assert.True(fixture.World.TryDestroy(entity));
        LifecycleReadResult result = fixture.World.ReadLifecycle(ref cursor, records);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2, result.Count);
        Assert.Equal(0L, allocated);
    }

    private static void Lifecycle_NativeAndPinnedBackendsPublishEquivalentFacts()
    {
        LifecycleRecord[] native = RunBackendLifecycle<NativeChunkArena>();
        LifecycleRecord[] pinned = RunBackendLifecycle<PinnedChunkArena>();
        Assert.Equal(native.Length, pinned.Length);
        for (int index = 0; index < native.Length; index++)
        {
            Assert.Equal(native[index].Sequence, pinned[index].Sequence);
            Assert.Equal(native[index].Kind, pinned[index].Kind);
            Assert.Equal(native[index].ComponentId, pinned[index].ComponentId);
            Assert.Equal(native[index].Entity.Index, pinned[index].Entity.Index);
            Assert.Equal(native[index].Entity.Generation, pinned[index].Entity.Generation);
        }
    }

    private static LifecycleRecord[] RunBackendLifecycle<TArena>()
        where TArena : class, IChunkArenaBackend
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        using var world = new EcsWorld<TArena>(
            schema,
            new EntityCapacity(8),
            maximumArchetypes: 2,
            static capacity => CreateArena<TArena>(capacity),
            maximumQueryPlans: 0,
            maximumPublicTransitions: 1,
            lifecycleRecordCapacity: 16);
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId enable = LayoutTestSchema.GetComponentId<LayoutEnableLong>();
        ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
        ArchetypeId source = world.RegisterArchetype(
            Sorted(wide, enable), ChunkLayoutOptions.KiB16, 1);
        ArchetypeId destination = world.RegisterArchetype(
            Sorted(wide, enable, tag), ChunkLayoutOptions.KiB16, 1);
        ArchetypeTransitionPlan<TArena> transition = world.PrepareTransition(source, destination);
        world.Seal();
        world.Initialize();
        world.BeginRun();
        LifecycleCursor cursor = world.CaptureLifecycleCursorFromBeginning();
        Assert.True(world.TryCreateDefault(source, out Entity entity));
        Assert.True(world.TryTransition(entity, transition, out _));
        Assert.True(world.TryDestroy(entity));
        var records = new LifecycleRecord[3];
        LifecycleReadResult read = world.ReadLifecycle(ref cursor, records);
        Assert.Equal(LifecycleReadStatus.Success, read.Status);
        Assert.Equal(3, read.Count);
        return records;
    }

    private static TRequestedArena CreateArena<TRequestedArena>(ChunkArenaCapacity capacity)
        where TRequestedArena : class, IChunkArenaBackend
    {
        object arena = typeof(TRequestedArena) == typeof(NativeChunkArena)
            ? new NativeChunkArena(capacity)
            : typeof(TRequestedArena) == typeof(PinnedChunkArena)
                ? new PinnedChunkArena(capacity)
                : throw new InvalidOperationException(
                    $"Unsupported lifecycle test arena {typeof(TRequestedArena).Name}.");
        return (TRequestedArena)arena;
    }

    private static void PublicFilter_RequiredExcludedTagsCompileIntoRoutes()
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        var options = new EcsWorldOptions(new EntityCapacity(32), 3, 8);
        using var world = new EcsWorld(schema, options);
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId enable = LayoutTestSchema.GetComponentId<LayoutEnableLong>();
        ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
        ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
        ArchetypeHandle plain = world.RegisterArchetype(Sorted(wide, enable), ChunkLayoutOptions.KiB16, 1);
        ArchetypeHandle tagged = world.RegisterArchetype(Sorted(wide, enable, tag), ChunkLayoutOptions.KiB16, 1);
        ArchetypeHandle taggedExcluded = world.RegisterArchetype(Sorted(wide, enable, tag, integer), ChunkLayoutOptions.KiB16, 1);
        EcsQueryFilter filter = world.PrepareQueryFilter([tag, tag], [integer]);
        ReadWriteJobPlan<LayoutWide, LayoutEnableLong> plan =
            world.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(filter);
        ReadWriteJobPlan<LayoutWide, LayoutEnableLong> sameFilterPlan =
            world.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(filter);
        world.Seal();
        world.Initialize();
        world.BeginRun();
        Assert.True(world.TryCreate(plain, out _));
        Assert.True(world.TryCreate(tagged, out _));
        Assert.True(world.TryCreate(tagged, out _));
        Assert.True(world.TryCreate(taggedExcluded, out _));
        var job = new FilterCountJob();
        Assert.Equal(2, world.ExecuteJob(plan, ref job));
        Assert.Equal(2, job.Rows);
        var second = new FilterCountJob();
        Assert.Equal(2, world.ExecuteJob(sameFilterPlan, ref second));
    }

    private static void PublicFilter_RejectsDefaultForeignInvalidAndContradictory()
    {
        ComponentSchema schema = LayoutTestSchema.CreateSchema();
        ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
        ComponentId enable = LayoutTestSchema.GetComponentId<LayoutEnableLong>();
        using var first = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(4), 1, 4));
        using var second = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(4), 1, 4));
        first.RegisterArchetype(Sorted(wide, enable), ChunkLayoutOptions.KiB16, 1);
        second.RegisterArchetype(Sorted(wide, enable), ChunkLayoutOptions.KiB16, 1);
        EcsQueryFilter foreign = first.PrepareQueryFilter([wide], []);
        Assert.Throws<ArgumentException>(() => second.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(foreign));
        EcsQueryFilter empty = second.PrepareQueryFilter([], []);
        Assert.True(empty.IsValid);
        _ = second.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(empty);
        EcsQueryFilter invalid = default;
        Assert.Throws<ArgumentException>(() => second.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(invalid));
        Assert.Throws<ArgumentException>(() => second.PrepareQueryFilter([wide], [wide]));
        EcsQueryFilter excludesDataColumn = second.PrepareQueryFilter([], [wide]);
        Assert.Throws<ArgumentException>(() =>
            second.PrepareReadWriteJob<LayoutWide, LayoutEnableLong>(excludesDataColumn));
        Assert.Throws<ArgumentOutOfRangeException>(() => second.PrepareQueryFilter([ComponentId.Invalid], []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            second.PrepareQueryFilter([new ComponentId(500)], []));
    }

    private static ComponentId[] Sorted(params ComponentId[] components)
    {
        Array.Sort(components);
        return components;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(
            int entityCapacity = 16,
            int structuralCapacity = 32,
            int lifecycleCapacity = 32,
            bool beginRun = true)
        {
            World = new EcsWorld(
                LayoutTestSchema.CreateSchema(),
                new EcsWorldOptions(
                    new EntityCapacity(entityCapacity),
                    maximumArchetypes: 2,
                    maximumPreparedPlans: 4,
                    maximumPreparedTransitions: 2,
                    maximumStructuralCommands: structuralCapacity,
                    lifecycleRecordCapacity: lifecycleCapacity));
            ComponentId wide = LayoutTestSchema.GetComponentId<LayoutWide>();
            ComponentId enable = LayoutTestSchema.GetComponentId<LayoutEnableLong>();
            ComponentId integer = LayoutTestSchema.GetComponentId<LayoutInt>();
            ComponentId tag = LayoutTestSchema.GetComponentId<LayoutTag>();
            Source = World.RegisterArchetype(Sorted(wide, enable), ChunkLayoutOptions.KiB16, 4);
            Destination = World.RegisterArchetype(Sorted(wide, enable, integer, tag), ChunkLayoutOptions.KiB16, 4);
            SourceToDestination = World.PrepareTransition(Source, Destination);
            DestinationToSource = World.PrepareTransition(Destination, Source);
            EnableLookup = World.PrepareLookup<LayoutEnableLong>();
            World.Seal();
            World.Initialize();
            if (beginRun)
                World.BeginRun();
        }

        internal EcsWorld World { get; }
        internal ArchetypeHandle Source { get; }
        internal ArchetypeHandle Destination { get; }
        internal ArchetypeTransition SourceToDestination { get; }
        internal ArchetypeTransition DestinationToSource { get; }
        internal ComponentLookup<LayoutEnableLong> EnableLookup { get; }

        public void Dispose() => World.Dispose();
    }

    private struct FilterCountJob : IReadWriteChunkJob<LayoutWide, LayoutEnableLong>
    {
        internal int Rows;

        public void Execute(in ReadWriteComponentChunk<LayoutWide, LayoutEnableLong> chunk) =>
            Rows += chunk.Count;
    }
}
