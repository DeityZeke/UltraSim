using System.Runtime.CompilerServices;
using UltraSim.ECS;

internal static class ContentAuthoringTests
{
    private static int _passed;

    internal static void RunAll()
    {
        Run(nameof(Content_LayoutsKeysAndCatalogAccountingAreExplicit), Content_LayoutsKeysAndCatalogAccountingAreExplicit);
        Run(nameof(Content_InheritanceDefaultsAndSignatureDeduplicationAreExact), Content_InheritanceDefaultsAndSignatureDeduplicationAreExact);
        Run(nameof(Content_DirectAndClassAuthoringConverge), Content_DirectAndClassAuthoringConverge);
        Run(nameof(Content_ManualAndCompiledInitializationConverge), Content_ManualAndCompiledInitializationConverge);
        Run(nameof(Content_CompilationIsRegistrationOrderDeterministic), Content_CompilationIsRegistrationOrderDeterministic);
        Run(nameof(Content_InvalidDefinitionsFailCold), Content_InvalidDefinitionsFailCold);
        Run(nameof(Content_ImmediateInstantiationIsAtomicIndependentAndLifecycleExact), Content_ImmediateInstantiationIsAtomicIndependentAndLifecycleExact);
        Run(nameof(Content_RowReuseRestoresCompiledDefaults), Content_RowReuseRestoresCompiledDefaults);
        Run(nameof(Content_DeferredInstantiationUsesOneCommandAndResolves), Content_DeferredInstantiationUsesOneCommandAndResolves);
        Run(nameof(Content_SchedulerInstantiationUsesOneCreateAndSameSegmentReplacement), Content_SchedulerInstantiationUsesOneCreateAndSameSegmentReplacement);
        Run(nameof(Content_InitialDefaultsDoNotMasqueradeAsValueWrites), Content_InitialDefaultsDoNotMasqueradeAsValueWrites);
        Run(nameof(Content_LaterMembershipAndDestroyLifecycleRemainOrdinary), Content_LaterMembershipAndDestroyLifecycleRemainOrdinary);
        Run(nameof(Content_CapacityAndForeignHandlesRejectWithoutFalseLifecycle), Content_CapacityAndForeignHandlesRejectWithoutFalseLifecycle);
        Run(nameof(Content_NativePinnedInstantiationMatches), Content_NativePinnedInstantiationMatches);
        Run(nameof(Content_WarmedLookupAndImmediateDeferredInstantiationAllocateZero), Content_WarmedLookupAndImmediateDeferredInstantiationAllocateZero);
        Console.WriteLine($"Content-authoring candidate tests passed: {_passed}.");
    }

    private static void Content_LayoutsKeysAndCatalogAccountingAreExplicit()
    {
        Assert.Equal(12, Unsafe.SizeOf<PrefabHandle>());
        Assert.Equal(32, Unsafe.SizeOf<CompiledPrefabEntry>());
        Assert.Equal(12, Unsafe.SizeOf<PrefabDefaultDescriptor>());
        Assert.True(new PrefabKey("actors/villager").IsValid);
        Assert.Throws<ArgumentException>(() => new PrefabKey("Actors/Villager"));
        Assert.Throws<ArgumentException>(() => new PrefabKey("actors//villager"));
        using ContentFixture fixture = ContentFixture.Create();
        CompiledContentSnapshot snapshot = fixture.Catalog.GetSnapshot();
        Assert.Equal(11, snapshot.PrefabCount);
        Assert.Equal(7, snapshot.UniqueSignatureCount);
        Assert.Equal(snapshot.PrefabCount * Unsafe.SizeOf<CompiledPrefabEntry>(), snapshot.EntryBytes);
        Assert.True(snapshot.DescriptorBytes > 0 && snapshot.DefaultBytes > 0 && snapshot.StableKeyBytes > 0);
        Assert.Equal(
            snapshot.EntryBytes + snapshot.DescriptorBytes + snapshot.DefaultBytes +
            snapshot.StableKeyBytes + snapshot.DisplayMetadataBytes,
            snapshot.TotalKnownPayloadBytes);
        Assert.Equal(snapshot.TotalKnownPayloadBytes,
            fixture.World.GetReservedPayloadSnapshot().CompiledContentBytes);
    }

    private static void Content_InheritanceDefaultsAndSignatureDeduplicationAreExact()
    {
        using ContentFixture fixture = ContentFixture.Create();
        PrefabHandle villager = fixture.Handle("actors/villager");
        PrefabHandle merchant = fixture.Handle("actors/merchant");
        PrefabHandle guard = fixture.Handle("actors/guard");
        PrefabHandle blacksmith = fixture.Handle("actors/blacksmith");
        Assert.Equal(villager.TargetArchetype, merchant.TargetArchetype);
        Assert.Equal(villager.TargetArchetype, guard.TargetArchetype);
        Assert.Equal(villager.TargetArchetype, blacksmith.TargetArchetype);
        Assert.NotEqual(villager, merchant);
        Assert.NotEqual(villager.TargetArchetype, fixture.Handle("creatures/wolf").TargetArchetype);
        Assert.NotEqual(fixture.Handle("items/iron-sword").TargetArchetype,
            fixture.Handle("items/bread").TargetArchetype);

        Entity villagerEntity = fixture.World.Instantiate(villager);
        Entity merchantEntity = fixture.World.Instantiate(merchant);
        Entity guardEntity = fixture.World.Instantiate(guard);
        AssertValue(fixture.World, villagerEntity, fixture.Health, 80);
        AssertValue(fixture.World, merchantEntity, fixture.Health, 90);
        AssertValue(fixture.World, guardEntity, fixture.Health, 160);
        AssertValue(fixture.World, merchantEntity, fixture.Wealth, 500);
        Assert.True(fixture.World.Has(guardEntity, fixture.HumanTag));
    }

