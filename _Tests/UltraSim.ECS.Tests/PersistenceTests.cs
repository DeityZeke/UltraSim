using UltraSim.ECS;
using System.Buffers.Binary;

internal static class PersistenceTests
{
    internal static void RunAll()
    {
        RoundTripIsCanonicalAndClean();
        Console.WriteLine("PASS Persistence_RoundTripIsCanonicalAndClean");
        OwnedCaptureIsImmutableAfterEpoch();
        Console.WriteLine("PASS Persistence_OwnedCaptureIsImmutableAfterEpoch");
        CorruptionAndBoundsRejectBeforeRestore();
        Console.WriteLine("PASS Persistence_CorruptionAndBoundsRejectBeforeRestore");
        DurableIdentityDoesNotReuseAfterRuntimeSlotReuse();
        Console.WriteLine("PASS Persistence_DurableIdentityDoesNotReuseAfterRuntimeSlotReuse");
        DurableIdentityLookupDoesNotAccumulateTombstones();
        Console.WriteLine("PASS Persistence_DurableIdentityLookupDoesNotAccumulateTombstones");
        ExplicitShapeMigrationTransformsNToOne();
        Console.WriteLine("PASS Persistence_ExplicitShapeMigrationTransformsNToOne");
        ConvergingHistoricalShapesAggregateDestinationCapacity();
        Console.WriteLine("PASS Persistence_ConvergingHistoricalShapesAggregateDestinationCapacity");
        DirectHistoricalReaderUpgradesToCurrentVersion();
        Console.WriteLine("PASS Persistence_DirectHistoricalReaderUpgradesToCurrentVersion");
        ShapeMigrationCanTransferEnablePlane();
        Console.WriteLine("PASS Persistence_ShapeMigrationCanTransferEnablePlane");
        DerivedStateReconstructsAfterHydration();
        Console.WriteLine("PASS Persistence_DerivedStateReconstructsAfterHydration");
        PersistentSystemStateAndTickRoundTrip();
        Console.WriteLine("PASS Persistence_PersistentSystemStateAndTickRoundTrip");
        CanonicalBytesIgnorePackedRowHistory();
        Console.WriteLine("PASS Persistence_CanonicalBytesIgnorePackedRowHistory");
        NativeAndPinnedCaptureAreByteExact();
        Console.WriteLine("PASS Persistence_NativeAndPinnedCaptureAreByteExact");
        SerialAndC8ScheduledWorldsCaptureIdentically();
        Console.WriteLine("PASS Persistence_SerialAndC8ScheduledWorldsCaptureIdentically");
        ExternalKeyAndMissingPrefabProvenanceRoundTrip();
        Console.WriteLine("PASS Persistence_ExternalKeyAndMissingPrefabProvenanceRoundTrip");
    }

