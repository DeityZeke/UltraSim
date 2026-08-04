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
        Assert.Equal(9000L, capacity.EstimatedMetadataBytes);
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