    private static void Content_DirectAndClassAuthoringConverge()
    {
        using ContentFixture fixture = ContentFixture.Create();
        PrefabHandle classPrefab = fixture.Handle("actors/villager");
        PrefabHandle directPrefab = fixture.Handle("direct/villager");
        Assert.Equal(classPrefab.TargetArchetype, directPrefab.TargetArchetype);
        Entity left = fixture.World.Instantiate(classPrefab);
        Entity right = fixture.World.Instantiate(directPrefab);
        AssertEquivalentActor(fixture, left, right);
        Assert.Equal(
            fixture.Catalog.GetInfo(classPrefab).DefaultByteCount,
            fixture.Catalog.GetInfo(directPrefab).DefaultByteCount);
    }

    private static void Content_ManualAndCompiledInitializationConverge()
    {
        using ContentFixture fixture = ContentFixture.Create(lifecycleCapacity: 8);
        PrefabHandle prefab = fixture.Handle("direct/wide-reuse");
        ChangeCursor changes = fixture.World.CreateChangeCursorFromNow();
        LifecycleCursor lifecycle = fixture.World.CaptureLifecycleCursorFromNow();
        Entity compiled = fixture.World.Instantiate(prefab);
        Assert.True(fixture.World.TryCreate(
            prefab.TargetArchetype,
            fixture.Wide,
            new ContentWide { A = 11, P = 99 },
            out Entity manual));

        Assert.True(fixture.World.TryRead(compiled, fixture.Wide, out ContentWide compiledValue));
        Assert.True(fixture.World.TryRead(manual, fixture.Wide, out ContentWide manualValue));
        Assert.Equal(compiledValue.A, manualValue.A);
        Assert.Equal(compiledValue.P, manualValue.P);
        Assert.True(fixture.World.TryGetEnabled(compiled, fixture.Wide, out bool compiledEnabled));
        Assert.True(fixture.World.TryGetEnabled(manual, fixture.Wide, out bool manualEnabled));
        Assert.Equal(compiledEnabled, manualEnabled);
        Assert.True(fixture.World.TryGetArchetype(compiled, out ArchetypeHandle compiledArchetype));
        Assert.True(fixture.World.TryGetArchetype(manual, out ArchetypeHandle manualArchetype));
        Assert.Equal(compiledArchetype, manualArchetype);

        var changed = new ContentWideChangedAudit();
        // The public initialized-create overload is an ordinary tracked value
        // publication; compiled defaults are initialization and intentionally
        // do not publish a false change. Current logical state still converges.
        Assert.Equal(1, fixture.World.ExecuteChangedRowsJob(
            fixture.WideChanged, ref changes, ref changed));
        Assert.Equal(1, changed.Rows);
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[4];
        LifecycleReadResult created = fixture.World.ReadLifecycle(ref lifecycle, records);
        Assert.Equal(2, created.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
        Assert.Equal(LifecycleKind.Created, records[1].Kind);
    }

    private static void Content_CompilationIsRegistrationOrderDeterministic()
    {
        using ContentFixture forward = ContentFixture.Create(reverse: false);
        using ContentFixture reverse = ContentFixture.Create(reverse: true);
        Assert.Equal(forward.Catalog.DiagnosticHash, reverse.Catalog.DiagnosticHash);
        foreach (string key in ContentFixture.Keys)
        {
            CompiledPrefabInfo a = forward.Catalog.GetInfo(forward.Handle(key));
            CompiledPrefabInfo b = reverse.Catalog.GetInfo(reverse.Handle(key));
            Assert.Equal(a.Key, b.Key);
            Assert.Equal(a.SignatureHash, b.SignatureHash);
            Assert.Equal(a.DefaultByteCount, b.DefaultByteCount);
            Assert.Equal(a.Handle.PrefabId, b.Handle.PrefabId);
            Assert.Equal(a.Handle.TargetArchetype.Id, b.Handle.TargetArchetype.Id);
        }
    }

    private static void Content_InvalidDefinitionsFailCold()
    {
        ComponentSchema schema = ContentTestSchema.CreateSchema();
        var duplicate = new ContentCatalogBuilder(schema);
        _ = duplicate.Define(new PrefabKey("test/duplicate"));
        Assert.Throws<InvalidOperationException>(() => duplicate.Define(new PrefabKey("test/duplicate")));

        var repeated = new ContentCatalogBuilder(schema);
        PrefabBuilder repeat = repeated.Define(new PrefabKey("test/repeated"));
        repeat.With<ContentHealth>();
        Assert.Throws<InvalidOperationException>(() => repeat.With<ContentHealth>());

        var missing = new ContentCatalogBuilder(schema);
        PrefabBuilder setMissing = missing.Define(new PrefabKey("test/missing"));
        Assert.Throws<InvalidOperationException>(() => setMissing.Set(new ContentHealth { Value = 1 }));

        var tagValue = new ContentCatalogBuilder(schema);
        PrefabBuilder tagDefault = tagValue.Define(new PrefabKey("test/tag-value"));
        Assert.Throws<ArgumentException>(() => tagDefault.With(new ContentHumanTag()));

        var dataAsTag = new ContentCatalogBuilder(schema);
        PrefabBuilder badTag = dataAsTag.Define(new PrefabKey("test/data-tag"));
        Assert.Throws<ArgumentException>(() => badTag.Tag<ContentHealth>());

        var missingAttribute = new ContentCatalogBuilder(schema);
        Assert.Throws<ArgumentException>(() => missingAttribute.Register<MissingAttributeScript>());

        var transactional = new ContentCatalogBuilder(schema);
        Assert.Throws<InvalidOperationException>(() => transactional.Register<FailingScript>());
        Assert.Equal(0, transactional.DefinitionCount);
        PrefabBuilder recovered = transactional.Define(new PrefabKey("test/failing-script"));
        recovered.With<ContentHealth>();

        var unsupported = new ContentCatalogBuilder(schema);
        PrefabBuilder unsupportedDefinition = unsupported.Define(new PrefabKey("test/unsupported"));
        Assert.Throws<ArgumentException>(() => unsupportedDefinition.With<ContentUnregistered>());

        using ContentFixture fixture = ContentFixture.Create();
        Assert.Throws<InvalidOperationException>(() => fixture.Builder.Define(new PrefabKey("late/change")));
        Assert.Throws<InvalidOperationException>(() => fixture.Builder.Compile(
            fixture.World, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 1)));
    }