    private static void RoundTripIsCanonicalAndClean()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x1234));
        using var source = new EcsWorld(schema, Options(identity));
        ComponentId positionId = PersistenceTestSchema.GetComponentId<PersistPosition>();
        ComponentId targetId = PersistenceTestSchema.GetComponentId<PersistTarget>();
        ComponentId enabledId = PersistenceTestSchema.GetComponentId<PersistEnabled>();
        ComponentId membershipId = PersistenceTestSchema.GetComponentId<PersistMembership>();
        ComponentId tagId = PersistenceTestSchema.GetComponentId<PersistTag>();
        ComponentId[] signature = [positionId, targetId, enabledId, membershipId, tagId];
        Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
        ArchetypeHandle shape = source.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 4);
        ComponentLookup<PersistPosition> positions = source.PrepareLookup<PersistPosition>();
        ComponentLookup<PersistTarget> targets = source.PrepareLookup<PersistTarget>();
        ComponentLookup<PersistEnabled> enabled = source.PrepareLookup<PersistEnabled>();
        source.Seal(); source.Initialize(); source.BeginRun();
        WorldReservedPayloadSnapshot reserved = source.GetReservedPayloadSnapshot();
        Assert.True(reserved.PersistenceIdentityBytes > 0);
        WorldCapacityPlan planned = WorldCapacityPlanner.PlanFromOptions(source.Options,
            ReadOnlySpan<ArchetypeCapacityPlan>.Empty);
        Assert.Equal(reserved.PersistenceIdentityBytes, planned.ManagedPersistenceIdentityBytes);
        var entities = new Entity[128];
        for (int i = 0; i < entities.Length; i++)
        {
            Assert.True(source.TryCreate(shape, out entities[i]));
            Assert.True(source.TryWrite(entities[i], positions, new PersistPosition { X = i * 3, Y = -i }));
            Assert.True(source.TryWrite(entities[i], enabled, new PersistEnabled { Value = i * 7 }));
            Assert.True(source.TrySetEnabled(entities[i], enabled, (i & 3) != 0));
            _ = source.GetOrAssignPersistentId(entities[i]);
        }
        for (int i = 0; i < entities.Length; i++)
            Assert.True(source.TryWrite(entities[i], targets, new PersistTarget { Value = entities[(i * 17) % entities.Length] }));

        var captureOptions = new PersistenceCaptureOptions(42, 1_700_000_000_000);
        PersistenceSnapshotArtifact first = EcsPersistence.Capture(source, catalog, captureOptions, out PersistenceCaptureReport capture);
        Assert.Equal(128, capture.EntityCount);
        PersistenceRestoreOptions restoreOptions = new(
            new PersistenceLimits(64 * 1024 * 1024, 10_000, 128, 128, 10_000, 32 * 1024 * 1024, 32 * 1024 * 1024),
            ChunkLayoutOptions.KiB16);
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(first.Bytes, catalog, restoreOptions);
        using EcsWorld restored = plan.Restore(
            requirements => new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions)),
            out PersistenceRestoreReport restore);
        Assert.Equal(128, restore.EntityCount);
        Assert.Equal(128, restored.AliveCount);
        Assert.False(restored.CurrentChangeVersion.IsValid);
        Assert.Equal(0, restored.GetLifecycleHistorySnapshot().RetainedCount);
        PersistenceSnapshotArtifact second = EcsPersistence.Capture(restored, catalog, captureOptions, out _);
        Assert.True(first.Bytes.Span.SequenceEqual(second.Bytes.Span));
    }

    private static void CorruptionAndBoundsRejectBeforeRestore()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        using var world = new EcsWorld(schema, Options(new PersistenceIdentityOptions(new PersistentWorldId(77))));
        ComponentId[] signature = [PersistenceTestSchema.GetComponentId<PersistPosition>()];
        ArchetypeHandle shape = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 1);
        world.Seal(); world.Initialize(); world.BeginRun();
        Assert.True(world.TryCreate(shape, out _));
        byte[] corrupt = EcsPersistence.Capture(world, catalog, new PersistenceCaptureOptions(1), out _).Bytes.ToArray();
        corrupt[corrupt.Length / 2] ^= 0x40;
        PersistenceRestoreOptions options = new(
            new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16);
        Assert.Throws<PersistenceValidationException>(() => EcsPersistenceRestore.Preflight(corrupt, catalog, options));
        PersistenceRestoreOptions tiny = new(
            new PersistenceLimits(64, 1, 1, 1, 1, 1, 1), ChunkLayoutOptions.KiB16);
        Assert.Throws<PersistenceValidationException>(() => EcsPersistenceRestore.Preflight(corrupt, catalog, tiny));

        byte[] valid = EcsPersistence.Capture(world, catalog, new PersistenceCaptureOptions(2), out _).Bytes.ToArray();
        int stride = Math.Max(1, valid.Length / 31);
        for (int offset = 0; offset < valid.Length; offset += stride)
        {
            byte[] mutation = (byte[])valid.Clone();
            mutation[offset] ^= 0xA5;
            Assert.Throws<PersistenceValidationException>(() => EcsPersistenceRestore.Preflight(mutation, catalog, options));
        }
    }

    private static void OwnedCaptureIsImmutableAfterEpoch()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        using var source = new EcsWorld(schema, Options(new PersistenceIdentityOptions(new PersistentWorldId(76))));
        ArchetypeHandle shape = source.RegisterArchetype([PersistenceTestSchema.GetComponentId<PersistPosition>()], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<PersistPosition> lookup = source.PrepareLookup<PersistPosition>();
        source.Seal(); source.Initialize(); source.BeginRun();
        Assert.True(source.TryCreate(shape, out Entity entity));
        Assert.True(source.TryWrite(entity, lookup, new PersistPosition { X = 7, Y = 8 }));
        using PersistenceWorldCapture capture = EcsPersistence.BeginCapture(source, catalog, new PersistenceCaptureOptions(1));
        Assert.True(source.TryWrite(entity, lookup, new PersistPosition { X = 70, Y = 80 }));
        PersistenceSnapshotArtifact artifact = EcsPersistence.CompleteCapture(capture, out _);
        ComponentLookup<PersistPosition> restoredLookup = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 10, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            restoredLookup = world.PrepareLookup<PersistPosition>();
            return world;
        }, out _);
        Assert.True(restored.TryGetEntity(0, out Entity restoredEntity));
        Assert.True(restored.TryRead(restoredEntity, restoredLookup, out PersistPosition value));
        Assert.Equal(7, value.X);
        Assert.Equal(8, value.Y);
    }

    private static void DurableIdentityDoesNotReuseAfterRuntimeSlotReuse()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        using var world = new EcsWorld(schema, Options(new PersistenceIdentityOptions(new PersistentWorldId(88), 100)));
        ArchetypeHandle shape = world.RegisterArchetype([PersistenceTestSchema.GetComponentId<PersistPosition>()], ChunkLayoutOptions.KiB16, 1);
        world.Seal(); world.Initialize(); world.BeginRun();
        Assert.True(world.TryCreate(shape, out Entity first));
        PersistentEntityId firstId = world.GetOrAssignPersistentId(first);
        Assert.Equal(101UL, firstId.Value);
        Assert.True(world.TryDestroy(first));
        Assert.True(world.TryCreate(shape, out Entity second));
        PersistentEntityId secondId = world.GetOrAssignPersistentId(second);
        Assert.Equal(102UL, secondId.Value);
        Assert.False(world.TryResolvePersistentEntity(firstId, out _));
    }

    private static void ExplicitShapeMigrationTransformsNToOne()
    {
        ComponentSchema oldSchema = PersistenceOldShapeSchema.CreateSchema();
        PersistenceCatalog oldCatalog = PersistenceOldShapeSchema.CreatePersistenceCatalog();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(99));
        using var source = new EcsWorld(oldSchema, Options(identity));
        ComponentId[] oldSignature = [PersistenceOldShapeSchema.GetComponentId<OldLeft>(), PersistenceOldShapeSchema.GetComponentId<OldRight>()];
        Array.Sort(oldSignature, static (left, right) => left.Value.CompareTo(right.Value));
        ArchetypeHandle shape = source.RegisterArchetype(oldSignature, ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<OldLeft> left = source.PrepareLookup<OldLeft>();
        ComponentLookup<OldRight> right = source.PrepareLookup<OldRight>();
        source.Seal(); source.Initialize(); source.BeginRun();
        for (int i = 0; i < 32; i++)
        {
            Assert.True(source.TryCreate(shape, out Entity entity));
            Assert.True(source.TryWrite(entity, left, new OldLeft { Value = i }));
            Assert.True(source.TryWrite(entity, right, new OldRight { Value = i * 10 }));
        }
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, oldCatalog, new PersistenceCaptureOptions(1), out _);

        ComponentSchema currentSchema = PersistenceNewShapeSchema.CreateSchema();
        ComponentId combinedId = PersistenceNewShapeSchema.GetComponentId<NewCombined>();
        PersistenceCatalog currentCatalog = PersistenceNewShapeSchema.CreatePersistenceCatalog(builder =>
            builder.RegisterShapeMigration([2101U, 2102U], [combinedId], new CombineShapeMigrator()));
        ComponentLookup<NewCombined> combined = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, currentCatalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            combined = world.PrepareLookup<NewCombined>();
            return world;
        }, out _);
        for (uint i = 0; i < 32; i++)
        {
            Assert.True(restored.TryGetEntity(i, out Entity entity));
            Assert.True(restored.TryRead(entity, combined, out NewCombined value));
            Assert.Equal(checked((int)i * 11), value.Value);
        }
    }

    private static void DurableIdentityLookupDoesNotAccumulateTombstones()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        using var world = new EcsWorld(schema, Options(new PersistenceIdentityOptions(new PersistentWorldId(89))));
        ArchetypeHandle shape = world.RegisterArchetype([PersistenceTestSchema.GetComponentId<PersistPosition>()], ChunkLayoutOptions.KiB16, 1);
        world.Seal(); world.Initialize(); world.BeginRun();
        PersistentEntityId latest = default;
        for (int i = 0; i < 4_096; i++)
        {
            Assert.True(world.TryCreate(shape, out Entity entity));
            latest = world.GetOrAssignPersistentId(entity);
            Assert.True(world.TryResolvePersistentEntity(latest, out Entity resolved));
            Assert.Equal(entity, resolved);
            Assert.True(world.TryDestroy(entity));
        }
        Assert.False(world.TryResolvePersistentEntity(latest, out _));
        Assert.True(world.TryCreate(shape, out Entity final));
        Assert.Equal(4097UL, world.GetOrAssignPersistentId(final).Value);
    }

    private static void DerivedStateReconstructsAfterHydration()
    {
        ComponentSchema schema = PersistenceDerivedSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceDerivedSchema.CreatePersistenceCatalog(builder =>
            builder.RegisterDerived<DerivedDouble>(new DoubleReconstructor()));
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(111));
        using var source = new EcsWorld(schema, Options(identity));
        ComponentId[] signature = [PersistenceDerivedSchema.GetComponentId<DerivedBase>(), PersistenceDerivedSchema.GetComponentId<DerivedDouble>()];
        Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
        ArchetypeHandle shape = source.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<DerivedBase> values = source.PrepareLookup<DerivedBase>();
        source.Seal(); source.Initialize(); source.BeginRun();
        for (int i = 0; i < 16; i++)
        {
            Assert.True(source.TryCreate(shape, out Entity entity));
            Assert.True(source.TryWrite(entity, values, new DerivedBase { Value = i + 3 }));
        }
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, catalog, new PersistenceCaptureOptions(1), out _);
        ComponentLookup<DerivedDouble> doubles = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            doubles = world.PrepareLookup<DerivedDouble>();
            return world;
        }, out _);
        for (uint i = 0; i < 16; i++)
        {
            Assert.True(restored.TryGetEntity(i, out Entity entity));
            Assert.True(restored.TryRead(entity, doubles, out DerivedDouble value));
            Assert.Equal(checked(((int)i + 3) * 2), value.Value);
        }
    }

    private static void ConvergingHistoricalShapesAggregateDestinationCapacity()
    {
        ComponentSchema oldSchema = PersistenceOldShapeSchema.CreateSchema();
        PersistenceCatalog oldCatalog = PersistenceOldShapeSchema.CreatePersistenceCatalog();
        using var source = new EcsWorld(oldSchema, Options(
            new PersistenceIdentityOptions(new PersistentWorldId(112))));
        ArchetypeHandle leftShape = source.RegisterArchetype([PersistenceOldShapeSchema.GetComponentId<OldLeft>()], ChunkLayoutOptions.KiB16, 1);
        ArchetypeHandle rightShape = source.RegisterArchetype([PersistenceOldShapeSchema.GetComponentId<OldRight>()], ChunkLayoutOptions.KiB16, 1);
        source.Seal(); source.Initialize(); source.BeginRun();
        for (int i = 0; i < 150; i++)
        {
            Assert.True(source.TryCreate(leftShape, out _));
            Assert.True(source.TryCreate(rightShape, out _));
        }
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, oldCatalog, new PersistenceCaptureOptions(1), out _);
        ComponentSchema currentSchema = PersistenceConvergedSchema.CreateSchema();
        ComponentId destination = PersistenceConvergedSchema.GetComponentId<ConvergedWide>();
        PersistenceCatalog currentCatalog = PersistenceConvergedSchema.CreatePersistenceCatalog(builder =>
        {
            builder.RegisterShapeMigration([2101U], [destination], new FillConvergedMigrator(1));
            builder.RegisterShapeMigration([2102U], [destination], new FillConvergedMigrator(2));
        });
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, currentCatalog,
            new PersistenceRestoreOptions(new PersistenceLimits(2_000_000, 1000, 10, 10, 100, 2_000_000, 2_000_000), ChunkLayoutOptions.KiB16));
        Assert.True(plan.Requirements.Shapes.All(static shape => shape.MaximumChunks >= 2));
        using EcsWorld restored = plan.Restore(requirements => new EcsWorld(requirements.Schema,
            Options(requirements.IdentityOptions)), out _);
        Assert.Equal(300, restored.AliveCount);
    }

    private static void PersistentSystemStateAndTickRoundTrip()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(222));
        EcsSchedulerOptions scheduler = SchedulerOptions();
        var sourceRuntime = new PersistentCounterRuntime { Value = 731 };
        var optionalSourceRuntime = new PersistentCounterRuntime(required: false) { Value = 99 };
        using var source = new EcsWorld(schema, Options(identity, scheduler));
        source.RegisterRangeSystem<PersistentCounterRuntime, EmptyPersistenceJob>(
            new SystemDefinition("persistence.counter", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial, persistenceId: 0xCAFE, stateSchemaVersion: 1), sourceRuntime);
        source.RegisterRangeSystem<PersistentCounterRuntime, EmptyPersistenceJob>(
            new SystemDefinition("persistence.optional", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial, persistenceId: 0xBEEF, stateSchemaVersion: 1), optionalSourceRuntime);
        source.Seal(); source.Initialize(); source.BeginRun();
        source.Tick(0.016); source.Tick(0.016);
        Assert.Throws<PersistenceValidationException>(() => EcsPersistence.Capture(source, catalog,
            new PersistenceCaptureOptions(99, maximumSystemStateBytes: 3), out _));
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, catalog, new PersistenceCaptureOptions(1), out _);

        var restoredRuntime = new PersistentCounterRuntime();
        var restoredOptionalRuntime = new PersistentCounterRuntime(required: false);
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions, scheduler));
            world.RegisterRangeSystem<PersistentCounterRuntime, EmptyPersistenceJob>(
                new SystemDefinition("persistence.counter", SystemStage.WorldSim,
                    executionPolicy: SystemExecutionPolicy.Serial, persistenceId: 0xCAFE, stateSchemaVersion: 1), restoredRuntime);
            world.RegisterRangeSystem<PersistentCounterRuntime, EmptyPersistenceJob>(
                new SystemDefinition("persistence.optional", SystemStage.WorldSim,
                    executionPolicy: SystemExecutionPolicy.Serial, persistenceId: 0xBEEF, stateSchemaVersion: 1), restoredOptionalRuntime);
            return world;
        }, out _);
        Assert.Equal(731, restoredRuntime.Value);
        Assert.Equal(99, restoredOptionalRuntime.Value);
        Assert.Equal(2UL, restored.TickCount);

        var requiredOnlyRuntime = new PersistentCounterRuntime();
        PersistenceRestorePlan optionalDropPlan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld requiredOnly = optionalDropPlan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions, scheduler));
            world.RegisterRangeSystem<PersistentCounterRuntime, EmptyPersistenceJob>(
                new SystemDefinition("persistence.counter", SystemStage.WorldSim,
                    executionPolicy: SystemExecutionPolicy.Serial, persistenceId: 0xCAFE, stateSchemaVersion: 1), requiredOnlyRuntime);
            return world;
        }, out _);
        Assert.Equal(731, requiredOnlyRuntime.Value);

        PersistenceRestorePlan missingRequiredPlan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        Assert.Throws<PersistenceCompatibilityException>(() =>
        {
            using EcsWorld unexpected = missingRequiredPlan.Restore(
                requirements => new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions, scheduler)), out _);
        });
    }

    private static void DirectHistoricalReaderUpgradesToCurrentVersion()
    {
        const string key = "migration.versioned-value";
        const uint persistenceId = 2501;
        var oldFingerprint = new PersistenceSchemaFingerprint(0x2501, 1);
        ComponentSchema oldSchema = ComponentSchema.Create(new SchemaId(0x2501, 1),
        [
            ComponentRegistration.Create<VersionedValue>(new ComponentId(0), key, StableHash64.Compute(key),
                version: 1, ComponentFlags.Persistent, persistenceId, ComponentPersistenceMode.Value),
        ]);
        PersistenceCatalog oldCatalog = new PersistenceCatalogBuilder(oldSchema)
            .Register<VersionedValue>(oldFingerprint, new VersionedValueCodec())
            .Build();
        using var source = new EcsWorld(oldSchema, Options(new PersistenceIdentityOptions(new PersistentWorldId(113))));
        ArchetypeHandle shape = source.RegisterArchetype([new ComponentId(0)], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<VersionedValue> lookup = source.PrepareLookup<VersionedValue>();
        source.Seal(); source.Initialize(); source.BeginRun();
        Assert.True(source.TryCreate(shape, out Entity entity));
        Assert.True(source.TryWrite(entity, lookup, new VersionedValue { Value = 7 }));
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, oldCatalog, new PersistenceCaptureOptions(1), out _);

        ComponentSchema currentSchema = ComponentSchema.Create(new SchemaId(0x2501, 2),
        [
            ComponentRegistration.Create<VersionedValue>(new ComponentId(0), key, StableHash64.Compute(key),
                version: 2, ComponentFlags.Persistent, persistenceId, ComponentPersistenceMode.Value),
        ]);
        PersistenceCatalog currentCatalog = new PersistenceCatalogBuilder(currentSchema)
            .Register<VersionedValue>(new PersistenceSchemaFingerprint(0x2501, 2), new VersionedValueCodec())
            .RegisterHistoricalReader<VersionedValue>(1, oldFingerprint, new VersionedValueV1Reader())
            .Build();
        ComponentLookup<VersionedValue> restoredLookup = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, currentCatalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 10, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            restoredLookup = world.PrepareLookup<VersionedValue>();
            return world;
        }, out PersistenceRestoreReport report);
        Assert.Equal(1, report.MigratedComponentCount);
        Assert.True(restored.TryGetEntity(0, out Entity restoredEntity));
        Assert.True(restored.TryRead(restoredEntity, restoredLookup, out VersionedValue value));
        Assert.Equal(70, value.Value);
    }

    private static void ShapeMigrationCanTransferEnablePlane()
    {
        ComponentSchema oldSchema = PersistenceOldEnableSchema.CreateSchema();
        PersistenceCatalog oldCatalog = PersistenceOldEnableSchema.CreatePersistenceCatalog();
        using var source = new EcsWorld(oldSchema, Options(new PersistenceIdentityOptions(new PersistentWorldId(114))));
        ArchetypeHandle shape = source.RegisterArchetype([PersistenceOldEnableSchema.GetComponentId<OldEnable>()], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<OldEnable> oldLookup = source.PrepareLookup<OldEnable>();
        source.Seal(); source.Initialize(); source.BeginRun();
        for (int i = 0; i < 17; i++)
        {
            Assert.True(source.TryCreate(shape, out Entity entity));
            Assert.True(source.TrySetEnabled(entity, oldLookup, (i & 1) == 0));
        }
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, oldCatalog, new PersistenceCaptureOptions(1), out _);
        ComponentSchema currentSchema = PersistenceNewEnableSchema.CreateSchema();
        ComponentId destination = PersistenceNewEnableSchema.GetComponentId<NewEnable>();
        PersistenceCatalog currentCatalog = PersistenceNewEnableSchema.CreatePersistenceCatalog(builder =>
            builder.RegisterShapeMigration([2601U], [destination], new EnablePlaneMigrator()));
        ComponentLookup<NewEnable> newLookup = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, currentCatalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            newLookup = world.PrepareLookup<NewEnable>();
            return world;
        }, out _);
        for (uint i = 0; i < 17; i++)
        {
            Assert.True(restored.TryGetEntity(i, out Entity entity));
            Assert.True(restored.TryGetEnabled(entity, newLookup, out bool enabled));
            Assert.Equal((i & 1) == 0, enabled);
        }
    }

    private static void CanonicalBytesIgnorePackedRowHistory()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        using var untouched = CreateCanonicalOrderingWorld(schema, out ArchetypeHandle untouchedSource,
            out _, out _, out Entity[] untouchedEntities);
        using var reordered = CreateCanonicalOrderingWorld(schema, out ArchetypeHandle reorderedSource,
            out ArchetypeTransition toTemporary, out ArchetypeTransition toSource, out Entity[] reorderedEntities);
        Assert.Equal(untouchedSource.Id, reorderedSource.Id);

        Assert.True(reordered.TryTransition(reorderedEntities[0], toTemporary));
        Assert.True(reordered.TryTransition(reorderedEntities[0], toSource));
        PersistenceCaptureOptions options = new(500, 1_700_000_000_500);
        PersistenceSnapshotArtifact expected = EcsPersistence.Capture(untouched, catalog, options, out _);
        PersistenceSnapshotArtifact actual = EcsPersistence.Capture(reordered, catalog, options, out _);
        Assert.True(expected.Bytes.Span.SequenceEqual(actual.Bytes.Span));

        for (int index = 0; index < untouchedEntities.Length; index++)
        {
            Assert.True(untouched.TryGetPersistentId(untouchedEntities[index], out PersistentEntityId left));
            Assert.True(reordered.TryGetPersistentId(reorderedEntities[index], out PersistentEntityId right));
            Assert.Equal(left, right);
        }
    }

    private static EcsWorld CreateCanonicalOrderingWorld(
        ComponentSchema schema,
        out ArchetypeHandle source,
        out ArchetypeTransition toTemporary,
        out ArchetypeTransition toSource,
        out Entity[] entities)
    {
        var world = new EcsWorld(schema, Options(new PersistenceIdentityOptions(new PersistentWorldId(0xCA1100))));
        ComponentId positionId = PersistenceTestSchema.GetComponentId<PersistPosition>();
        ComponentId membershipId = PersistenceTestSchema.GetComponentId<PersistMembership>();
        ComponentId tagId = PersistenceTestSchema.GetComponentId<PersistTag>();
        ComponentId[] sourceSignature = [positionId, membershipId];
        ComponentId[] temporarySignature = [positionId, membershipId, tagId];
        Array.Sort(sourceSignature, static (left, right) => left.CompareTo(right));
        Array.Sort(temporarySignature, static (left, right) => left.CompareTo(right));
        source = world.RegisterArchetype(sourceSignature, ChunkLayoutOptions.KiB16, 2);
        ArchetypeHandle temporary = world.RegisterArchetype(temporarySignature, ChunkLayoutOptions.KiB16, 2);
        toTemporary = world.PrepareTransition(source, temporary);
        toSource = world.PrepareTransition(temporary, source);
        ComponentLookup<PersistPosition> position = world.PrepareLookup<PersistPosition>();
        world.Seal(); world.Initialize(); world.BeginRun();
        entities = new Entity[256];
        for (int index = 0; index < entities.Length; index++)
        {
            Assert.True(world.TryCreate(source, out entities[index]));
            Assert.True(world.TryWrite(entities[index], position, new PersistPosition { X = index * 11, Y = -index * 7 }));
            _ = world.GetOrAssignPersistentId(entities[index]);
        }
        return world;
    }

    private static void NativeAndPinnedCaptureAreByteExact()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        using EcsWorld<NativeChunkArena> native = CreateBackendPersistenceWorld(
            schema, static capacity => new NativeChunkArena(capacity));
        using EcsWorld<PinnedChunkArena> pinned = CreateBackendPersistenceWorld(
            schema, static capacity => new PinnedChunkArena(capacity));
        PersistenceCaptureOptions options = new(600, 1_700_000_000_600);
        PersistenceSnapshotArtifact nativeArtifact = EcsPersistence.CaptureBackend(native, catalog, options, out _);
        PersistenceSnapshotArtifact pinnedArtifact = EcsPersistence.CaptureBackend(pinned, catalog, options, out _);
        Assert.True(nativeArtifact.Bytes.Span.SequenceEqual(pinnedArtifact.Bytes.Span));
    }

    private static EcsWorld<TArena> CreateBackendPersistenceWorld<TArena>(
        ComponentSchema schema,
        Func<ChunkArenaCapacity, TArena> arenaFactory)
        where TArena : class, IChunkArenaBackend
    {
        var world = new EcsWorld<TArena>(schema, new EntityCapacity(512), maximumArchetypes: 1,
            arenaFactory, maximumQueryPlans: 0, persistence: new PersistenceIdentityOptions(new PersistentWorldId(0xBAAC0)));
        ComponentId[] signature =
        [
            PersistenceTestSchema.GetComponentId<PersistPosition>(),
            PersistenceTestSchema.GetComponentId<PersistEnabled>(),
            PersistenceTestSchema.GetComponentId<PersistTag>(),
        ];
        Array.Sort(signature, static (left, right) => left.CompareTo(right));
        ArchetypeId archetype = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 2);
        ArchetypeComponentBinding<PersistPosition> position = world.BindComponent<PersistPosition>(
            archetype, PersistenceTestSchema.GetComponentId<PersistPosition>());
        ArchetypeComponentBinding<PersistEnabled> enabled = world.BindComponent<PersistEnabled>(
            archetype, PersistenceTestSchema.GetComponentId<PersistEnabled>());
        world.Seal(); world.Initialize(); world.BeginRun();
        PopulateBackendPersistenceWorld(world, archetype, position, enabled);
        return world;
    }

    private static void PopulateBackendPersistenceWorld<TArena>(
        EcsWorld<TArena> world,
        ArchetypeId archetype,
        ArchetypeComponentBinding<PersistPosition> position,
        ArchetypeComponentBinding<PersistEnabled> enabled)
        where TArena : class, IChunkArenaBackend
    {
        for (int index = 0; index < 320; index++)
        {
            Assert.True(world.TryCreateDefault(archetype, out Entity entity));
            Assert.True(world.TrySetComponent(entity, position, new PersistPosition { X = index * 3, Y = index * -5 }));
            Assert.True(world.TrySetComponent(entity, enabled, new PersistEnabled { Value = index * 17 }));
            Assert.True(world.TrySetComponentEnabled(entity, enabled, (index & 3) != 0));
            _ = world.PersistenceIdentities.GetOrAssign(entity);
        }
    }

    private static void SerialAndC8ScheduledWorldsCaptureIdentically()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        using EcsWorld serial = CreateScheduledPersistenceWorld(schema, backgroundWorkers: 0,
            out PersistenceIncrementRuntime serialRuntime);
        using EcsWorld parallel = CreateScheduledPersistenceWorld(schema, backgroundWorkers: 7,
            out PersistenceIncrementRuntime parallelRuntime);
        for (int tick = 0; tick < 16; tick++)
        {
            serial.Tick(1d / 60d);
            parallel.Tick(1d / 60d);
        }
        Assert.Equal(16 * 4096, serialRuntime.Rows);
        Assert.Equal(serialRuntime.Rows, parallelRuntime.Rows);
        PersistenceCaptureOptions options = new(700, 1_700_000_000_700);
        PersistenceSnapshotArtifact serialArtifact = EcsPersistence.Capture(serial, catalog, options, out _);
        PersistenceSnapshotArtifact parallelArtifact = EcsPersistence.Capture(parallel, catalog, options, out _);
        Assert.True(serialArtifact.Bytes.Span.SequenceEqual(parallelArtifact.Bytes.Span));
    }

    private static EcsWorld CreateScheduledPersistenceWorld(
        ComponentSchema schema,
        int backgroundWorkers,
        out PersistenceIncrementRuntime runtime)
    {
        var scheduler = new EcsSchedulerOptions(maximumSystems: 1, maximumDependencies: 0,
            maximumWorkItems: 16, maximumStructuralSegments: 16, backgroundWorkerCount: backgroundWorkers,
            callerParticipates: true, structuralCommandsPerProducer: 0, structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 8);
        var options = new EcsWorldOptions(new EntityCapacity(4096), maximumArchetypes: 1,
            maximumPreparedPlans: 2, maximumPreparedTransitions: 0, maximumStructuralCommands: 0,
            scheduler: scheduler, persistence: new PersistenceIdentityOptions(new PersistentWorldId(0xC8C8)));
        var world = new EcsWorld(schema, options);
        ArchetypeHandle shape = world.RegisterArchetype(
            [PersistenceTestSchema.GetComponentId<PersistPosition>()], ChunkLayoutOptions.KiB16, 4);
        WriteJobPlan<PersistPosition> plan = world.PrepareWriteJob<PersistPosition>();
        runtime = new PersistenceIncrementRuntime();
        world.RegisterWriteSystem<PersistPosition, PersistenceIncrementRuntime, PersistenceIncrementJob>(
            new SystemDefinition("persistence.parity.increment", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel, parallelRowThreshold: 1,
                minimumRowsPerPartition: 64), plan, runtime);
        ComponentLookup<PersistPosition> lookup = world.PrepareLookup<PersistPosition>();
        world.Seal(); world.Initialize(); world.BeginRun();
        for (int index = 0; index < 4096; index++)
        {
            Assert.True(world.TryCreate(shape, out Entity entity));
            Assert.True(world.TryWrite(entity, lookup, new PersistPosition { X = index, Y = -index }));
            _ = world.GetOrAssignPersistentId(entity);
        }
        return world;
    }

    private static void ExternalKeyAndMissingPrefabProvenanceRoundTrip()
    {
        ComponentSchema schema = PersistenceTestSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceTestSchema.CreatePersistenceCatalog();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0xE17E));
        using var source = new EcsWorld(schema, Options(identity));
        ComponentId linkId = PersistenceTestSchema.GetComponentId<PersistExternalLink>();
        ArchetypeHandle shape = source.RegisterArchetype([linkId], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<PersistExternalLink> link = source.PrepareLookup<PersistExternalLink>();
        source.Seal(); source.Initialize(); source.BeginRun();
        Assert.True(source.TryCreate(shape, out Entity entity));
        var external = new PersistentEntityKey(new PersistentWorldId(0xAACC), new PersistentEntityId(0x55AA));
        Assert.True(source.TryWrite(entity, link, new PersistExternalLink { Value = external }));
        var missingPrefab = new PrefabKey("content.missing.after-save");
        source.PersistenceCore.PersistenceIdentities.SetPrefabKey(entity, missingPrefab);

        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, catalog,
            new PersistenceCaptureOptions(800, 1_700_000_000_800), out _);
        ComponentLookup<PersistExternalLink> restoredLink = default;
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog,
            new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 10, 10, 10, 100, 1_000_000, 1_000_000),
                ChunkLayoutOptions.KiB16));
        using EcsWorld restored = plan.Restore(requirements =>
        {
            var world = new EcsWorld(requirements.Schema, Options(requirements.IdentityOptions));
            restoredLink = world.PrepareLookup<PersistExternalLink>();
            return world;
        }, out _);
        Assert.True(restored.TryGetEntity(0, out Entity restoredEntity));
        Assert.True(restored.TryRead(restoredEntity, restoredLink, out PersistExternalLink value));
        Assert.Equal(external, value.Value);
        Assert.True(restored.TryGetPrefabProvenance(restoredEntity, out PrefabKey provenance));
        Assert.Equal(missingPrefab, provenance);
    }

    private static EcsWorldOptions Options(PersistenceIdentityOptions persistence, EcsSchedulerOptions scheduler = default) => new(
        new EntityCapacity(512), maximumArchetypes: 8, maximumPreparedPlans: 8,
        maximumPreparedTransitions: 8, maximumStructuralCommands: 8,
        scheduler: scheduler, lifecycleRecordCapacity: 512, persistence: persistence);

    private static EcsSchedulerOptions SchedulerOptions() => new(maximumSystems: 2, maximumDependencies: 0,
        maximumWorkItems: 2, maximumStructuralSegments: 2, backgroundWorkerCount: 0,
        structuralCommandsPerProducer: 0, structuralPayloadBytesPerProducer: 0);
}

