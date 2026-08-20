using System.Runtime.InteropServices;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

var tests = new (string Name, Action Body)[]
{
    ("FrozenProfileAndPayloadWidths", FrozenProfileAndPayloadWidths),
    ("FloorDivisionOwnsNegativeBoundaries", FloorDivisionOwnsNegativeBoundaries),
    ("AllCrossingDirectionsAndCornersNormalize", AllCrossingDirectionsAndCornersNormalize),
    ("GlobalLocalRoundTripsAcrossFullProfile", GlobalLocalRoundTripsAcrossFullProfile),
    ("NormalizationRejectsUnaddressableChunks", NormalizationRejectsUnaddressableChunks),
    ("MalformedValuesRejectWithoutSilentNormalization", MalformedValuesRejectWithoutSilentNormalization),
    ("ChunkIdentityOrderingAndHashAreCanonical", ChunkIdentityOrderingAndHashAreCanonical),
    ("SpaceIdentityCannotAliasChunkCoordinates", SpaceIdentityCannotAliasChunkCoordinates),
    ("HalfOpenBoundsAndCircleDistanceAreExact", HalfOpenBoundsAndCircleDistanceAreExact),
    ("WideDistancePromotesBeforeSubtraction", WideDistancePromotesBeforeSubtraction),
    ("FloatImportUsesNearestMillimeterAwayFromZero", FloatImportUsesNearestMillimeterAwayFromZero),
    ("FloatImportRejectsNonfiniteAndOutOfProfile", FloatImportRejectsNonfiniteAndOutOfProfile),
    ("TerrainCellsUseIndependentFiveHundredMillimeterGrid", TerrainCellsUseIndependentFiveHundredMillimeterGrid),
    ("DynamicPositionIsNotPlacementOrTerrainSnapped", DynamicPositionIsNotPlacementOrTerrainSnapped),
    ("TerrainFoundationLayoutIsFrozen", TerrainFoundationLayoutIsFrozen),
    ("TerrainHeightQuantizationIsDeterministic", TerrainHeightQuantizationIsDeterministic),
    ("TerrainHeightPlaneEncodingIsTransactional", TerrainHeightPlaneEncodingIsTransactional),
    ("TerrainSharedEdgesAreBitExact", TerrainSharedEdgesAreBitExact),
    ("TerrainHeightCodecWarmedAllocationZero", TerrainHeightCodecWarmedAllocationZero),
    ("WarmedCoordinateMachineAllocatesZero", WarmedCoordinateMachineAllocatesZero),
    ("PositionComponentIsPersistedCanonicalAuthority", PositionComponentIsPersistedCanonicalAuthority),
    ("RuntimeCommitsFinalPositionAfterCompleteEcsStep", RuntimeCommitsFinalPositionAfterCompleteEcsStep),
    ("ComponentRemovalReadditionAndSlotReuseConverge", ComponentRemovalReadditionAndSlotReuseConverge),
    ("SaturatedMigrationMergesIntoExistingDestination", SaturatedMigrationMergesIntoExistingDestination),
    ("SaturatedMigrationRekeysSingletonForNewDestination", SaturatedMigrationRekeysSingletonForNewDestination),
    ("SpatialIndexMatchesReferenceModelUnderBoundedChurn", SpatialIndexMatchesReferenceModelUnderBoundedChurn),
    ("LifecycleOverrunRebuildsFromCurrentTruth", LifecycleOverrunRebuildsFromCurrentTruth),
    ("FailedRebuildPreservesPublishedSpatialIndex", FailedRebuildPreservesPublishedSpatialIndex),
    ("GenerationReplacementPublishesFreshDerivedIndex", GenerationReplacementPublishesFreshDerivedIndex),
    ("MillionSameAndCrossChunkMovesAllocateZero", MillionSameAndCrossChunkMovesAllocateZero),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Terrain T0.0/T0.1/T0.2 world tests passed.");

static void FrozenProfileAndPayloadWidths()
{
    Equal(1_000, GameWorldSpatialProfile.UnitsPerMeter);
    Equal(1, GameWorldSpatialProfile.AuthoritativePositionQuantumUnits);
    Equal(16_000, GameWorldSpatialProfile.WorldChunkEdgeUnits);
    Equal(500, GameWorldSpatialProfile.TerrainCellEdgeUnits);
    Equal(32, GameWorldSpatialProfile.TerrainCellsPerChunkEdge);
    Equal(100, GameWorldSpatialProfile.PlacementSnapUnits);
    Equal(8, Marshal.SizeOf<SpatialSpaceId>());
    Equal(8, Marshal.SizeOf<SpatialChunkCoord>());
    Equal(16, Marshal.SizeOf<SpatialChunkKey>());
    Equal(32, Marshal.SizeOf<SpatialBounds>());
    Equal(8, Marshal.SizeOf<WorldLocalPosition>());
    Equal(16, Marshal.SizeOf<WorldPosition>());
    Equal(16, Marshal.SizeOf<WorldPositionComponent>());
    Equal(2, Marshal.SizeOf<TerrainCellCoord>());
    Equal(2, Marshal.SizeOf<TerrainVertexCoord>());
    Equal(2, Marshal.SizeOf<TerrainHeight>());
}

static void TerrainFoundationLayoutIsFrozen()
{
    Equal(32, TerrainChunkFormat.MaterialCellEdge);
    Equal(1_024, TerrainChunkFormat.MaterialCellCount);
    Equal(33, TerrainChunkFormat.HeightVertexEdge);
    Equal(1_089, TerrainChunkFormat.HeightVertexCount);
    Equal(2_048, TerrainChunkFormat.TriangleCount);
    Equal(6_144, TerrainChunkFormat.TriangleIndexCount);
    Equal(10, TerrainChunkFormat.HeightQuantumMillimeters);
    Equal(-327_680, TerrainChunkFormat.MinimumHeightMillimeters);
    Equal(327_670, TerrainChunkFormat.MaximumHeightMillimeters);
    Equal(2_048, TerrainChunkFormat.MaterialPlaneBytes);
    Equal(2_178, TerrainChunkFormat.HeightPlaneBytes);
    Equal(4_226, TerrainChunkFormat.FoundationBytes);
    Equal(65, TerrainChunkFormat.DuplicatedBorderVertexCountPerChunk);
    Equal(130, TerrainChunkFormat.DuplicatedBorderBytesPerChunk);

    var maximumCell = new TerrainCellCoord(31, 31);
    var maximumVertex = new TerrainVertexCoord(32, 32);
    Equal(65, TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(1, 2)));
    Equal(67, TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(1, 2)));
    Equal(1_023, TerrainChunkFormat.GetMaterialIndex(maximumCell));
    Equal(1_088, TerrainChunkFormat.GetHeightIndex(maximumVertex));
    Equal(16_000, maximumVertex.LocalXMillimeters);
    Equal(16_000, maximumVertex.LocalZMillimeters);
    Throws<ArgumentOutOfRangeException>(() => _ = new TerrainVertexCoord(33, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new TerrainVertexCoord(0, 33));
}

