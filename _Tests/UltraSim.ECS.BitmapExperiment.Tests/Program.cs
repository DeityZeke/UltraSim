using System.Runtime.InteropServices;
using UltraSim.ECS.BitmapExperiment;

var tests = new (string Name, Action Execute)[]
{
    (nameof(EntityLayout_IsEightBytes), EntityLayout_IsEightBytes),
    (nameof(NonMultipleCapacity_MasksInvalidTailBits), NonMultipleCapacity_MasksInvalidTailBits),
    (nameof(DenseRemoval_RelocatesDonorAndPreservesValues), DenseRemoval_RelocatesDonorAndPreservesValues),
    (nameof(BitmapRemoval_LeavesStableSurvivorRows), BitmapRemoval_LeavesStableSurvivorRows),
    (nameof(BitmapReuse_OverwritesEveryColumn), BitmapReuse_OverwritesEveryColumn),
    (nameof(BarrierLocal_CompactsOnlyLeftoverHoles), BarrierLocal_CompactsOnlyLeftoverHoles),
    (nameof(Adaptive_RespectsDensityAndPartialWordPolicy), Adaptive_RespectsDensityAndPartialWordPolicy),
    (nameof(TopologyBulk_PrefersEmptyWords), TopologyBulk_PrefersEmptyWords),
    (nameof(Traversal_NeverVisitsDeadRows), Traversal_NeverVisitsDeadRows),
    (nameof(Compaction_PreservesCanonicalState), Compaction_PreservesCanonicalState),
    (nameof(Transition_PreservesRetainedAndDefaultsAddedColumns), Transition_PreservesRetainedAndDefaultsAddedColumns),
    (nameof(StaleAndDuplicateDestroy_AreRejected), StaleAndDuplicateDestroy_AreRejected),
    (nameof(CapacityFailure_IsTransactionalAndReusable), CapacityFailure_IsTransactionalAndReusable),
    (nameof(FullDestinationTransition_IsTransactional), FullDestinationTransition_IsTransactional),
    (nameof(Reset_ReinitializesEveryPublishedColumn), Reset_ReinitializesEveryPublishedColumn),
    (nameof(WorstCaseCompaction_FitsBoundedScratch), WorstCaseCompaction_FitsBoundedScratch),
    (nameof(RandomizedVariants_ProduceCanonicalLogicalState), RandomizedVariants_ProduceCanonicalLogicalState),
    (nameof(WarmedBitmapChurn_AllocatesZeroManagedBytes), WarmedBitmapChurn_AllocatesZeroManagedBytes),
};

foreach ((string name, Action execute) in tests)
{
    execute();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} bitmap experiment tests passed.");

static void EntityLayout_IsEightBytes() => Equal(8, Marshal.SizeOf<PrototypeEntity>());

static void NonMultipleCapacity_MasksInvalidTailBits()
{
    using var world = new PrototypeWorld(
        PrototypeStorageKind.BitmapPersistent,
        entityCapacity: 65,
        storeCapacity: 65,
        2);
    var entities = new PrototypeEntity[65];
    for (int index = 0; index < entities.Length; index++)
        True(world.TryCreate(0, (ulong)index, out entities[index]));
    False(world.TryCreate(0, 99, out _));
    Equal(65, world.AliveCount);
    Equal(65, world.GetTopology(0).HighWaterRows);
}

static void DenseRemoval_RelocatesDonorAndPreservesValues()
{
    using var world = CreateWorld(PrototypeStorageKind.DensePacked, 128, 4);
    PrototypeEntity[] entities = Fill(world, 0, 128);
    True(world.TryRead(entities[^1], 3, out ulong donorValue));
    True(world.TryDestroy(entities[7]));
    True(world.TryRead(entities[^1], 3, out ulong actual));
    Equal(donorValue, actual);
    Equal(1L, world.GetTopology(0).RowRelocations);
}

static void BitmapRemoval_LeavesStableSurvivorRows()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapPersistent, 128, 4);
    PrototypeEntity[] entities = Fill(world, 0, 128);
    True(world.TryDestroy(entities[7]));
    Equal(0L, world.GetTopology(0).RowRelocations);
    for (int index = 0; index < entities.Length; index++)
    {
        if (index == 7)
            continue;
        True(world.TryRead(entities[index], 0, out ulong value));
        Equal(Expected((ulong)index, 0), value);
    }
}

static void BitmapReuse_OverwritesEveryColumn()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapPersistent, 64, 16);
    PrototypeEntity[] entities = Fill(world, 0, 64);
    True(world.TryDestroy(entities[17]));
    True(world.TryCreate(0, 9_999, out PrototypeEntity replacement));
    Equal(1L, world.GetTopology(0).HolesReused);
    for (int column = 0; column < 16; column++)
    {
        True(world.TryRead(replacement, column, out ulong value));
        Equal(Expected(9_999, column), value);
    }
}