internal sealed class PersistenceIncrementRuntime : ISystemRuntime<PersistenceIncrementJob>
{
    internal int Rows;
    public PersistenceIncrementJob Prepare(in SystemPrepareContext context) => default;
    public void Complete(in SystemCompleteContext context, ReadOnlySpan<PersistenceIncrementJob> jobs)
    {
        for (int index = 0; index < jobs.Length; index++) Rows += jobs[index].Rows;
    }
}

internal struct PersistenceIncrementJob : IScheduledWriteChunkJob<PersistPosition>
{
    internal int Rows;
    public void Execute(in SystemJobContext context, in WriteComponentChunk<PersistPosition> chunk)
    {
        Span<PersistPosition> positions = chunk.WriteComponents;
        for (int row = 0; row < chunk.Count; row++)
        {
            positions[row].X++;
            positions[row].Y--;
        }
        Rows += chunk.Count;
    }
}

[EcsComponent("persistence.enabled", IsEnableable = true, PersistenceId = 1001, PersistEnableState = true)]
internal struct PersistEnabled : IComponent<PersistEnabled> { public int Value { get; set; } }

[EcsComponent("persistence.membership", PersistenceId = 1002, PersistenceMode = ComponentPersistenceMode.MembershipOnly)]
internal struct PersistMembership : IComponent<PersistMembership> { public int RuntimeOnly { get; set; } }

