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

        Console.WriteLine($"All {_passed} ECS Next primitive tests passed.");
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
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void NotEqual<T>(T unexpected, T actual)
        where T : IEquatable<T>
    {
        if (unexpected.Equals(actual))
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