    private static void Content_ImmediateInstantiationIsAtomicIndependentAndLifecycleExact()
    {
        using ContentFixture fixture = ContentFixture.Create(lifecycleCapacity: 32);
        LifecycleCursor cursor = fixture.World.CaptureLifecycleCursorFromNow();
        PrefabHandle villager = fixture.Handle("actors/villager");
        Entity a = fixture.World.Instantiate(villager);
        Entity b = fixture.World.Instantiate(villager);
        AssertValue(fixture.World, a, fixture.Health, 80);
        AssertValue(fixture.World, b, fixture.Health, 80);
        Assert.True(fixture.World.TryWrite(a, fixture.Health, new ContentHealth { Value = 7 }));
        AssertValue(fixture.World, a, fixture.Health, 7);
        AssertValue(fixture.World, b, fixture.Health, 80);
        Assert.Equal(80, ReadDefault<ContentHealth>(fixture.Catalog, villager).Value);

        Span<LifecycleRecord> records = stackalloc LifecycleRecord[8];
        LifecycleReadResult result = fixture.World.ReadLifecycle(ref cursor, records);
        Assert.Equal(LifecycleReadStatus.Success, result.Status);
        Assert.Equal(2, result.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
        Assert.Equal(LifecycleKind.Created, records[1].Kind);
    }

    private static void Content_RowReuseRestoresCompiledDefaults()
    {
        using ContentFixture fixture = ContentFixture.Create();
        PrefabHandle prefab = fixture.Handle("direct/wide-reuse");
        Entity first = fixture.World.Instantiate(prefab);
        _ = fixture.World.Instantiate(prefab); // Prevent whole-store destruction from taking precedence.
        Assert.True(fixture.World.TryWrite(first, fixture.Wide, new ContentWide { A = -999, P = -777 }));
        Assert.True(fixture.World.TrySetEnabled(first, fixture.Wide, enabled: false));
        StructuralCommandBuffer commands = fixture.World.StructuralCommands;
        Assert.True(commands.TryEnqueueDestroy(first));
        DeferredEntity token = commands.EnqueueInstantiate(prefab);
        StructuralPlaybackResult playback = fixture.World.PlaybackStructuralCommands();
        Assert.Equal(1, playback.TemporaryHoleCount);
        Assert.Equal(1, playback.ReusedTemporaryHoleCount);
        Assert.Equal(0, playback.CompactedTemporaryHoleCount);
        Assert.True(commands.TryResolve(token, out Entity replacement));
        Assert.Equal(first.Index, replacement.Index);
        Assert.NotEqual(first.Generation, replacement.Generation);
        Assert.True(fixture.World.TryRead(replacement, fixture.Wide, out ContentWide wide));
        Assert.Equal(11L, wide.A);
        Assert.Equal(99L, wide.P);
        Assert.True(fixture.World.TryGetEnabled(replacement, fixture.Wide, out bool enabled));
        Assert.True(enabled);
    }

    private static void Content_DeferredInstantiationUsesOneCommandAndResolves()
    {
        using ContentFixture fixture = ContentFixture.Create(structuralCapacity: 8, lifecycleCapacity: 16);
        LifecycleCursor cursor = fixture.World.CaptureLifecycleCursorFromNow();
        StructuralCommandBuffer commands = fixture.World.StructuralCommands;
        DeferredEntity token = commands.EnqueueInstantiate(fixture.Handle("items/iron-sword"));
        Assert.Equal(1, commands.Count);
        StructuralPlaybackResult result = fixture.World.PlaybackStructuralCommands();
        Assert.Equal(1, result.CommandCount);
        Assert.Equal(1, result.CreatedCount);
        Assert.True(commands.TryResolve(token, out Entity entity));
        AssertValue(fixture.World, entity, fixture.Damage, 12);
        AssertValue(fixture.World, entity, fixture.Durability, 50);
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[4];
        LifecycleReadResult lifecycle = fixture.World.ReadLifecycle(ref cursor, records);
        Assert.Equal(1, lifecycle.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
    }

    private static void Content_SchedulerInstantiationUsesOneCreateAndSameSegmentReplacement()
    {
        ComponentSchema schema = ContentTestSchema.CreateSchema();
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1,
            maximumDependencies: 0,
            maximumWorkItems: 3,
            maximumStructuralSegments: 3,
            backgroundWorkerCount: 2,
            callerParticipates: true,
            structuralCommandsPerProducer: 8,
            structuralPayloadBytesPerProducer: 64,
            maximumPartitionsPerSystem: 3);
        using var world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(16),
            maximumArchetypes: 16,
            maximumPreparedPlans: 4,
            maximumStructuralCommands: 32,
            scheduler: scheduler,
            lifecycleRecordCapacity: 32));
        ContentCatalogBuilder builder = CreateBuilder(schema, reverse: false);
        CompiledPrefabCatalog catalog = builder.Compile(
            world, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 2));
        PrefabHandle prefab = catalog.GetHandle(new PrefabKey("actors/villager"));
        ComponentLookup<ContentHealth> health = world.PrepareLookup<ContentHealth>();
        ChangedRowsJobPlan<ContentHealth, ContentHealth> changed =
            world.PrepareChangedRowsJob<ContentHealth, ContentHealth>();
        world.RegisterRangeSystem<ContentSpawnRuntime, ContentSpawnJob>(
            new SystemDefinition(
                "content.scheduler-spawn",
                SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel,
                structuralComponents: [ContentTestSchema.GetComponentId<ContentHealth>()],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 1),
            new ContentSpawnRuntime(prefab, count: 8));
        world.Seal();
        world.Initialize();
        LifecycleCursor cursor = world.CaptureLifecycleCursorFromNow();
        world.BeginRun();
        world.Tick(0.1);

        Span<LifecycleRecord> records = stackalloc LifecycleRecord[16];
        LifecycleReadResult result = world.ReadLifecycle(ref cursor, records);
        Assert.Equal(8, result.Count);
        for (int index = 0; index < result.Count; index++)
        {
            Assert.Equal(LifecycleKind.Created, records[index].Kind);
            Assert.True(world.TryRead(records[index].Entity, health, out ContentHealth value));
            Assert.Equal(1_000 + index, value.Value);
        }
        var audit = new ContentChangedAudit();
        ChangeCursor changes = world.CreateChangeCursorFromBeginning();
        Assert.Equal(8, world.ExecuteChangedRowsJob(changed, ref changes, ref audit));
        Assert.Equal(8, audit.Rows);
    }

    private static void Content_InitialDefaultsDoNotMasqueradeAsValueWrites()
    {
        using ContentFixture fixture = ContentFixture.Create();
        ChangeCursor cursor = fixture.World.CreateChangeCursorFromNow();
        Entity entity = fixture.World.Instantiate(fixture.Handle("actors/villager"));
        var audit = new ContentChangedAudit();
        Assert.Equal(0, fixture.World.ExecuteChangedRowsJob(
            fixture.HealthChanged, ref cursor, ref audit));
        Assert.True(fixture.World.TryWrite(
            entity, fixture.Health, new ContentHealth { Value = 79 }));
        Assert.Equal(1, fixture.World.ExecuteChangedRowsJob(
            fixture.HealthChanged, ref cursor, ref audit));
        Assert.Equal(1, audit.Rows);
    }

    private static void Content_LaterMembershipAndDestroyLifecycleRemainOrdinary()
    {
        ComponentSchema schema = ContentTestSchema.CreateSchema();
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 1,
            maximumStructuralSegments: 1, backgroundWorkerCount: 0,
            structuralCommandsPerProducer: 2, structuralPayloadBytesPerProducer: 16,
            maximumPartitionsPerSystem: 1);
        using var world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(2), maximumArchetypes: 16, maximumPreparedPlans: 2,
            maximumStructuralCommands: 4, scheduler: scheduler, lifecycleRecordCapacity: 8));
        ContentCatalogBuilder builder = CreateBuilder(schema, reverse: false);
        CompiledPrefabCatalog catalog = builder.Compile(
            world, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 2));
        PrefabHandle prefab = catalog.GetHandle(new PrefabKey("direct/wide-reuse"));
        ComponentLookup<ContentHealth> health = world.PrepareLookup<ContentHealth>();
        world.RegisterRangeSystem<ContentSpawnRuntime, ContentSpawnJob>(
            new SystemDefinition(
                "content.membership-add",
                SystemStage.WorldSim,
                structuralComponents: [ContentTestSchema.GetComponentId<ContentHealth>()]),
            new ContentSpawnRuntime(prefab, count: 1));
        world.Seal(); world.Initialize();
        LifecycleCursor cursor = world.CaptureLifecycleCursorFromNow();
        world.BeginRun(); world.Tick(0.1);

        Span<LifecycleRecord> records = stackalloc LifecycleRecord[8];
        LifecycleReadResult createdAndAdded = world.ReadLifecycle(ref cursor, records);
        Assert.Equal(2, createdAndAdded.Count);
        Assert.Equal(LifecycleKind.Created, records[0].Kind);
        Assert.Equal(LifecycleKind.ComponentAdded, records[1].Kind);
        Assert.Equal(ContentTestSchema.GetComponentId<ContentHealth>(), records[1].ComponentId);
        Entity entity = records[0].Entity;
        Assert.True(world.TryRead(entity, health, out ContentHealth value));
        Assert.Equal(1_000, value.Value);

        Assert.True(world.TryDestroy(entity));
        LifecycleReadResult destroyed = world.ReadLifecycle(ref cursor, records);
        Assert.Equal(1, destroyed.Count);
        Assert.Equal(LifecycleKind.Destroyed, records[0].Kind);
        Assert.Equal(entity, records[0].Entity);
    }

    private static void Content_CapacityAndForeignHandlesRejectWithoutFalseLifecycle()
    {
        using ContentFixture full = ContentFixture.Create(entityCapacity: 1, lifecycleCapacity: 8);
        PrefabHandle prefab = full.Handle("actors/villager");
        _ = full.World.Instantiate(prefab);
        LifecycleCursor cursor = full.World.CaptureLifecycleCursorFromNow();
        Assert.False(full.World.TryInstantiate(prefab, out Entity rejected));
        Assert.Equal(Entity.Null, rejected);
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[2];
        Assert.Equal(LifecycleReadStatus.NoData, full.World.ReadLifecycle(ref cursor, records).Status);

        using ContentFixture other = ContentFixture.Create();
        Assert.Throws<ArgumentException>(() => other.World.TryInstantiate(prefab, out _));
        Assert.Throws<ArgumentException>(() => other.World.StructuralCommands.EnqueueInstantiate(prefab));
        Assert.Throws<ArgumentException>(() => other.World.TryInstantiate(PrefabHandle.Invalid, out _));

        using ContentFixture oneCommand = ContentFixture.Create(structuralCapacity: 1);
        Assert.True(oneCommand.World.StructuralCommands.TryEnqueueInstantiate(
            oneCommand.Handle("actors/villager"), out _));
        Assert.False(oneCommand.World.StructuralCommands.TryEnqueueInstantiate(
            oneCommand.Handle("actors/villager"), out _));

        ComponentSchema schema = ContentTestSchema.CreateSchema();
        using var chunkBounded = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(256), maximumArchetypes: 16, maximumPreparedPlans: 4,
            lifecycleRecordCapacity: 1));
        ContentCatalogBuilder builder = CreateBuilder(schema, reverse: false);
        CompiledPrefabCatalog catalog = builder.Compile(
            chunkBounded, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 1));
        PrefabHandle wide = catalog.GetHandle(new PrefabKey("direct/wide-reuse"));
        chunkBounded.Seal(); chunkBounded.Initialize(); chunkBounded.BeginRun();
        int created = 0;
        while (chunkBounded.TryInstantiate(wide, out _))
            created++;
        Assert.True(created > 0 && created < 256);
        LifecycleCursor rejectedCursor = chunkBounded.CaptureLifecycleCursorFromNow();
        Assert.False(chunkBounded.TryInstantiate(wide, out _));
        Assert.Equal(LifecycleReadStatus.NoData,
            chunkBounded.ReadLifecycle(ref rejectedCursor, records).Status);

        var forgedId = new PrefabHandle(chunkBounded.Id, uint.MaxValue, wide.ArchetypeId);
        Assert.Throws<ArgumentOutOfRangeException>(() => chunkBounded.TryInstantiate(forgedId, out _));
        var forgedRoute = new PrefabHandle(
            chunkBounded.Id,
            wide.PrefabId,
            catalog.GetHandle(new PrefabKey("actors/villager")).ArchetypeId);
        Assert.Throws<ArgumentException>(() => chunkBounded.TryInstantiate(forgedRoute, out _));
    }

    private static void Content_NativePinnedInstantiationMatches()
    {
        ComponentSchema schema = ContentTestSchema.CreateSchema();
        var native = new EcsWorld<NativeChunkArena>(schema, new EntityCapacity(32), 8,
            static capacity => new NativeChunkArena(capacity), 1, maximumPublicTransitions: 0, lifecycleRecordCapacity: 16);
        var pinned = new EcsWorld<PinnedChunkArena>(schema, new EntityCapacity(32), 8,
            static capacity => new PinnedChunkArena(capacity), 1, maximumPublicTransitions: 0, lifecycleRecordCapacity: 16);
        try
        {
            var nativeBuilder = CreateBuilder(schema, reverse: false);
            var pinnedBuilder = CreateBuilder(schema, reverse: true);
            CompiledPrefabCatalog nativeCatalog = nativeBuilder.CompileCore(
                native, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 2));
            CompiledPrefabCatalog pinnedCatalog = pinnedBuilder.CompileCore(
                pinned, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 2));
            ComponentLookupPlan<NativeChunkArena, ContentHealth> nativeHealth =
                native.PrepareComponentLookup<ContentHealth>();
            ComponentLookupPlan<PinnedChunkArena, ContentHealth> pinnedHealth =
                pinned.PrepareComponentLookup<ContentHealth>();
            native.Seal(); native.Initialize(); native.BeginRun();
            pinned.Seal(); pinned.Initialize(); pinned.BeginRun();
            PrefabHandle nativeHandle = nativeCatalog.GetHandle(new PrefabKey("actors/guard"));
            PrefabHandle pinnedHandle = pinnedCatalog.GetHandle(new PrefabKey("actors/guard"));
            Assert.True(native.TryInstantiatePrefab(nativeHandle.PrefabId, nativeHandle.ArchetypeId, out Entity n));
            Assert.True(pinned.TryInstantiatePrefab(pinnedHandle.PrefabId, pinnedHandle.ArchetypeId, out Entity p));
            Assert.True(native.TryGetComponent(n, nativeHealth, out ContentHealth nv));
            Assert.True(pinned.TryGetComponent(p, pinnedHealth, out ContentHealth pv));
            Assert.Equal(nv.Value, pv.Value);
            Assert.Equal(nativeCatalog.DiagnosticHash, pinnedCatalog.DiagnosticHash);
        }
        finally
        {
            native.Dispose();
            pinned.Dispose();
        }
    }

    private static void Content_WarmedLookupAndImmediateDeferredInstantiationAllocateZero()
    {
        using ContentFixture fixture = ContentFixture.Create(entityCapacity: 64, structuralCapacity: 8);
        PrefabKey key = new("actors/villager");
        PrefabHandle handle = fixture.Catalog.GetHandle(key);
        for (int index = 0; index < 8; index++)
        {
            Entity entity = fixture.World.Instantiate(handle);
            Assert.True(fixture.World.TryDestroy(entity));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            Assert.True(fixture.Catalog.TryGetHandle(key, out PrefabHandle resolved));
            Entity entity = fixture.World.Instantiate(resolved);
            Assert.True(fixture.World.TryDestroy(entity));
        }
        long immediate = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, immediate);

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            DeferredEntity token = fixture.World.StructuralCommands.EnqueueInstantiate(handle);
            _ = fixture.World.PlaybackStructuralCommands();
            Assert.True(fixture.World.StructuralCommands.TryResolve(token, out Entity entity));
            Assert.True(fixture.World.TryDestroy(entity));
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static T ReadDefault<T>(CompiledPrefabCatalog catalog, PrefabHandle handle)
        where T : unmanaged, IComponent<T>
    {
        ref readonly CompiledPrefabEntry entry = ref catalog.GetEntry(handle.PrefabId);
        ReadOnlySpan<PrefabDefaultDescriptor> descriptors = catalog.GetDescriptors(entry);
        for (int index = 0; index < descriptors.Length; index++)
        {
            if (descriptors[index].ComponentId == ContentTestSchema.GetComponentId<T>())
                return System.Runtime.InteropServices.MemoryMarshal.Read<T>(catalog.GetDefaultBytes(descriptors[index]));
        }
        return default;
    }

    private static void AssertValue(
        EcsWorld world,
        Entity entity,
        ComponentLookup<ContentHealth> lookup,
        int expected)
    {
        Assert.True(world.TryRead(entity, lookup, out ContentHealth value));
        Assert.Equal(expected, value.Value);
    }

    private static void AssertValue(
        EcsWorld world,
        Entity entity,
        ComponentLookup<ContentWealth> lookup,
        int expected)
    {
        Assert.True(world.TryRead(entity, lookup, out ContentWealth value));
        Assert.Equal(expected, value.Value);
    }

    private static void AssertValue(
        EcsWorld world,
        Entity entity,
        ComponentLookup<ContentDamage> lookup,
        int expected)
    {
        Assert.True(world.TryRead(entity, lookup, out ContentDamage value));
        Assert.Equal(expected, value.Value);
    }

    private static void AssertValue(
        EcsWorld world,
        Entity entity,
        ComponentLookup<ContentDurability> lookup,
        int expected)
    {
        Assert.True(world.TryRead(entity, lookup, out ContentDurability value));
        Assert.Equal(expected, value.Value);
    }

    private static void AssertEquivalentActor(ContentFixture fixture, Entity left, Entity right)
    {
        Assert.True(fixture.World.TryRead(left, fixture.Health, out ContentHealth lh));
        Assert.True(fixture.World.TryRead(right, fixture.Health, out ContentHealth rh));
        Assert.True(fixture.World.TryRead(left, fixture.Wealth, out ContentWealth lw));
        Assert.True(fixture.World.TryRead(right, fixture.Wealth, out ContentWealth rw));
        Assert.Equal(lh.Value, rh.Value);
        Assert.Equal(lw.Value, rw.Value);
        Assert.Equal(fixture.World.TryGetArchetype(left, out ArchetypeHandle la), true);
        Assert.Equal(fixture.World.TryGetArchetype(right, out ArchetypeHandle ra), true);
        Assert.Equal(la, ra);
    }

    private static ContentCatalogBuilder CreateBuilder(ComponentSchema schema, bool reverse)
    {
        var builder = new ContentCatalogBuilder(schema);
        if (reverse)
        {
            builder.Register<TemporaryEffectScript>();
            builder.Register<BreadScript>();
            builder.Register<IronSwordScript>();
            builder.Register<WolfScript>();
            builder.Register<BlacksmithScript>();
            builder.Register<GuardScript>();
            builder.Register<MerchantScript>();
            builder.Register<VillagerScript>();
        }
        else
        {
            builder.Register<VillagerScript>();
            builder.Register<MerchantScript>();
            builder.Register<GuardScript>();
            builder.Register<BlacksmithScript>();
            builder.Register<WolfScript>();
            builder.Register<IronSwordScript>();
            builder.Register<BreadScript>();
            builder.Register<TemporaryEffectScript>();
        }
        PrefabBuilder direct = builder.Define(new PrefabKey("direct/villager"));
        direct.With<ContentIdentity>();
        direct.With<ContentPosition>();
        direct.With(new ContentHealth { Value = 80 });
        direct.With<ContentNeeds>();
        direct.With(new ContentActivity { Profile = 1 });
        direct.With(new ContentWealth { Value = 10 });
        direct.Tag<ContentHumanTag>();
        direct.DisplayName("Direct villager");
        PrefabBuilder wide = builder.Define(new PrefabKey("direct/wide-reuse"));
        wide.With(new ContentWide { A = 11, P = 99 });
        PrefabBuilder wideHealth = builder.Define(new PrefabKey("direct/wide-with-health"));
        wideHealth.With(new ContentWide { A = 11, P = 99 });
        wideHealth.With(new ContentHealth { Value = 50 });
        return builder;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private sealed class ContentSpawnRuntime : IRangeSystemRuntime<ContentSpawnJob>
    {
        private readonly PrefabHandle _prefab;
        private readonly int _count;

        internal ContentSpawnRuntime(PrefabHandle prefab, int count) =>
            (_prefab, _count) = (prefab, count);

        public int GetRangeCount(in SystemPrepareContext context) => _count;

        public ContentSpawnJob Prepare(in SystemPrepareContext context) => new(_prefab);
    }

    private readonly struct ContentSpawnJob : IRangeJob
    {
        private readonly PrefabHandle _prefab;

        internal ContentSpawnJob(PrefabHandle prefab) => _prefab = prefab;

        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                DeferredEntity entity = context.Commands.Instantiate(_prefab);
                Assert.True(context.Commands.TryAdd(
                    entity, new ContentHealth { Value = 1_000 + index }));
            }
        }
    }

    private struct ContentChangedAudit : IReadOnlyChunkJob<ContentHealth>
    {
        internal int Rows;
        public void Execute(in ReadOnlyComponentChunk<ContentHealth> chunk) => Rows += chunk.Count;
    }

    private struct ContentWideChangedAudit : IReadOnlyChunkJob<ContentWide>
    {
        internal int Rows;
        public void Execute(in ReadOnlyComponentChunk<ContentWide> chunk) => Rows += chunk.Count;
    }

    private sealed class ContentFixture : IDisposable
    {
        private ContentFixture(EcsWorld world, ContentCatalogBuilder builder, CompiledPrefabCatalog catalog)
        {
            World = world;
            Builder = builder;
            Catalog = catalog;
            Health = world.PrepareLookup<ContentHealth>();
            Wealth = world.PrepareLookup<ContentWealth>();
            Damage = world.PrepareLookup<ContentDamage>();
            Durability = world.PrepareLookup<ContentDurability>();
            HumanTag = world.PrepareLookup<ContentHumanTag>();
            Wide = world.PrepareLookup<ContentWide>();
            HealthChanged = world.PrepareChangedRowsJob<ContentHealth, ContentHealth>();
            WideChanged = world.PrepareChangedRowsJob<ContentWide, ContentWide>();
            world.Seal();
            world.Initialize();
            world.BeginRun();
        }

        internal static readonly string[] Keys =
        [
            "actors/blacksmith", "actors/guard", "actors/merchant", "actors/villager",
            "creatures/wolf", "direct/villager", "direct/wide-reuse", "direct/wide-with-health",
            "effects/temporary",
            "items/bread", "items/iron-sword",
        ];

        internal EcsWorld World { get; }
        internal ContentCatalogBuilder Builder { get; }
        internal CompiledPrefabCatalog Catalog { get; }
        internal ComponentLookup<ContentHealth> Health { get; }
        internal ComponentLookup<ContentWealth> Wealth { get; }
        internal ComponentLookup<ContentDamage> Damage { get; }
        internal ComponentLookup<ContentDurability> Durability { get; }
        internal ComponentLookup<ContentHumanTag> HumanTag { get; }
        internal ComponentLookup<ContentWide> Wide { get; }
        internal ChangedRowsJobPlan<ContentHealth, ContentHealth> HealthChanged { get; }
        internal ChangedRowsJobPlan<ContentWide, ContentWide> WideChanged { get; }

        internal PrefabHandle Handle(string key) => Catalog.GetHandle(new PrefabKey(key));

        internal static ContentFixture Create(
            bool reverse = false,
            int entityCapacity = 256,
            int structuralCapacity = 32,
            int lifecycleCapacity = 64)
        {
            ComponentSchema schema = ContentTestSchema.CreateSchema();
            var world = new EcsWorld(schema, new EcsWorldOptions(
                new EntityCapacity(entityCapacity),
                maximumArchetypes: 16,
                maximumPreparedPlans: 8,
                maximumStructuralCommands: structuralCapacity,
                lifecycleRecordCapacity: lifecycleCapacity));
            ContentCatalogBuilder builder = CreateBuilder(schema, reverse);
            CompiledPrefabCatalog catalog = builder.Compile(
                world,
                new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 8));
            return new ContentFixture(world, builder, catalog);
        }

        public void Dispose() => World.Dispose();
    }
}