[EcsComponent("persistence.position", PersistenceId = 1003)]
internal struct PersistPosition : IComponent<PersistPosition> { public int X { get; set; } public int Y { get; set; } }

[EcsComponent("persistence.tag", IsTag = true, PersistenceId = 1004, PersistenceMode = ComponentPersistenceMode.MembershipOnly)]
internal struct PersistTag : IComponent<PersistTag> { }

[EcsComponent("persistence.target", PersistenceId = 1005)]
internal struct PersistTarget : IComponent<PersistTarget>
{
    [PersistenceEntityReference]
    public Entity Value { get; set; }
}

[EcsComponent("persistence.external-link", PersistenceId = 1006)]
internal struct PersistExternalLink : IComponent<PersistExternalLink>
{
    public PersistentEntityKey Value { get; set; }
}

[EcsSchema(typeof(PersistEnabled), typeof(PersistExternalLink), typeof(PersistMembership), typeof(PersistPosition), typeof(PersistTag), typeof(PersistTarget))]
internal static partial class PersistenceTestSchema { }

[EcsComponent("migration.left", PersistenceId = 2101)]
internal struct OldLeft : IComponent<OldLeft> { public int Value { get; set; } }
[EcsComponent("migration.right", PersistenceId = 2102)]
internal struct OldRight : IComponent<OldRight> { public int Value { get; set; } }
[EcsSchema(typeof(OldLeft), typeof(OldRight))]
internal static partial class PersistenceOldShapeSchema { }