static void TerrainHeightQuantizationIsDeterministic()
{
    True(TerrainHeight.TryFromMillimeters(0, out TerrainHeight zero));
    Equal((short)0, zero.Units);
    True(TerrainHeight.TryFromMillimeters(4, out TerrainHeight positiveBelow));
    Equal(0, positiveBelow.Millimeters);
    True(TerrainHeight.TryFromMillimeters(-4, out TerrainHeight negativeBelow));
    Equal(0, negativeBelow.Millimeters);
    True(TerrainHeight.TryFromMillimeters(5, out TerrainHeight positiveTie));
    Equal(10, positiveTie.Millimeters);
    True(TerrainHeight.TryFromMillimeters(-5, out TerrainHeight negativeTie));
    Equal(-10, negativeTie.Millimeters);
    True(TerrainHeight.TryFromMillimeters(TerrainChunkFormat.MinimumHeightMillimeters, out TerrainHeight minimum));
    Equal(short.MinValue, minimum.Units);
    True(TerrainHeight.TryFromMillimeters(TerrainChunkFormat.MaximumHeightMillimeters, out TerrainHeight maximum));
    Equal(short.MaxValue, maximum.Units);
    False(TerrainHeight.TryFromMillimeters(TerrainChunkFormat.MinimumHeightMillimeters - 1, out _));
    False(TerrainHeight.TryFromMillimeters(TerrainChunkFormat.MaximumHeightMillimeters + 1, out _));
    for (int units = short.MinValue; units <= short.MaxValue; units++)
    {
        int millimeters = units * TerrainChunkFormat.HeightQuantumMillimeters;
        True(TerrainHeight.TryFromMillimeters(millimeters, out TerrainHeight roundTrip));
        Equal(checked((short)units), roundTrip.Units);
    }
    for (int millimeters = -100; millimeters <= 100; millimeters++)
    {
        True(TerrainHeight.TryFromMillimeters(millimeters, out TerrainHeight quantized));
        int expected = checked((int)Math.Round(
            millimeters / (double)TerrainChunkFormat.HeightQuantumMillimeters,
            MidpointRounding.AwayFromZero)) * TerrainChunkFormat.HeightQuantumMillimeters;
        Equal(expected, quantized.Millimeters);
    }
}

static void TerrainHeightPlaneEncodingIsTransactional()
{
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    var encoded = Enumerable.Repeat(new TerrainHeight(123), TerrainChunkFormat.HeightVertexCount).ToArray();
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryEncodePlane(source.AsSpan(1), encoded, out _, out _));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryEncodePlane(source, encoded.AsSpan(1), out _, out _));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.DecodePlane(encoded.AsSpan(1), source));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.DecodePlane(encoded, source.AsSpan(1)));
    var overlapping = new byte[TerrainChunkFormat.HeightVertexCount * sizeof(int)];
    Throws<ArgumentException>(() => EncodeOverlappingTerrainPlane(overlapping));
    Throws<ArgumentException>(() => DecodeOverlappingTerrainPlane(overlapping));
    for (int index = 0; index < source.Length; index++)
        source[index] = checked((index - 544) * 37);
    source[700] = TerrainChunkFormat.MaximumHeightMillimeters + 1;
    False(TerrainHeightEncoding.TryEncodePlane(source, encoded, out int invalid, out _));
    Equal(700, invalid);
    True(encoded.All(static value => value.Units == 123));

    source[700] = 12_345;
    True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out invalid,
        out TerrainHeightEncodingSummary summary));
    Equal(-1, invalid);
    Equal(5, summary.MaximumAbsoluteErrorMillimeters);
    True(summary.TotalAbsoluteErrorMillimeters > 0);
    var decoded = new int[TerrainChunkFormat.HeightVertexCount];
    TerrainHeightEncoding.DecodePlane(encoded, decoded);
    for (int index = 0; index < source.Length; index++)
        True(Math.Abs(source[index] - decoded[index]) <= 5);
}