static void BarrierLocal_CompactsOnlyLeftoverHoles()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapBarrierLocal, 256, 8);
    PrototypeEntity[] entities = Fill(world, 0, 256);
    for (int index = 0; index < 64; index++)
        True(world.TryDestroy(entities[index * 3]));
    for (int index = 0; index < 32; index++)
        True(world.TryCreate(0, (ulong)(10_000 + index), out _));
    Equal(32, world.GetTopology(0).HoleCount);
    int moved = world.EndStructuralBarrier();
    True(moved > 0);
    Equal(0, world.GetTopology(0).HoleCount);
    True(world.GetStore(0).IsDense);
}

static void Adaptive_RespectsDensityAndPartialWordPolicy()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapAdaptive, 512, 4);
    PrototypeEntity[] entities = Fill(world, 0, 512);
    for (int index = 0; index < 16; index++)
        True(world.TryDestroy(entities[index * 31]));
    Equal(0, world.EndStructuralBarrier(adaptiveMinimumDensity: 0.50, adaptiveMaximumPartialWords: 64));
    True(world.GetTopology(0).HoleCount != 0);
    True(world.EndStructuralBarrier(adaptiveMinimumDensity: 0.99, adaptiveMaximumPartialWords: 1) > 0);
    Equal(0, world.GetTopology(0).HoleCount);
}

static void TopologyBulk_PrefersEmptyWords()
{
    using var topology = CreateWorld(PrototypeStorageKind.BitmapTopologyAware, 256, 2);
    PrototypeEntity[] entities = Fill(topology, 0, 128);
    True(topology.TryDestroy(entities[1]));
    True(topology.TryDestroy(entities[65]));
    topology.BeginBulk(0);
    for (int index = 0; index < 64; index++)
        True(topology.TryCreate(0, (ulong)(20_000 + index), out _, bulk: true));
    topology.EndBulk(0);
    PrototypeTopology state = topology.GetTopology(0);
    Equal(0L, state.HolesReused);
    Equal(1, state.FullWords);
    Equal(2, state.PartialWords);
}

static void Traversal_NeverVisitsDeadRows()
{
    foreach (PrototypeStorageKind kind in Enum.GetValues<PrototypeStorageKind>())
    {
        using var world = CreateWorld(kind, 1_024, 4);
        PrototypeEntity[] entities = Fill(world, 0, 1_024);
        for (int index = 0; index < entities.Length; index += 5)
            True(world.TryDestroy(entities[index]));
        if (kind == PrototypeStorageKind.BitmapBarrierLocal)
            world.EndStructuralBarrier();
        _ = world.Traverse(0, 2, out int visited);
        Equal(world.AliveCount, visited);
    }
}

static void Compaction_PreservesCanonicalState()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapBarrierLocal, 1_024, 16);
    PrototypeEntity[] entities = Fill(world, 0, 1_024);
    for (int index = 7; index < entities.Length; index += 9)
        True(world.TryDestroy(entities[index]));
    ulong before = world.CaptureCanonicalHash();
    world.EndStructuralBarrier();
    Equal(before, world.CaptureCanonicalHash());
}

static void Transition_PreservesRetainedAndDefaultsAddedColumns()
{
    using var world = new PrototypeWorld(
        PrototypeStorageKind.BitmapTopologyAware,
        entityCapacity: 512,
        storeCapacity: 512,
        4,
        8);
    PrototypeEntity[] entities = Fill(world, 0, 256);
    world.BeginBulk(1);
    foreach (PrototypeEntity entity in entities)
        True(world.TryTransition(entity, 1, retainedColumns: 4, bulk: true));
    world.EndBulk(1);
    foreach (PrototypeEntity entity in entities)
    {
        for (int column = 0; column < 4; column++)
        {
            True(world.TryRead(entity, column, out ulong value));
            Equal(Expected((ulong)entity.Index, column), value);
        }
        for (int column = 4; column < 8; column++)
        {
            True(world.TryRead(entity, column, out ulong value));
            Equal(Expected(0, column), value);
        }
    }
}

static void StaleAndDuplicateDestroy_AreRejected()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapPersistent, 16, 2);
    True(world.TryCreate(0, 17, out PrototypeEntity entity));
    True(world.TryDestroy(entity));
    False(world.TryDestroy(entity));
    True(world.TryCreate(0, 18, out PrototypeEntity replacement));
    Equal(entity.Index, replacement.Index);
    True(entity.Generation != replacement.Generation);
    False(world.IsAlive(entity));
    True(world.IsAlive(replacement));
}