[EcsComponent("migration.combined", PersistenceId = 2201)]
internal struct NewCombined : IComponent<NewCombined> { public int Value { get; set; } }
[EcsSchema(typeof(NewCombined))]
internal static partial class PersistenceNewShapeSchema { }

internal sealed class CombineShapeMigrator : IPersistenceShapeMigrator
{
    public void Migrate(PersistenceShapeMigrationContext context)
    {
        if (!context.TryGetSavedColumn(2101, out PersistenceSavedColumn left) ||
            !context.TryGetSavedColumn(2102, out PersistenceSavedColumn right))
            throw new InvalidOperationException("Expected old shape columns.");
        for (int row = 0; row < context.Entities.Length; row++)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(left.GetEncodedValue(row)) +
                BinaryPrimitives.ReadInt32LittleEndian(right.GetEncodedValue(row));
            context.Write(row, new NewCombined { Value = value });
        }
    }
}

[EcsComponent("derived.base", PersistenceId = 2301)]
internal struct DerivedBase : IComponent<DerivedBase> { public int Value { get; set; } }
[EcsComponent("derived.double", PersistenceId = 2302, PersistenceMode = ComponentPersistenceMode.Derived)]
internal struct DerivedDouble : IComponent<DerivedDouble> { public int Value { get; set; } }
[EcsSchema(typeof(DerivedBase), typeof(DerivedDouble))]
internal static partial class PersistenceDerivedSchema { }