static void EncodeOverlappingTerrainPlane(byte[] storage)
{
    ReadOnlySpan<int> source = MemoryMarshal.Cast<byte, int>(storage);
    Span<TerrainHeight> destination = MemoryMarshal.Cast<byte, TerrainHeight>(storage)
        [..TerrainChunkFormat.HeightVertexCount];
    TerrainHeightEncoding.TryEncodePlane(source, destination, out _, out _);
}

static void DecodeOverlappingTerrainPlane(byte[] storage)
{
    ReadOnlySpan<TerrainHeight> source = MemoryMarshal.Cast<byte, TerrainHeight>(storage)
        [..TerrainChunkFormat.HeightVertexCount];
    Span<int> destination = MemoryMarshal.Cast<byte, int>(storage);
    TerrainHeightEncoding.DecodePlane(source, destination);
}

static void TerrainSharedEdgesAreBitExact()
{
    TerrainHeight[] left = EncodeDeterministicTerrainPlane(globalVertexOffsetX: 0);
    TerrainHeight[] right = EncodeDeterministicTerrainPlane(
        globalVertexOffsetX: GameWorldSpatialProfile.TerrainCellsPerChunkEdge);
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        int leftIndex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, z));
        int rightIndex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, z));
        Equal(left[leftIndex], right[rightIndex]);
    }
}

static void TerrainHeightCodecWarmedAllocationZero()
{
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    var encoded = new TerrainHeight[TerrainChunkFormat.HeightVertexCount];
    var decoded = new int[TerrainChunkFormat.HeightVertexCount];
    for (int index = 0; index < source.Length; index++) source[index] = index * 17 - 9_000;
    True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out _, out _));
    TerrainHeightEncoding.DecodePlane(encoded, decoded);
    long before = GC.GetAllocatedBytesForCurrentThread();
    long checksum = 0;
    for (int iteration = 0; iteration < 10_000; iteration++)
    {
        True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out _, out _));
        TerrainHeightEncoding.DecodePlane(encoded, decoded);
        checksum += decoded[iteration % decoded.Length];
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
}

static TerrainHeight[] EncodeDeterministicTerrainPlane(int globalVertexOffsetX)
{
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
        {
            int globalX = globalVertexOffsetX + x;
            source[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z))] =
                checked((globalX * 137 + z * 251 - 4_000) % 30_001);
        }
    }
    var encoded = new TerrainHeight[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out _, out _));
    return encoded;
}

static void PositionComponentIsPersistedCanonicalAuthority()
{
    ComponentSchema schema = GameSchema.CreateSchema();
    Equal(8, schema.ComponentCount);
    ComponentId position = GameSchema.GetComponentId<WorldPositionComponent>();
    True(position.IsValid);
    PersistenceCatalog catalog = GameSchema.CreatePersistenceCatalog();
    Equal(schema.Id, catalog.Schema.Id);

    var source = new WorldPosition(-7, 11, 15_999, 3, -400);
    var component = new WorldPositionComponent(source);
    True(component.TryGetValue(out WorldPosition restored));
    Equal(source, restored);
    component.LocalXMillimeters = 16_000;
    False(component.TryGetValue(out _));
}

static void RuntimeCommitsFinalPositionAfterCompleteEcsStep()
{
    var composition = new UltraSimGameComposition(runtimeProfile: GameRuntimeProfile.Local);
    EcsWorldOptions options = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x8101)),
        GameRuntimeProfile.Local);
    using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(options, WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();

    Entity entity = default;
    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
    {
        if (!world.TryInstantiate(game.Content.Character, out entity)) return false;
        for (int index = 0; index < 10; index++)
        {
            var value = new WorldPosition(0, 0, checked((ushort)(100 + index)), 200, index);
            if (!game.WorldState.TrySetPosition(world, entity, value)) return false;
        }
        return true;
    }, out bool written));
    True(written);
    runtime.Tick();

    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
    {
        True(game.WorldState.Index.TryGetHome(entity, out SpatialChunkKey home));
        Equal(new SpatialChunkCoord(0, 0), home.Coord);
        GameWorldStateSnapshot snapshot = game.WorldState.GetSnapshot();
        Equal(1, snapshot.Index.EntityCount);
        Equal(1L, snapshot.ReconciledEntities);
        return true;
    }, out bool checkedFirst));
    True(checkedFirst);

    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
        game.WorldState.TrySetPosition(world, entity, new WorldPosition(0, 0, 500, 600, 0)), out bool same));
    True(same);
    runtime.Tick();
    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
        game.WorldState.TrySetPosition(world, entity, new WorldPosition(-1, -1, 15_999, 15_999, 0)), out bool crossed));
    True(crossed);
    runtime.Tick();

    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
    {
        True(game.WorldState.Index.TryGetHome(entity, out SpatialChunkKey home));
        Equal(new SpatialChunkCoord(-1, -1), home.Coord);
        WorldSpatialIndexSnapshot snapshot = game.WorldState.Index.GetSnapshot();
        Equal(1L, snapshot.SameChunkObservations);
        Equal(1L, snapshot.CrossChunkMigrations);
        return true;
    }, out bool checkedFinal));
    True(checkedFinal);
}