internal abstract class ContentEntityBase : BaseClass
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.With<ContentIdentity>();
        builder.With<ContentPosition>();
    }
}

internal abstract class ContentActorBase : ContentEntityBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.With(new ContentHealth { Value = 100 });
        builder.With<ContentNeeds>();
        builder.With(new ContentActivity { Profile = 1 });
        builder.With(new ContentWealth { Value = 10 });
    }
}

internal abstract class ContentHumanBase : ContentActorBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.Tag<ContentHumanTag>();
    }
}

[ObjectScript("actors/villager")]
internal sealed class VillagerScript : ContentHumanBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ContentHealth { Value = 80 });
        prefab.DisplayName("Villager");
    }
}

[ObjectScript("actors/merchant")]
internal sealed class MerchantScript : ContentHumanBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ContentHealth { Value = 90 });
        prefab.Set(new ContentWealth { Value = 500 });
        prefab.DisplayName("Merchant");
    }
}

[ObjectScript("actors/guard")]
internal sealed class GuardScript : ContentHumanBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ContentHealth { Value = 160 });
        prefab.Set(new ContentActivity { Profile = 7 });
        prefab.DisplayName("Guard");
    }
}

[ObjectScript("actors/blacksmith")]
internal sealed class BlacksmithScript : ContentHumanBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ContentHealth { Value = 110 });
        prefab.Set(new ContentWealth { Value = 250 });
        prefab.DisplayName("Blacksmith");
    }
}