internal sealed class DoubleReconstructor : IPersistenceDerivedReconstructor
{
    public void Reconstruct(PersistenceReconstructionContext context)
    {
        for (int row = 0; row < context.Entities.Length; row++)
        {
            DerivedBase value = context.Read<DerivedBase>(row);
            context.Write(row, new DerivedDouble { Value = value.Value * 2 });
        }
    }
}

internal sealed class PersistentCounterRuntime : IRangeSystemRuntime<EmptyPersistenceJob>, IPersistentSystemState
{
    private readonly bool _required;
    internal PersistentCounterRuntime(bool required = true) => _required = required;
    internal int Value;
    public int PersistenceStateByteCount => sizeof(int);
    public bool PersistenceStateRequired => _required;
    public int GetRangeCount(in SystemPrepareContext context) => 0;
    public EmptyPersistenceJob Prepare(in SystemPrepareContext context) => default;
    public void WritePersistenceState(Span<byte> destination) => BinaryPrimitives.WriteInt32LittleEndian(destination, Value);
    public void ReadPersistenceState(ushort savedVersion, ReadOnlySpan<byte> source)
    {
        if (savedVersion != 1 || source.Length != sizeof(int)) throw new PersistenceCompatibilityException("Unsupported counter state.");
        Value = BinaryPrimitives.ReadInt32LittleEndian(source);
    }
}