static void ComponentRemovalReadditionAndSlotReuseConverge()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 64);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity first));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(2, 3, 1, 2, 3)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    True(fixture.State.Index.TryGetHome(first, out _));

    True(fixture.World.TryTransition(first, fixture.RemovePosition));
    fixture.State.CommitAfterEcsStep(fixture.World);
    False(fixture.State.Index.TryGetHome(first, out _));

    True(fixture.World.TryTransition(first, fixture.AddPosition));
    fixture.State.CommitAfterEcsStep(fixture.World);
    True(fixture.World.TryGetEntity(first.Index, out Entity readded));
    True(fixture.State.Index.TryGetHome(readded, out SpatialChunkKey origin));
    Equal(new SpatialChunkCoord(0, 0), origin.Coord);

    True(fixture.World.TryDestroy(readded));
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity replacement));
    Equal(readded.Index, replacement.Index);
    True(replacement.Generation > readded.Generation);
    True(fixture.State.TrySetPosition(fixture.World, replacement, new WorldPosition(-4, 8, 9, 10, 11)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    False(fixture.State.Index.TryGetHome(readded, out _));
    True(fixture.State.Index.TryGetHome(replacement, out SpatialChunkKey replacementHome));
    Equal(new SpatialChunkCoord(-4, 8), replacementHome.Coord);
}

static void LifecycleOverrunRebuildsFromCurrentTruth()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 4);
    for (int index = 0; index < 8; index++)
    {
        True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity transient));
        True(fixture.World.TryDestroy(transient));
    }
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity survivor));
    True(fixture.State.TrySetPosition(fixture.World, survivor, new WorldPosition(-2, -3, 4, 5, 6)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    GameWorldStateSnapshot snapshot = fixture.State.GetSnapshot();
    Equal(2L, snapshot.FullRebuildCount);
    Equal(1, snapshot.Index.EntityCount);
    True(fixture.State.Index.TryGetHome(survivor, out SpatialChunkKey home));
    Equal(new SpatialChunkCoord(-2, -3), home.Coord);
}

static void SaturatedMigrationMergesIntoExistingDestination()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 16, entityCapacity: 2);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity first));
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity second));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(0, 0, 0, 0, 0)));
    True(fixture.State.TrySetPosition(fixture.World, second, new WorldPosition(1, 0, 0, 0, 0)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(2, fixture.State.Index.GetSnapshot().ChunkCount);

    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(1, 0, 1, 0, 0)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    WorldSpatialIndexSnapshot snapshot = fixture.State.Index.GetSnapshot();
    Equal(2, snapshot.EntityCount);
    Equal(1, snapshot.ChunkCount);
    var destination = new SpatialChunkKey(GameWorldState.DefaultSpace, 1, 0);
    Span<Entity> members = stackalloc Entity[2];
    Equal(2, fixture.State.Index.CopyMembers(destination, members));
    True(members.Contains(first));
    True(members.Contains(second));
}

static void SaturatedMigrationRekeysSingletonForNewDestination()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 16, entityCapacity: 2);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity first));
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity second));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(0, 0, 0, 0, 0)));
    True(fixture.State.TrySetPosition(fixture.World, second, new WorldPosition(1, 0, 0, 0, 0)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(2, 0, 0, 0, 0)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    WorldSpatialIndexSnapshot snapshot = fixture.State.Index.GetSnapshot();
    Equal(2, snapshot.EntityCount);
    Equal(2, snapshot.ChunkCount);
    True(fixture.State.Index.TryGetHome(first, out SpatialChunkKey firstHome));
    True(fixture.State.Index.TryGetHome(second, out SpatialChunkKey secondHome));
    Equal(new SpatialChunkCoord(2, 0), firstHome.Coord);
    Equal(new SpatialChunkCoord(1, 0), secondHome.Coord);
}

static void SpatialIndexMatchesReferenceModelUnderBoundedChurn()
{
    for (int capacity = 4; capacity <= 16; capacity += 4)
    {
        using RawSpatialFixture fixture = new(lifecycleCapacity: 64, entityCapacity: capacity);
        var live = new List<Entity>(capacity);
        var expected = new Dictionary<Entity, SpatialChunkKey>(capacity);
        var stale = new List<Entity>();
        uint state = unchecked((uint)(0x9E37_79B9u ^ capacity));
        for (int operation = 0; operation < 5_000; operation++)
        {
            state = unchecked(state * 1_664_525u + 1_013_904_223u);
            int choice = (int)(state % 100);
            if (live.Count == 0 || (choice < 28 && live.Count < capacity))
            {
                True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity entity));
                WorldPosition position = RandomPosition(ref state);
                True(fixture.State.TrySetPosition(fixture.World, entity, position));
                live.Add(entity);
                expected.Add(entity, position.ToChunkKey(GameWorldState.DefaultSpace));
            }
            else if (choice < 78)
            {
                int selected = (int)(state % (uint)live.Count);
                Entity entity = live[selected];
                WorldPosition position = RandomPosition(ref state);
                True(fixture.State.TrySetPosition(fixture.World, entity, position));
                expected[entity] = position.ToChunkKey(GameWorldState.DefaultSpace);
            }
            else
            {
                int selected = (int)(state % (uint)live.Count);
                Entity entity = live[selected];
                True(fixture.World.TryDestroy(entity));
                live.RemoveAt(selected);
                expected.Remove(entity);
                stale.Add(entity);
                if (stale.Count > 64)
                    stale.RemoveAt(0);
            }

            fixture.State.CommitAfterEcsStep(fixture.World);
            AssertSpatialModel(fixture.State.Index, expected, stale);
        }
    }
}