internal abstract class ContentCreatureBase : ContentActorBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.Tag<ContentCreatureTag>();
    }
}

[ObjectScript("creatures/wolf")]
internal sealed class WolfScript : ContentCreatureBase
{
    protected override void Configure(PrefabBuilder prefab) =>
        prefab.Set(new ContentHealth { Value = 70 });
}

internal abstract class ContentItemBase : ContentEntityBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.With(new ContentWeight { Value = 1 });
        builder.With(new ContentWealth { Value = 1 });
        builder.Tag<ContentItemTag>();
    }
}

internal abstract class ContentEquipmentBase : ContentItemBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.With<ContentDamage>();
        builder.With<ContentDurability>();
        builder.Tag<ContentEquipmentTag>();
    }
}

[ObjectScript("items/iron-sword")]
internal sealed class IronSwordScript : ContentEquipmentBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Set(new ContentDamage { Value = 12 });
        prefab.Set(new ContentDurability { Value = 50 });
        prefab.Set(new ContentWeight { Value = 8 });
        prefab.Set(new ContentWealth { Value = 75 });
    }
}

[ObjectScript("items/bread")]
internal sealed class BreadScript : ContentItemBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.Tag<ContentFoodTag>();
        prefab.Set(new ContentWeight { Value = 2 });
        prefab.Set(new ContentWealth { Value = 3 });
    }
}

