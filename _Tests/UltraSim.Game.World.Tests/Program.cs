using System.Runtime.InteropServices;
using UltraSim.Game.World;
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
    ("WarmedCoordinateMachineAllocatesZero", WarmedCoordinateMachineAllocatesZero),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Terrain T0.0 spatial/value tests passed.");

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
    Equal(2, Marshal.SizeOf<TerrainCellCoord>());
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