static WorldPosition RandomPosition(ref uint state)
{
    state = unchecked(state * 1_664_525u + 1_013_904_223u);
    int chunkX = (int)(state % 9) - 4;
    state = unchecked(state * 1_664_525u + 1_013_904_223u);
    int chunkZ = (int)(state % 9) - 4;
    state = unchecked(state * 1_664_525u + 1_013_904_223u);
    ushort localX = checked((ushort)(state % (uint)GameWorldSpatialProfile.WorldChunkEdgeUnits));
    state = unchecked(state * 1_664_525u + 1_013_904_223u);
    ushort localZ = checked((ushort)(state % (uint)GameWorldSpatialProfile.WorldChunkEdgeUnits));
    return new WorldPosition(chunkX, chunkZ, localX, localZ, unchecked((int)state));
}

static void AssertSpatialModel(
    WorldSpatialIndex index,
    IReadOnlyDictionary<Entity, SpatialChunkKey> expected,
    IReadOnlyList<Entity> stale)
{
    WorldSpatialIndexSnapshot snapshot = index.GetSnapshot();
    Equal(expected.Count, snapshot.EntityCount);
    int expectedChunks = expected.Values.Distinct().Count();
    Equal(expectedChunks, snapshot.ChunkCount);
    foreach ((Entity entity, SpatialChunkKey key) in expected)
    {
        True(index.TryGetHome(entity, out SpatialChunkKey actual));
        Equal(key, actual);
    }
    for (int indexValue = 0; indexValue < stale.Count; indexValue++)
        False(index.TryGetHome(stale[indexValue], out _));

    var copied = new Entity[Math.Max(1, expected.Count)];
    foreach (IGrouping<SpatialChunkKey, KeyValuePair<Entity, SpatialChunkKey>> group in
        expected.GroupBy(static pair => pair.Value))
    {
        int count = index.CopyMembers(group.Key, copied);
        Equal(group.Count(), count);
        var actual = copied.AsSpan(0, count).ToArray();
        Array.Sort(actual, static (left, right) =>
        {
            int slot = left.Index.CompareTo(right.Index);
            return slot != 0 ? slot : left.Generation.CompareTo(right.Generation);
        });
        Entity[] wanted = group.Select(static pair => pair.Key).ToArray();
        Array.Sort(wanted, static (left, right) =>
        {
            int slot = left.Index.CompareTo(right.Index);
            return slot != 0 ? slot : left.Generation.CompareTo(right.Generation);
        });
        True(actual.SequenceEqual(wanted));
    }
}

static void FailedRebuildPreservesPublishedSpatialIndex()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 32);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity entity));
    True(fixture.State.TrySetPosition(fixture.World, entity, new WorldPosition(1, 2, 3, 4, 5)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    WorldSpatialIndex published = fixture.State.Index;
    True(fixture.World.TryWrite(entity, fixture.RawPositionLookup, new WorldPositionComponent
    {
        ChunkX = 9,
        ChunkZ = 9,
        LocalXMillimeters = 16_000,
        LocalZMillimeters = 0,
        YMillimeters = 0,
    }));
    Throws<InvalidOperationException>(() => fixture.State.Rebuild(fixture.World));
    True(ReferenceEquals(published, fixture.State.Index));
    True(published.TryGetHome(entity, out SpatialChunkKey oldHome));
    Equal(new SpatialChunkCoord(1, 2), oldHome.Coord);
}

static void GenerationReplacementPublishesFreshDerivedIndex()
{
    var composition = new UltraSimGameComposition(runtimeProfile: GameRuntimeProfile.Local);
    EcsWorldOptions options = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x8102)),
        GameRuntimeProfile.Local);
    using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(options, WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    ulong oldGeneration = runtime.ActiveGenerationId;

    GameStaging staging = composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x8102)));
    var accounting = new AccountingStore();
    GameGenerationState? candidate = null;
    bool published = false;
    try
    {
        staging.World.Seal();
        staging.World.Initialize();
        staging.World.BeginRun();
        True(staging.World.TryInstantiate(staging.Content.Character, out Entity entity));
        True(staging.WorldState.TrySetPosition(
            staging.World,
            entity,
            new WorldPosition(7, -9, 11, 13, 17)));
        candidate = new GameGenerationState(
            accounting,
            staging.Inventory,
            staging.Content,
            staging.WorldState,
            staging.SimulationLookup,
            staging.Replication);
        True(runtime.Game.TryPublishPreparedGeneration(staging.World, candidate, out ulong newGeneration));
        published = true;
        candidate = null;
        True(newGeneration > oldGeneration);
        True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, generation, game) =>
        {
            Equal(newGeneration, generation);
            True(game.WorldState.Index.TryGetHome(entity, out SpatialChunkKey home));
            Equal(new SpatialChunkCoord(7, -9), home.Coord);
            Equal(1L, game.WorldState.GetSnapshot().FullRebuildCount);
            return true;
        }, out bool verified));
        True(verified);
    }
    finally
    {
        if (!published)
        {
            candidate?.Dispose();
            if (candidate is null)
            {
                accounting.Dispose();
                staging.WorldState.Dispose();
                staging.Inventory.Dispose();
            }
            if (!staging.World.IsDisposed) staging.World.Dispose();
        }
    }
}