internal readonly struct EmptyPersistenceJob : IRangeJob
{
    public void Execute(in SystemJobContext context, int start, int count) { }
}

[EcsComponent("migration.converged-wide", PersistenceId = 2401)]
internal struct ConvergedWide : IComponent<ConvergedWide>
{
    public long A { get; set; } public long B { get; set; } public long C { get; set; } public long D { get; set; }
    public long E { get; set; } public long F { get; set; } public long G { get; set; } public long H { get; set; }
}
[EcsSchema(typeof(ConvergedWide))]
internal static partial class PersistenceConvergedSchema { }

internal sealed class FillConvergedMigrator : IPersistenceShapeMigrator
{
    private readonly long _value;
    internal FillConvergedMigrator(long value) => _value = value;
    public void Migrate(PersistenceShapeMigrationContext context)
    {
        for (int row = 0; row < context.Entities.Length; row++)
            context.Write(row, new ConvergedWide { A = _value, H = row });
    }
}

internal struct VersionedValue : IComponent<VersionedValue> { public int Value; }

internal sealed class VersionedValueCodec : IPersistenceComponentCodec<VersionedValue>, IPersistenceBatchComponentCodec<VersionedValue>
{
    public int EncodedSize => sizeof(int);
    public void Encode(in VersionedValue value, Span<byte> destination, PersistenceWriteContext context) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Value);
    public void Decode(ReadOnlySpan<byte> source, ref VersionedValue value, PersistenceReadContext context) =>
        value.Value = BinaryPrimitives.ReadInt32LittleEndian(source);
    public void EncodeBatch(ReadOnlySpan<VersionedValue> values, Span<byte> destination, PersistenceWriteContext context)
    {
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(destination[(i * 4)..], values[i].Value);
    }
    public void DecodeBatch(ReadOnlySpan<byte> source, Span<VersionedValue> values, PersistenceReadContext context)
    {
        for (int i = 0; i < values.Length; i++) values[i].Value = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]);
    }
}