internal abstract class ContentEffectBase : ContentEntityBase
{
    protected override void Define(BaseClassBuilder builder)
    {
        base.Define(builder);
        builder.With(new ContentLifetime { Value = 30 });
        builder.Tag<ContentEffectTag>();
    }
}

[ObjectScript("effects/temporary")]
internal sealed class TemporaryEffectScript : ContentEffectBase { }

internal sealed class MissingAttributeScript : ContentEntityBase { }

[ObjectScript("test/failing-script")]
internal sealed class FailingScript : BaseClass
{
    protected override void Define(BaseClassBuilder builder)
    {
        builder.With<ContentHealth>();
        builder.With<ContentHealth>();
    }
}

internal struct ContentUnregistered : IComponent<ContentUnregistered> { public int Value { get; set; } }

[EcsComponent("content-test.identity")]
internal struct ContentIdentity : IComponent<ContentIdentity> { public int Value { get; set; } }
[EcsComponent("content-test.position")]
internal struct ContentPosition : IComponent<ContentPosition> { public int X { get; set; } public int Y { get; set; } }
[EcsComponent("content-test.health")]
internal struct ContentHealth : IComponent<ContentHealth> { public int Value; }
[EcsComponent("content-test.needs")]
internal struct ContentNeeds : IComponent<ContentNeeds>
{
    public int Hunger { get; set; }
    public int Fatigue { get; set; }
}
[EcsComponent("content-test.activity")]
internal struct ContentActivity : IComponent<ContentActivity> { public int Profile; }
[EcsComponent("content-test.wealth")]
internal struct ContentWealth : IComponent<ContentWealth> { public int Value; }
[EcsComponent("content-test.weight")]
internal struct ContentWeight : IComponent<ContentWeight> { public int Value; }
[EcsComponent("content-test.damage")]
internal struct ContentDamage : IComponent<ContentDamage> { public int Value; }
[EcsComponent("content-test.durability")]
internal struct ContentDurability : IComponent<ContentDurability> { public int Value; }
[EcsComponent("content-test.lifetime")]
internal struct ContentLifetime : IComponent<ContentLifetime> { public int Value; }
[EcsComponent("content-test.wide", IsEnableable = true)]
internal struct ContentWide : IComponent<ContentWide>
{
    public long A { get; set; } public long B { get; set; }
    public long C { get; set; } public long D { get; set; }
    public long E { get; set; } public long F { get; set; }
    public long G { get; set; } public long H { get; set; }
    public long I { get; set; } public long J { get; set; }
    public long K { get; set; } public long L { get; set; }
    public long M { get; set; } public long N { get; set; }
    public long O { get; set; } public long P { get; set; }
}
[EcsComponent("content-test.human", IsTag = true)]
internal struct ContentHumanTag : IComponent<ContentHumanTag> { }
[EcsComponent("content-test.creature", IsTag = true)]
internal struct ContentCreatureTag : IComponent<ContentCreatureTag> { }
[EcsComponent("content-test.item", IsTag = true)]
internal struct ContentItemTag : IComponent<ContentItemTag> { }
[EcsComponent("content-test.equipment", IsTag = true)]
internal struct ContentEquipmentTag : IComponent<ContentEquipmentTag> { }
[EcsComponent("content-test.food", IsTag = true)]
internal struct ContentFoodTag : IComponent<ContentFoodTag> { }
[EcsComponent("content-test.effect", IsTag = true)]
internal struct ContentEffectTag : IComponent<ContentEffectTag> { }

[EcsSchema(
    typeof(ContentIdentity), typeof(ContentPosition), typeof(ContentHealth), typeof(ContentNeeds),
    typeof(ContentActivity), typeof(ContentWealth), typeof(ContentWeight), typeof(ContentDamage),
    typeof(ContentDurability), typeof(ContentLifetime), typeof(ContentWide), typeof(ContentHumanTag), typeof(ContentCreatureTag),
    typeof(ContentItemTag), typeof(ContentEquipmentTag), typeof(ContentFoodTag), typeof(ContentEffectTag))]
internal static partial class ContentTestSchema { }