static void MillionSameAndCrossChunkMovesAllocateZero()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 32);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity entity));
    True(fixture.State.TrySetPosition(fixture.World, entity, new WorldPosition(0, 0, 0, 0, 0)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    for (int index = 0; index < 1_000; index++)
    {
        int chunk = index & 1;
        True(fixture.State.TrySetPosition(fixture.World, entity,
            new WorldPosition(chunk, -chunk, checked((ushort)(index & 15_999)), 0, index)));
        fixture.State.CommitAfterEcsStep(fixture.World);
    }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 1_000_000; index++)
    {
        int chunk = (index >> 4) & 1;
        True(fixture.State.TrySetPosition(fixture.World, entity,
            new WorldPosition(chunk, -chunk, checked((ushort)(index & 15_999)), 0, index)));
        fixture.State.CommitAfterEcsStep(fixture.World);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    Equal(1, fixture.State.Index.GetSnapshot().EntityCount);
}

static void FloorDivisionOwnsNegativeBoundaries()
{
    (long Global, int Chunk, int Local)[] cases =
    {
        (-32_001, -3, 15_999),
        (-32_000, -2, 0),
        (-16_001, -2, 15_999),
        (-16_000, -1, 0),
        (-15_999, -1, 1),
        (-1, -1, 15_999),
        (0, 0, 0),
        (1, 0, 1),
        (15_999, 0, 15_999),
        (16_000, 1, 0),
        (32_000, 2, 0),
    };

    foreach ((long global, int expectedChunk, int expectedLocal) in cases)
    {
        True(SpatialMath.TryNormalizeAxis(global, 16_000, out int chunk, out int local));
        Equal(expectedChunk, chunk);
        Equal(expectedLocal, local);
    }
}

static void AllCrossingDirectionsAndCornersNormalize()
{
    AssertPosition(15_999, 15_999, 0, 0, 15_999, 15_999);
    AssertPosition(16_000, 15_999, 1, 0, 0, 15_999);
    AssertPosition(15_999, 16_000, 0, 1, 15_999, 0);
    AssertPosition(16_000, 16_000, 1, 1, 0, 0);
    AssertPosition(-1, 0, -1, 0, 15_999, 0);
    AssertPosition(0, -1, 0, -1, 0, 15_999);
    AssertPosition(-1, -1, -1, -1, 15_999, 15_999);
    AssertPosition(-16_000, -16_000, -1, -1, 0, 0);
    AssertPosition(-16_001, -16_001, -2, -2, 15_999, 15_999);
}

static void GlobalLocalRoundTripsAcrossFullProfile()
{
    long[] boundaries =
    {
        GameWorldSpatialProfile.MinimumGlobalAxisUnits,
        GameWorldSpatialProfile.MinimumGlobalAxisUnits + 1,
        -32_001, -32_000, -16_001, -16_000, -1, 0, 1, 15_999, 16_000, 16_001,
        GameWorldSpatialProfile.MaximumGlobalAxisUnits - 1,
        GameWorldSpatialProfile.MaximumGlobalAxisUnits,
    };
    foreach (long x in boundaries)
    foreach (long z in boundaries)
    {
        True(WorldPosition.TryFromGlobalMillimeters(x, 37, z, out WorldPosition position));
        Equal(x, position.GlobalXMillimeters);
        Equal(z, position.GlobalZMillimeters);
        Equal(37, position.YMillimeters);
    }

    ulong state = 0xD1B5_4A32_D192_ED03;
    long range = checked(GameWorldSpatialProfile.MaximumGlobalAxisUnits -
        GameWorldSpatialProfile.MinimumGlobalAxisUnits + 1);
    for (int index = 0; index < 100_000; index++)
    {
        state = unchecked(state * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL);
        long x = GameWorldSpatialProfile.MinimumGlobalAxisUnits + checked((long)(state % (ulong)range));
        state = unchecked(state * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL);
        long z = GameWorldSpatialProfile.MinimumGlobalAxisUnits + checked((long)(state % (ulong)range));
        True(WorldPosition.TryFromGlobalMillimeters(x, unchecked((int)state), z, out WorldPosition position));
        Equal(x, position.GlobalXMillimeters);
        Equal(z, position.GlobalZMillimeters);
    }
}

static void NormalizationRejectsUnaddressableChunks()
{
    False(WorldPosition.TryFromGlobalMillimeters(
        checked(GameWorldSpatialProfile.MinimumGlobalAxisUnits - 1), 0, 0, out WorldPosition below));
    Equal(default, below);
    False(WorldPosition.TryFromGlobalMillimeters(
        checked(GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1), 0, 0, out WorldPosition above));
    Equal(default, above);
    False(SpatialMath.TryNormalizeAxis(long.MinValue, int.MaxValue, out int chunk, out int local));
    Equal(0, chunk);
    Equal(0, local);
}

static void MalformedValuesRejectWithoutSilentNormalization()
{
    Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSpaceId(0));
    Throws<ArgumentOutOfRangeException>(() => _ = new SpatialChunkKey(default, 0, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new SpatialChunkGeometry(0));
    Throws<InvalidOperationException>(() => _ = default(SpatialChunkGeometry).GetBounds(default));
    Throws<ArgumentOutOfRangeException>(() => _ = new SpatialBounds(0, 0, 0, 1));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldLocalPosition(16_000, 0, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldLocalPosition(0, 16_000, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new TerrainCellCoord(32, 0));
    Throws<ArgumentOutOfRangeException>(() => SpatialMath.FloorDivRem(0, 0, out _, out _));
}

static void ChunkIdentityOrderingAndHashAreCanonical()
{
    var space = new SpatialSpaceId(7);
    var a = new SpatialChunkKey(space, -1, 4);
    var equal = new SpatialChunkKey(new SpatialSpaceId(7), -1, 4);
    var b = new SpatialChunkKey(space, -1, 5);
    var c = new SpatialChunkKey(space, 0, -100);
    True(a == equal);
    Equal(a.GetHashCode(), equal.GetHashCode());
    True(a.CompareTo(b) < 0);
    True(b.CompareTo(c) < 0);
    SpatialChunkKey[] values = { c, b, a };
    Array.Sort(values);
    Equal(a, values[0]);
    Equal(b, values[1]);
    Equal(c, values[2]);
}

static void SpaceIdentityCannotAliasChunkCoordinates()
{
    var first = new SpatialChunkKey(new SpatialSpaceId(1), 4, 9);
    var second = new SpatialChunkKey(new SpatialSpaceId(2), 4, 9);
    False(first == second);
    True(first.CompareTo(second) < 0);
    False(first.GetHashCode() == second.GetHashCode());
}

static void HalfOpenBoundsAndCircleDistanceAreExact()
{
    SpatialChunkGeometry geometry = GameWorldSpatialProfile.ChunkGeometry;
    SpatialBounds origin = geometry.GetBounds(new SpatialChunkCoord(0, 0));
    Equal(new SpatialBounds(0, 0, 16_000, 16_000), origin);
    True(origin.Contains(0, 0));
    True(origin.Contains(15_999, 15_999));
    False(origin.Contains(16_000, 0));
    False(origin.Contains(0, 16_000));
    False(origin.Intersects(geometry.GetBounds(new SpatialChunkCoord(1, 0))));
    True(origin.Intersects(new SpatialBounds(15_999, 15_999, 16_001, 16_001)));
    Equal((Int128)0, origin.SquaredDistanceTo(16_000, 8_000));
    Equal((Int128)25_000_000, origin.SquaredDistanceTo(21_000, 8_000));
    True(origin.IntersectsCircle(21_000, 8_000, 5_000));
    False(origin.IntersectsCircle(21_001, 8_000, 5_000));
    Throws<ArgumentOutOfRangeException>(() => origin.IntersectsCircle(0, 0, -1));
}

static void WideDistancePromotesBeforeSubtraction()
{
    long min = GameWorldSpatialProfile.MinimumGlobalAxisUnits;
    long max = GameWorldSpatialProfile.MaximumGlobalAxisUnits;
    Int128 delta = (Int128)max - min;
    Equal(checked(delta * delta * 2), SpatialMath.SquaredDistance(min, min, max, max));
    SpatialBounds far = GameWorldSpatialProfile.ChunkGeometry.GetBounds(
        new SpatialChunkCoord(int.MaxValue, int.MaxValue));
    True(far.SquaredDistanceTo(min, min) > long.MaxValue);
}

static void FloatImportUsesNearestMillimeterAwayFromZero()
{
    True(WorldPosition.TryFromMeters(12.035_728_3, -0.000_5, -12.035_728_3, out WorldPosition position));
    Equal(12_036L, position.GlobalXMillimeters);
    Equal(-12_036L, position.GlobalZMillimeters);
    Equal(-1, position.YMillimeters);
    True(WorldPosition.TryFromMeters(0.000_5, 0.000_5, -0.000_5, out position));
    Equal(1L, position.GlobalXMillimeters);
    Equal(-1L, position.GlobalZMillimeters);
    Equal(1, position.YMillimeters);
}

static void FloatImportRejectsNonfiniteAndOutOfProfile()
{
    False(WorldPosition.TryFromMeters(double.NaN, 0, 0, out _));
    False(WorldPosition.TryFromMeters(double.PositiveInfinity, 0, 0, out _));
    False(WorldPosition.TryFromMeters(0, double.NegativeInfinity, 0, out _));
    double aboveX = (GameWorldSpatialProfile.MaximumGlobalAxisUnits + 1_000_000d) /
        GameWorldSpatialProfile.UnitsPerMeter;
    double belowX = (GameWorldSpatialProfile.MinimumGlobalAxisUnits - 1_000_000d) /
        GameWorldSpatialProfile.UnitsPerMeter;
    False(WorldPosition.TryFromMeters(aboveX, 0, 0, out _));
    False(WorldPosition.TryFromMeters(belowX, 0, 0, out _));
    False(WorldPosition.TryFromMeters(0, (int.MaxValue + 1_000d) / 1_000d, 0, out _));
    False(WorldPosition.TryFromMeters(0, (int.MinValue - 1_000d) / 1_000d, 0, out _));
}

static void TerrainCellsUseIndependentFiveHundredMillimeterGrid()
{
    Equal(new TerrainCellCoord(0, 0), TerrainCellCoord.FromLocal(new WorldLocalPosition(0, 0, 0)));
    Equal(new TerrainCellCoord(0, 0), TerrainCellCoord.FromLocal(new WorldLocalPosition(499, 499, 0)));
    Equal(new TerrainCellCoord(1, 1), TerrainCellCoord.FromLocal(new WorldLocalPosition(500, 500, 0)));
    Equal(new TerrainCellCoord(31, 31), TerrainCellCoord.FromLocal(new WorldLocalPosition(15_999, 15_999, 0)));
}

static void DynamicPositionIsNotPlacementOrTerrainSnapped()
{
    True(WorldPosition.TryFromGlobalMillimeters(12_036, 17, 7_684, out WorldPosition position));
    Equal(12_036L, position.GlobalXMillimeters);
    Equal(7_684L, position.GlobalZMillimeters);
    False(position.GlobalXMillimeters % GameWorldSpatialProfile.TerrainCellEdgeUnits == 0);
    False(position.GlobalXMillimeters % GameWorldSpatialProfile.PlacementSnapUnits == 0);
    False(position.GlobalZMillimeters % GameWorldSpatialProfile.TerrainCellEdgeUnits == 0);
    False(position.GlobalZMillimeters % GameWorldSpatialProfile.PlacementSnapUnits == 0);
}

static void WarmedCoordinateMachineAllocatesZero()
{
    const int operations = 100_000;
    SpatialChunkGeometry geometry = GameWorldSpatialProfile.ChunkGeometry;
    SpatialSpaceId space = new(11);
    ulong expectedChecksum = Run();
    long before = GC.GetAllocatedBytesForCurrentThread();
    ulong checksum = Run();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    Equal(expectedChecksum, checksum);
    True(checksum != 0);

    ulong Run()
    {
        ulong value = 0;
        for (int index = 0; index < operations; index++)
        {
            long x = unchecked((long)(uint)(index * 104_729) - 2_000_000_000L);
            long z = unchecked((long)(uint)(index * 130_363) - 1_000_000_000L);
            if (!WorldPosition.TryFromGlobalMillimeters(x, index, z, out WorldPosition position))
                throw new InvalidOperationException("Warmed normalization failed.");
            SpatialChunkKey key = position.ToChunkKey(space);
            SpatialBounds bounds = geometry.GetBounds(key.Coord);
            TerrainCellCoord cell = TerrainCellCoord.FromLocal(position.Local);
            value = unchecked(value + (uint)key.GetHashCode() + (uint)cell.GetHashCode() +
                (ulong)bounds.SquaredDistanceTo(x, z));
        }
        return value;
    }
}

static void AssertPosition(
    long globalX,
    long globalZ,
    int expectedChunkX,
    int expectedChunkZ,
    int expectedLocalX,
    int expectedLocalZ)
{
    True(WorldPosition.TryFromGlobalMillimeters(globalX, 0, globalZ, out WorldPosition position));
    Equal(new SpatialChunkCoord(expectedChunkX, expectedChunkZ), position.Chunk);
    Equal(expectedLocalX, position.Local.XMillimeters);
    Equal(expectedLocalZ, position.Local.ZMillimeters);
    Equal(globalX, position.GlobalXMillimeters);
    Equal(globalZ, position.GlobalZMillimeters);
}

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("Expected true.");
}

static void False(bool condition) => True(!condition);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class RawSpatialFixture : IDisposable
{
    internal RawSpatialFixture(int lifecycleCapacity, int entityCapacity = 32)
    {
        var options = new EcsWorldOptions(
            new EntityCapacity(entityCapacity),
            maximumArchetypes: 4,
            maximumPreparedPlans: 8,
            maximumPreparedTransitions: 2,
            lifecycleRecordCapacity: lifecycleCapacity);
        World = new EcsWorld(GameSchema.CreateSchema(), options);
        State = new GameWorldState(World);
        RawPositionLookup = World.PrepareLookup<WorldPositionComponent>();
        ComponentId position = GameSchema.GetComponentId<WorldPositionComponent>();
        NonSpatialArchetype = World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        Span<ComponentId> spatialSignature = stackalloc ComponentId[1] { position };
        SpatialArchetype = World.RegisterArchetype(
            spatialSignature,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        RemovePosition = World.PrepareTransition(SpatialArchetype, NonSpatialArchetype);
        AddPosition = World.PrepareTransition(NonSpatialArchetype, SpatialArchetype);
        World.Seal();
        World.Initialize();
        World.BeginRun();
        State.Initialize(World);
    }

    internal EcsWorld World { get; }
    internal GameWorldState State { get; }
    internal ComponentLookup<WorldPositionComponent> RawPositionLookup { get; }
    internal ArchetypeHandle SpatialArchetype { get; }
    internal ArchetypeHandle NonSpatialArchetype { get; }
    internal ArchetypeTransition RemovePosition { get; }
    internal ArchetypeTransition AddPosition { get; }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}