static void CapacityFailure_IsTransactionalAndReusable()
{
    using var world = new PrototypeWorld(
        PrototypeStorageKind.BitmapPersistent,
        entityCapacity: 8,
        storeCapacity: 4,
        8);
    PrototypeEntity[] entities = Fill(world, 0, 4);
    ulong before = world.CaptureCanonicalHash();
    False(world.TryCreate(0, 123_456, out PrototypeEntity failed));
    False(failed.IsValid);
    Equal(before, world.CaptureCanonicalHash());
    True(world.TryDestroy(entities[1]));
    True(world.TryCreate(0, 123_456, out PrototypeEntity replacement));
    True(world.IsAlive(replacement));
    for (int column = 0; column < 8; column++)
    {
        True(world.TryRead(replacement, column, out ulong value));
        Equal(Expected(123_456, column), value);
    }
}

static void FullDestinationTransition_IsTransactional()
{
    using var world = new PrototypeWorld(
        PrototypeStorageKind.BitmapTopologyAware,
        entityCapacity: 8,
        storeCapacity: 4,
        4,
        8);
    PrototypeEntity[] source = Fill(world, 0, 4);
    _ = Fill(world, 1, 4);
    ulong before = world.CaptureCanonicalHash();
    False(world.TryTransition(source[2], 1, retainedColumns: 4, bulk: true));
    Equal(before, world.CaptureCanonicalHash());
    True(world.TryRead(source[2], 3, out ulong value));
    Equal(Expected(2, 3), value);
}

static void Reset_ReinitializesEveryPublishedColumn()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapPersistent, 128, 32);
    PrototypeEntity[] old = Fill(world, 0, 128);
    for (int column = 0; column < 32; column++)
        True(world.TryWrite(old[17], column, ulong.MaxValue));
    world.Reset();
    True(world.TryCreate(0, 9_876, out PrototypeEntity current));
    for (int column = 0; column < 32; column++)
    {
        True(world.TryRead(current, column, out ulong value));
        Equal(Expected(9_876, column), value);
    }
}

static void WorstCaseCompaction_FitsBoundedScratch()
{
    const int count = 4_096;
    using var world = CreateWorld(PrototypeStorageKind.BitmapBarrierLocal, count, 64);
    PrototypeEntity[] entities = Fill(world, 0, count);
    for (int index = 0; index < count; index += 2)
        True(world.TryDestroy(entities[index]));
    int relocated = world.EndStructuralBarrier();
    True(relocated <= count / 2);
    Equal(0, world.GetTopology(0).HoleCount);
    Equal(count / 2, world.AliveCount);
}

static void RandomizedVariants_ProduceCanonicalLogicalState()
{
    ulong? expected = null;
    foreach (PrototypeStorageKind kind in Enum.GetValues<PrototypeStorageKind>())
    {
        using var world = new PrototypeWorld(kind, 2_048, 2_048, 8, 12);
        var entities = new PrototypeEntity[1_024];
        for (int index = 0; index < entities.Length; index++)
            True(world.TryCreate(0, (ulong)index, out entities[index]));
        for (int index = 0; index < entities.Length; index += 3)
            True(world.TryDestroy(entities[index]));
        for (int index = 1; index < entities.Length; index += 7)
        {
            if (world.IsAlive(entities[index]))
                True(world.TryTransition(entities[index], 1, retainedColumns: 8, bulk: false));
        }
        for (int index = 0; index < 300; index++)
            True(world.TryCreate(index % 2, (ulong)(50_000 + index), out _));
        world.EndStructuralBarrier(adaptiveMinimumDensity: 0.95, adaptiveMaximumPartialWords: 2);
        ulong actual = world.CaptureCanonicalHash();
        if (expected.HasValue)
            Equal(expected.Value, actual);
        else
            expected = actual;
    }
}

static void WarmedBitmapChurn_AllocatesZeroManagedBytes()
{
    using var world = CreateWorld(PrototypeStorageKind.BitmapPersistent, 10_000, 8);
    PrototypeEntity[] entities = Fill(world, 0, 10_000);
    for (int index = 0; index < 1_000; index++)
    {
        True(world.TryDestroy(entities[index]));
        True(world.TryCreate(0, (ulong)(100_000 + index), out entities[index]));
    }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 1_000; index++)
    {
        True(world.TryDestroy(entities[index]));
        True(world.TryCreate(0, (ulong)(200_000 + index), out entities[index]));
    }
    _ = world.Traverse(0, 2, out int visited);
    long after = GC.GetAllocatedBytesForCurrentThread();
    Equal(10_000, visited);
    Equal(0L, after - before);
}

static PrototypeWorld CreateWorld(PrototypeStorageKind kind, int capacity, int columns) =>
    new(kind, capacity, capacity, columns);

static PrototypeEntity[] Fill(PrototypeWorld world, int store, int count)
{
    var entities = new PrototypeEntity[count];
    for (int index = 0; index < count; index++)
        True(world.TryCreate(store, (ulong)index, out entities[index]));
    return entities;
}

static ulong Expected(ulong seed, int column) =>
    unchecked(seed + ((ulong)(column + 1) * 0x9E37_79B9_7F4A_7C15UL));

static void True(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Expected true.");
}

static void False(bool condition) => True(!condition);

static void Equal<T>(T expected, T actual)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}