internal sealed class VersionedValueV1Reader : IPersistenceComponentReader<VersionedValue>
{
    public int EncodedSize => sizeof(int);
    public void Decode(ReadOnlySpan<byte> source, ref VersionedValue value, PersistenceReadContext context) =>
        value.Value = checked(BinaryPrimitives.ReadInt32LittleEndian(source) * 10);
}

[EcsComponent("migration.old-enable", IsEnableable = true, PersistenceId = 2601, PersistEnableState = true)]
internal struct OldEnable : IComponent<OldEnable> { public int Value { get; set; } }
[EcsSchema(typeof(OldEnable))]
internal static partial class PersistenceOldEnableSchema { }

[EcsComponent("migration.new-enable", IsEnableable = true, PersistenceId = 2602, PersistEnableState = true)]
internal struct NewEnable : IComponent<NewEnable> { public int Value { get; set; } }
[EcsSchema(typeof(NewEnable))]
internal static partial class PersistenceNewEnableSchema { }

internal sealed class EnablePlaneMigrator : IPersistenceShapeMigrator
{
    public void Migrate(PersistenceShapeMigrationContext context)
    {
        if (!context.TryGetSavedEnablePlane(2601, out PersistenceSavedEnablePlane source))
            throw new InvalidOperationException("Expected saved enable plane.");
        for (int row = 0; row < context.Entities.Length; row++)
            context.SetEnabled<NewEnable>(row, source.IsEnabled(row));
    }
}
