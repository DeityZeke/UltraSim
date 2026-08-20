using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    ("TerrainSharedEdgesReconstructExactlyAcrossDifferentBases",
        TerrainSharedEdgesReconstructExactlyAcrossDifferentBases),
    ("TerrainHeightCodecWarmedAllocationZero", TerrainHeightCodecWarmedAllocationZero),
    ("WorldPackFormatAndFingerprintsAreFrozen", WorldPackFormatAndFingerprintsAreFrozen),
    ("WorldPackCompilationIsCanonicalAndOrderIndependent", WorldPackCompilationIsCanonicalAndOrderIndependent),
    ("WorldPackRandomTerrainReadsAreExactAndAllocationFree", WorldPackRandomTerrainReadsAreExactAndAllocationFree),
    ("WorldPackCompilerRejectsInvalidPlanesAndBrokenSeams", WorldPackCompilerRejectsInvalidPlanesAndBrokenSeams),
    ("WorldPackOpenRejectsCorruptionAndMalformedStructure", WorldPackOpenRejectsCorruptionAndMalformedStructure),
    ("WorldPackFileReopensWithStableFingerprint", WorldPackFileReopensWithStableFingerprint),
    ("WorldPackBoundsRejectBeforeProportionalAllocation", WorldPackBoundsRejectBeforeProportionalAllocation),
    ("WorldPackContentBindingPublishesWithGameGeneration", WorldPackContentBindingPublishesWithGameGeneration),
    ("BaseTerrainQueriesHonorParityBoundariesAndSignedRounding",
        BaseTerrainQueriesHonorParityBoundariesAndSignedRounding),
    ("BaseTerrainQueriesOwnNegativeAndCrossChunkCoordinates",
        BaseTerrainQueriesOwnNegativeAndCrossChunkCoordinates),
    ("BaseTerrainMeshIsCanonicalBoundedAndMatchesQueries",
        BaseTerrainMeshIsCanonicalBoundedAndMatchesQueries),
    ("BaseTerrainMeshSharesExactFramesAcrossChunkSeams",
        BaseTerrainMeshSharesExactFramesAcrossChunkSeams),
    ("BaseTerrainMeshHandlesMaximumReliefAcrossASeam",
        BaseTerrainMeshHandlesMaximumReliefAcrossASeam),
    ("BaseTerrainMeshFailuresAreTransactionalAndWarmedPathsAllocateZero",
        BaseTerrainMeshFailuresAreTransactionalAndWarmedPathsAllocateZero),
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

Console.WriteLine($"All {tests.Length} Terrain T0.0-T0.4 world tests passed.");

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
    Equal(4, Marshal.SizeOf<TerrainHeightPlaneBase>());
    Equal(2, Marshal.SizeOf<TerrainHeightOffset>());
}

static void TerrainFoundationLayoutIsFrozen()
{
    Equal(32, TerrainChunkFormat.MaterialCellEdge);
    Equal(1_024, TerrainChunkFormat.MaterialCellCount);
    Equal(33, TerrainChunkFormat.HeightVertexEdge);
    Equal(1_089, TerrainChunkFormat.HeightVertexCount);
    Equal(2_048, TerrainChunkFormat.TriangleCount);
    Equal(6_144, TerrainChunkFormat.TriangleIndexCount);
    Equal(5, TerrainChunkFormat.HeightQuantumMillimeters);
    Equal(327_675, TerrainChunkFormat.MaximumHeightReliefMillimeters);
    Equal(-2_147_483_645, TerrainChunkFormat.MinimumQuantizedHeightMillimeters);
    Equal(2_147_483_645, TerrainChunkFormat.MaximumQuantizedHeightMillimeters);
    Equal(2_048, TerrainChunkFormat.MaterialPlaneBytes);
    Equal(4, TerrainChunkFormat.HeightPlaneBaseBytes);
    Equal(2_178, TerrainChunkFormat.HeightOffsetPlaneBytes);
    Equal(2_182, TerrainChunkFormat.HeightPlaneBytes);
    Equal(4_230, TerrainChunkFormat.FoundationBytes);
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
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase zero));
    Equal(0, zero.Millimeters);
    True(TerrainHeightPlaneBase.TryCreate(-1_500_000, out TerrainHeightPlaneBase negative));
    Equal(-1_500_000, negative.Millimeters);
    True(TerrainHeightPlaneBase.TryCreate(1_500_000, out TerrainHeightPlaneBase positive));
    Equal(1_500_000, positive.Millimeters);
    False(TerrainHeightPlaneBase.TryCreate(1, out _));
    False(TerrainHeightPlaneBase.TryCreate(int.MinValue, out _));
    False(TerrainHeightPlaneBase.TryCreate(int.MaxValue, out _));
    for (int units = ushort.MinValue; units <= ushort.MaxValue; units++)
    {
        var offset = new TerrainHeightOffset(checked((ushort)units));
        Equal(units * TerrainChunkFormat.HeightQuantumMillimeters, offset.OffsetMillimeters);
    }
    for (int millimeters = -100; millimeters <= 100; millimeters++)
    {
        var source = Enumerable.Repeat(millimeters, TerrainChunkFormat.HeightVertexCount).ToArray();
        var encoded = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out TerrainHeightPlaneBase planeBase,
            out _, out TerrainHeightEncodingSummary summary));
        int expected = checked((int)Math.Round(millimeters /
            (double)TerrainChunkFormat.HeightQuantumMillimeters, MidpointRounding.AwayFromZero)) *
            TerrainChunkFormat.HeightQuantumMillimeters;
        Equal(expected, planeBase.Millimeters);
        Equal(0, encoded[0].Units);
        Equal(Math.Abs(millimeters - expected), summary.MaximumAbsoluteErrorMillimeters);
    }
}

static void TerrainHeightPlaneEncodingIsTransactional()
{
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    var encoded = Enumerable.Repeat(new TerrainHeightOffset(123), TerrainChunkFormat.HeightVertexCount).ToArray();
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryEncodePlane(source.AsSpan(1), encoded, out _, out _, out _));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryEncodePlane(source, encoded.AsSpan(1), out _, out _, out _));
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase zeroBase));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryDecodePlane(zeroBase, encoded.AsSpan(1), source, out _));
    Throws<ArgumentException>(() =>
        TerrainHeightEncoding.TryDecodePlane(zeroBase, encoded, source.AsSpan(1), out _));
    var overlapping = new byte[TerrainChunkFormat.HeightVertexCount * sizeof(int)];
    Throws<ArgumentException>(() => EncodeOverlappingTerrainPlane(overlapping));
    Throws<ArgumentException>(() => DecodeOverlappingTerrainPlane(overlapping));
    for (int index = 0; index < source.Length; index++)
        source[index] = checked(1_200_000 + (index - 544) * 37);
    source[700] = checked(source.Min() + TerrainChunkFormat.MaximumHeightReliefMillimeters + 5);
    False(TerrainHeightEncoding.TryEncodePlane(source, encoded, out _, out int invalid, out _));
    True(invalid >= 0);
    True(encoded.All(static value => value.Units == 123));

    source[700] = 1_212_345;
    True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out TerrainHeightPlaneBase planeBase, out invalid,
        out TerrainHeightEncodingSummary summary));
    Equal(-1, invalid);
    Equal(2, summary.MaximumAbsoluteErrorMillimeters);
    True(summary.TotalAbsoluteErrorMillimeters > 0);
    True(summary.ReliefMillimeters < TerrainChunkFormat.MaximumHeightReliefMillimeters);
    var decoded = new int[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightEncoding.TryDecodePlane(planeBase, encoded, decoded, out invalid));
    Equal(-1, invalid);
    for (int index = 0; index < source.Length; index++)
        True(Math.Abs(source[index] - decoded[index]) <= 2);

    True(TerrainHeightPlaneBase.TryCreate(TerrainChunkFormat.MaximumQuantizedHeightMillimeters,
        out TerrainHeightPlaneBase maximumBase));
    Array.Fill(encoded, new TerrainHeightOffset(ushort.MaxValue));
    Array.Fill(decoded, 17);
    False(TerrainHeightEncoding.TryDecodePlane(maximumBase, encoded, decoded, out invalid));
    Equal(0, invalid);
    True(decoded.All(static value => value == 17));

    Array.Fill(source, 0);
    source[42] = int.MinValue;
    False(TerrainHeightEncoding.TryEncodePlane(source, encoded, out _, out invalid, out _));
    Equal(42, invalid);
}

static void EncodeOverlappingTerrainPlane(byte[] storage)
{
    ReadOnlySpan<int> source = MemoryMarshal.Cast<byte, int>(storage);
    Span<TerrainHeightOffset> destination = MemoryMarshal.Cast<byte, TerrainHeightOffset>(storage)
        [..TerrainChunkFormat.HeightVertexCount];
    TerrainHeightEncoding.TryEncodePlane(source, destination, out _, out _, out _);
}

static void DecodeOverlappingTerrainPlane(byte[] storage)
{
    ReadOnlySpan<TerrainHeightOffset> source = MemoryMarshal.Cast<byte, TerrainHeightOffset>(storage)
        [..TerrainChunkFormat.HeightVertexCount];
    Span<int> destination = MemoryMarshal.Cast<byte, int>(storage);
    TerrainHeightEncoding.TryDecodePlane(default, source, destination, out _);
}

static void TerrainSharedEdgesReconstructExactlyAcrossDifferentBases()
{
    AssertSharedTerrainEdgeWithDifferentBases(1_250_003);
    AssertSharedTerrainEdgeWithDifferentBases(-1_500_002);
}

static void TerrainHeightCodecWarmedAllocationZero()
{
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    var encoded = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    var decoded = new int[TerrainChunkFormat.HeightVertexCount];
    for (int index = 0; index < source.Length; index++) source[index] = 1_400_000 + index * 17 - 9_000;
    True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out TerrainHeightPlaneBase planeBase,
        out _, out _));
    True(TerrainHeightEncoding.TryDecodePlane(planeBase, encoded, decoded, out _));
    long before = GC.GetAllocatedBytesForCurrentThread();
    long checksum = 0;
    for (int iteration = 0; iteration < 10_000; iteration++)
    {
        True(TerrainHeightEncoding.TryEncodePlane(source, encoded, out planeBase, out _, out _));
        True(TerrainHeightEncoding.TryDecodePlane(planeBase, encoded, decoded, out _));
        checksum += decoded[iteration % decoded.Length];
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
}

static void WorldPackFormatAndFingerprintsAreFrozen()
{
    Equal(160, WorldPackFormat.HeaderBytes);
    Equal(48, WorldPackFormat.ChunkDirectoryRowBytes);
    Equal(48, WorldPackFormat.SectionDirectoryRowBytes);
    Equal(128, WorldPackFormat.PackFingerprintOffset);
    Equal(32, WorldPackFormat.PackFingerprintBytes);
    Equal(1U, WorldPackFormat.ContainerVersion);
    Equal(1U, WorldPackFormat.TerrainMaterialsSchemaVersion);
    Equal(1U, WorldPackFormat.TerrainHeightsSchemaVersion);
    Equal(16, Marshal.SizeOf<WorldContentFingerprint>());
    Equal(32, Marshal.SizeOf<WorldPackFingerprint>());
    Equal(1U, (uint)WorldPackSectionKind.TerrainMaterials);
    Equal(2U, (uint)WorldPackSectionKind.TerrainHeights);
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldContentFingerprint(0, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLimits(159, 1, 2));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLimits(160, 0, 2));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLimits(160, 1, 1));
}

static void WorldPackCompilationIsCanonicalAndOrderIndependent()
{
    WorldPackArtifact forward = CreateWorldPackArtifact(reverse: false);
    WorldPackArtifact reverse = CreateWorldPackArtifact(reverse: true);
    Equal(forward.Length, reverse.Length);
    Equal(forward.Fingerprint, reverse.Fingerprint);
    var first = new byte[forward.Length];
    var second = new byte[reverse.Length];
    forward.CopyTo(first);
    reverse.CopyTo(second);
    True(first.AsSpan().SequenceEqual(second));

    WorldPack pack = forward.Open();
    Equal(16, pack.ChunkCount);
    Equal(2, pack.SectionCount);
    Equal(WorldPackTestProfile.Space, pack.Space);
    Equal(WorldPackTestProfile.Content, pack.ContentFingerprint);
    Equal(forward.Fingerprint, pack.Fingerprint);
    Equal(forward.Length, pack.Length);
}

static void WorldPackRandomTerrainReadsAreExactAndAllocationFree()
{
    WorldPack pack = CreateWorldPackArtifact(reverse: true).Open();
    ulong expected = Read(1_000);
    long before = GC.GetAllocatedBytesForCurrentThread();
    ulong actual = Read(100_000);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(expected != 0);
    True(actual != 0);
    False(pack.TryGetBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 100, 100), out _));

    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var heights = new int[TerrainChunkFormat.HeightVertexCount];
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, -2, -2);
    True(pack.TryGetBaseTerrain(key, out WorldTerrainChunkView view));
    view.CopyMaterials(materials);
    view.CopyAbsoluteHeights(heights);
    Equal(PackMaterial(-2, -2, 17, 9), materials[TerrainChunkFormat.GetMaterialIndex(
        new TerrainCellCoord(17, 9))]);
    Equal(QuantizePackHeight(PackHeight(-2, -2, 32, 32)),
        heights[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, 32))]);

    ulong Read(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = operation % 4 - 2;
            int chunkZ = operation / 4 % 4 - 2;
            var chunk = new SpatialChunkKey(WorldPackTestProfile.Space, chunkX, chunkZ);
            if (!pack.TryGetBaseTerrain(chunk, out WorldTerrainChunkView terrain))
                throw new InvalidOperationException("Expected packed chunk was absent.");
            int materialIndex = operation % TerrainChunkFormat.MaterialCellCount;
            int vertexIndex = operation % TerrainChunkFormat.HeightVertexCount;
            checksum = unchecked(checksum + terrain.GetMaterial(materialIndex) +
                (uint)terrain.GetAbsoluteHeightMillimeters(vertexIndex) +
                (uint)terrain.HeightBase.Millimeters);
        }
        return checksum;
    }
}

static void WorldPackCompilerRejectsInvalidPlanesAndBrokenSeams()
{
    var limits = new WorldPackLimits(2 * 1024 * 1024, 16, 8);
    CreatePackPlanes(-1, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights);
    var firstKey = new SpatialChunkKey(WorldPackTestProfile.Space, -1, 0);
    var secondKey = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);

    var duplicate = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content, limits);
    True(duplicate.TryAddBaseTerrain(firstKey, materials, heightBase, heights, out _));
    False(duplicate.TryAddBaseTerrain(firstKey, materials, heightBase, heights, out WorldPackBuildError error));
    Equal(WorldPackBuildError.DuplicateChunk, error);
    False(duplicate.TryAddBaseTerrain(new SpatialChunkKey(new SpatialSpaceId(999), 0, 0),
        materials, heightBase, heights, out error));
    Equal(WorldPackBuildError.SpaceMismatch, error);
    False(duplicate.TryAddBaseTerrain(secondKey, materials.AsSpan(1), heightBase, heights, out error));
    Equal(WorldPackBuildError.MalformedMaterialPlane, error);
    materials[4] = 0;
    False(duplicate.TryAddBaseTerrain(secondKey, materials, heightBase, heights, out error));
    Equal(WorldPackBuildError.InvalidMaterialId, error);
    materials[4] = 1;
    False(duplicate.TryAddBaseTerrain(secondKey, materials, heightBase, heights.AsSpan(1), out error));
    Equal(WorldPackBuildError.MalformedHeightPlane, error);
    True(TerrainHeightPlaneBase.TryCreate(TerrainChunkFormat.MaximumQuantizedHeightMillimeters,
        out TerrainHeightPlaneBase maximumBase));
    Array.Fill(heights, new TerrainHeightOffset(ushort.MaxValue));
    False(duplicate.TryAddBaseTerrain(secondKey, materials, maximumBase, heights, out error));
    Equal(WorldPackBuildError.InvalidHeightPlane, error);

    var capacity = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 1, 8));
    CreatePackPlanes(-1, 0, out materials, out heightBase, out heights);
    True(capacity.TryAddBaseTerrain(firstKey, materials, heightBase, heights, out _));
    CreatePackPlanes(0, 0, out materials, out heightBase, out heights);
    False(capacity.TryAddBaseTerrain(secondKey, materials, heightBase, heights, out error));
    Equal(WorldPackBuildError.CapacityExceeded, error);

    var brokenSeam = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content, limits);
    CreatePackPlanes(-1, 0, out materials, out heightBase, out heights);
    True(brokenSeam.TryAddBaseTerrain(firstKey, materials, heightBase, heights, out _));
    CreatePackPlanes(0, 0, out materials, out heightBase, out heights);
    int edge = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, 11));
    heights[edge] = new TerrainHeightOffset(checked((ushort)(heights[edge].Units + 1)));
    True(brokenSeam.TryAddBaseTerrain(secondKey, materials, heightBase, heights, out _));
    False(brokenSeam.TryBuild(out WorldPackArtifact? artifact, out error));
    Equal(null, artifact);
    Equal(WorldPackBuildError.SharedEdgeMismatch, error);
}

static void WorldPackOpenRejectsCorruptionAndMalformedStructure()
{
    WorldPackArtifact artifact = CreateWorldPackArtifact(reverse: false);
    var valid = new byte[artifact.Length];
    artifact.CopyTo(valid);
    var limits = new WorldPackLimits(2 * 1024 * 1024, 64, 8);
    False(WorldPack.TryOpen(valid, limits, new WorldContentFingerprint(8, 9),
        out WorldPack? pack, out WorldPackOpenError error));
    Equal(null, pack);
    Equal(WorldPackOpenError.ContentMismatch, error);

    byte[] corrupted = (byte[])valid.Clone();
    corrupted[^1] ^= 0x80;
    AssertPackOpenFails(corrupted, limits, WorldPackOpenError.HashMismatch);
    byte[] sectionCorrupted = (byte[])valid.Clone();
    sectionCorrupted[^1] ^= 0x40;
    RewritePackFingerprint(sectionCorrupted);
    AssertPackOpenFails(sectionCorrupted, limits, WorldPackOpenError.HashMismatch);
    AssertPackOpenFails(valid.AsSpan(0, WorldPackFormat.HeaderBytes - 1).ToArray(), limits,
        WorldPackOpenError.Truncated);
    byte[] trailing = new byte[valid.Length + 1];
    valid.CopyTo(trailing, 0);
    AssertPackOpenFails(trailing, limits, WorldPackOpenError.LengthMismatch);

    byte[] badVersion = (byte[])valid.Clone();
    BinaryPrimitives.WriteUInt32LittleEndian(badVersion.AsSpan(8), 2);
    AssertPackOpenFails(badVersion, limits, WorldPackOpenError.UnsupportedVersion);

    byte[] unsorted = (byte[])valid.Clone();
    BinaryPrimitives.WriteInt32LittleEndian(unsorted.AsSpan(WorldPackFormat.HeaderBytes + 8), int.MaxValue);
    RewritePackFingerprint(unsorted);
    AssertPackOpenFails(unsorted, limits, WorldPackOpenError.InvalidChunkDirectory);

    byte[] unsupportedHeight = (byte[])valid.Clone();
    int sectionDirectory = WorldPackFormat.HeaderBytes + 16 * WorldPackFormat.ChunkDirectoryRowBytes;
    BinaryPrimitives.WriteUInt32LittleEndian(unsupportedHeight.AsSpan(
        sectionDirectory + WorldPackFormat.SectionDirectoryRowBytes + 4), 2);
    RewritePackFingerprint(unsupportedHeight);
    AssertPackOpenFails(unsupportedHeight, limits, WorldPackOpenError.UnsupportedRequiredSection);

    byte[] brokenPackedSeam = (byte[])valid.Clone();
    int heightRow = sectionDirectory + WorldPackFormat.SectionDirectoryRowBytes;
    int heightSection = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
        brokenPackedSeam.AsSpan(heightRow + 8)));
    int rightEdgeVertex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, 0));
    int heightValue = heightSection + TerrainChunkFormat.HeightPlaneBaseBytes + rightEdgeVertex * sizeof(ushort);
    ushort oldUnits = BinaryPrimitives.ReadUInt16LittleEndian(brokenPackedSeam.AsSpan(heightValue));
    BinaryPrimitives.WriteUInt16LittleEndian(brokenPackedSeam.AsSpan(heightValue),
        checked((ushort)(oldUnits + 1)));
    RewriteSectionDigest(brokenPackedSeam, heightRow);
    RewritePackFingerprint(brokenPackedSeam);
    AssertPackOpenFails(brokenPackedSeam, limits, WorldPackOpenError.SharedEdgeMismatch);
}

static void WorldPackFileReopensWithStableFingerprint()
{
    WorldPackArtifact artifact = CreateWorldPackArtifact(reverse: true);
    string path = Path.Combine(Path.GetTempPath(), $"ultrasim-worldpack-{Guid.NewGuid():N}.uwp");
    try
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            artifact.WriteTo(stream);
        True(WorldPack.TryOpenFile(path, new WorldPackLimits(2 * 1024 * 1024, 64, 8), WorldPackTestProfile.Content,
            out WorldPack? reopened, out WorldPackOpenError error));
        Equal(WorldPackOpenError.None, error);
        True(reopened is not null);
        Equal(artifact.Fingerprint, reopened!.Fingerprint);
        Equal(artifact.Length, reopened.Length);
        True(reopened.TryGetBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 1, 1),
            out WorldTerrainChunkView terrain));
        Equal(QuantizePackHeight(PackHeight(1, 1, 32, 32)),
            terrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
                new TerrainVertexCoord(32, 32))));
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

static void WorldPackBoundsRejectBeforeProportionalAllocation()
{
    var header = new byte[WorldPackFormat.HeaderBytes];
    BinaryPrimitives.WriteUInt64LittleEndian(header, WorldPackFormat.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), WorldPackFormat.ContainerVersion);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), 16 * 1024 * 1024);
    var limits = new WorldPackLimits(1024 * 1024, 64, 8);
    False(WorldPack.TryOpen(header, limits, WorldPackTestProfile.Content, out _, out WorldPackOpenError warmError));
    Equal(WorldPackOpenError.CapacityExceeded, warmError);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 10_000; index++)
    {
        False(WorldPack.TryOpen(header, limits, WorldPackTestProfile.Content, out WorldPack? pack,
            out WorldPackOpenError error));
        Equal(null, pack);
        Equal(WorldPackOpenError.CapacityExceeded, error);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
}

static void WorldPackContentBindingPublishesWithGameGeneration()
{
    Throws<ArgumentException>(() => _ = new WorldContentBinding(GameContent.WorldFingerprint, [0]));
    Throws<ArgumentException>(() => _ = new WorldContentBinding(GameContent.WorldFingerprint, [1, 1]));
    WorldPack wrongContent = CreateWorldPackArtifact(reverse: false).Open();
    var gameBinding = new WorldContentBinding(GameContent.WorldFingerprint,
    [
        GameTerrainMaterialIds.Water,
        GameTerrainMaterialIds.Sand,
        GameTerrainMaterialIds.Grass,
        GameTerrainMaterialIds.Rock,
        GameTerrainMaterialIds.Snow,
    ]);
    False(wrongContent.TryBind(gameBinding, out BoundWorldPack? bound, out WorldPackBindingError error));
    Equal(null, bound);
    Equal(WorldPackBindingError.ContentMismatch, error);

    WorldPack gamePack = CreateWorldPackArtifact(reverse: true, GameContent.WorldFingerprint).Open();
    var incomplete = new WorldContentBinding(GameContent.WorldFingerprint, [GameTerrainMaterialIds.Water]);
    False(gamePack.TryBind(incomplete, out bound, out error));
    Equal(null, bound);
    Equal(WorldPackBindingError.UnknownTerrainMaterial, error);

    var composition = new UltraSimGameComposition(baseWorldPack: gamePack);
    using var runtime = new ServerRuntime(composition,
        new ServerRuntimeOptions(UltraSimGameComposition.CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((_, _, game) =>
    {
        True(game.WorldState.BaseWorld is not null);
        Equal(gamePack.Fingerprint, game.WorldState.BaseWorld!.Fingerprint);
        Equal(gamePack.ChunkCount, game.WorldState.BaseWorld.ChunkCount);
        return true;
    }, out bool published));
    True(published);
}

static void BaseTerrainQueriesHonorParityBoundariesAndSignedRounding()
{
    BoundWorldPack world = CreateBoundTerrainWorld(0, 0, 0, 0, QueryHeight, QueryMaterial);

    Equal(1_400, Sample(400, 100));
    Equal(2_000, Sample(100, 400));
    Equal(2_000, Sample(600, 100));
    Equal(6_200, Sample(900, 400));
    Equal(2_000, Sample(250, 250));
    Equal(3_600, Sample(700, 300));
    Equal(-3, Sample(250, 1_000));
    Equal(3, Sample(750, 1_000));

    var firstCell = new WorldPosition(0, 0, 499, 499, int.MinValue);
    True(world.TryResolveBaseTerrainMaterial(firstCell, out ushort firstMaterial));
    Equal(QueryMaterial(0, 0), firstMaterial);
    var nextCell = new WorldPosition(0, 0, 500, 0, int.MaxValue);
    True(world.TryResolveBaseTerrainMaterial(nextCell, out ushort nextMaterial));
    Equal(QueryMaterial(1, 0), nextMaterial);

    var absent = new WorldPosition(1, 0, 0, 0, 0);
    False(world.TrySampleBaseTerrainHeight(absent, out int absentHeight));
    Equal(0, absentHeight);
    False(world.TryResolveBaseTerrainMaterial(absent, out ushort absentMaterial));
    Equal((ushort)0, absentMaterial);

    int Sample(ushort x, ushort z)
    {
        var location = new WorldPosition(0, 0, x, z, 123_456_789);
        True(world.TrySampleBaseTerrainHeight(location, out int height));
        return height;
    }

    static int QueryHeight(int x, int z) => (x, z) switch
    {
        (0, 0) => 0,
        (1, 0) => 1_000,
        (2, 0) => 3_000,
        (0, 1) => 2_000,
        (1, 1) => 4_000,
        (2, 1) => 8_000,
        (0, 2) => -5,
        (1, 2) => 0,
        (2, 2) => 5,
        (0, 3) => -5,
        (1, 3) => 0,
        (2, 3) => 5,
        _ => 0,
    };
}

static void BaseTerrainQueriesOwnNegativeAndCrossChunkCoordinates()
{
    BoundWorldPack world = CreateBoundTerrainWorld(-2, 1, -2, 1, SmoothHeight, QueryMaterial, reverse: true);
    for (int vertexZ = -31; vertexZ <= 31; vertexZ++)
    {
        for (int vertexX = -31; vertexX <= 31; vertexX++)
        {
            long globalX = checked((long)vertexX * GameWorldSpatialProfile.TerrainCellEdgeUnits);
            long globalZ = checked((long)vertexZ * GameWorldSpatialProfile.TerrainCellEdgeUnits);
            True(WorldPosition.TryFromGlobalMillimeters(globalX, int.MinValue, globalZ, out WorldPosition location));
            True(world.TrySampleBaseTerrainHeight(location, out int height));
            Equal(SmoothHeight(vertexX, vertexZ), height);
        }
    }

    (long X, long Z)[] probes =
    [
        (-1, -1),
        (-500, -500),
        (-501, -999),
        (-16_000, 0),
        (-16_001, 15_999),
        (0, 0),
        (15_999, -1),
        (16_000, 16_000),
    ];
    foreach ((long x, long z) in probes)
    {
        True(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition location));
        True(world.TryResolveBaseTerrainMaterial(location, out ushort material));
        int globalCellX = checked((int)FloorDivideReference(x, GameWorldSpatialProfile.TerrainCellEdgeUnits));
        int globalCellZ = checked((int)FloorDivideReference(z, GameWorldSpatialProfile.TerrainCellEdgeUnits));
        Equal(QueryMaterial(globalCellX, globalCellZ), material);
    }
}

static void BaseTerrainMeshIsCanonicalBoundedAndMatchesQueries()
{
    Equal(36, Marshal.SizeOf<TerrainBaseMeshVertex>());
    BoundWorldPack world = CreateBoundTerrainWorld(-2, 1, -2, 1, SmoothHeight, QueryMaterial);
    var key = new SpatialChunkKey(world.Space, 0, 0);
    TerrainBaseMeshVertex[] vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    ushort[] indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    ushort[] materials = new ushort[TerrainChunkFormat.TriangleCount];
    True(world.TryBuildBaseTerrainMesh(key, vertices, indices, materials,
        out TerrainBaseMeshSummary summary, out TerrainBaseMeshBuildError error));
    Equal(TerrainBaseMeshBuildError.None, error);
    Equal(TerrainChunkFormat.HeightVertexCount, summary.VertexCount);
    Equal(TerrainChunkFormat.TriangleCount, summary.TriangleCount);
    Equal(TerrainChunkFormat.TriangleIndexCount, summary.IndexCount);
    Equal(vertices.Min(vertex => vertex.AbsoluteYMillimeters), summary.MinimumHeightMillimeters);
    Equal(vertices.Max(vertex => vertex.AbsoluteYMillimeters), summary.MaximumHeightMillimeters);

    Equal((ushort)0, indices[0]);
    Equal((ushort)33, indices[1]);
    Equal((ushort)34, indices[2]);
    Equal((ushort)0, indices[3]);
    Equal((ushort)34, indices[4]);
    Equal((ushort)1, indices[5]);
    True(indices.All(index => index < TerrainChunkFormat.HeightVertexCount));
    for (int triangle = 0; triangle < materials.Length; triangle += 2)
        Equal(materials[triangle], materials[triangle + 1]);

    for (int index = 0; index < vertices.Length; index += 37)
    {
        TerrainBaseMeshVertex vertex = vertices[index];
        long globalX = checked((long)key.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
            vertex.LocalXMillimeters);
        long globalZ = checked((long)key.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits +
            vertex.LocalZMillimeters);
        True(WorldPosition.TryFromGlobalMillimeters(globalX, 0, globalZ, out WorldPosition location));
        True(world.TrySampleBaseTerrainHeight(location, out int sampled));
        Equal(vertex.AbsoluteYMillimeters, sampled);
        True(vertex.NormalY > 0);
        Equal(0L, checked((long)vertex.NormalX * vertex.TangentX +
            (long)vertex.NormalY * vertex.TangentY + (long)vertex.NormalZ * vertex.TangentZ));
    }

    TerrainBaseMeshVertex[] secondVertices = new TerrainBaseMeshVertex[vertices.Length];
    ushort[] secondIndices = new ushort[indices.Length];
    ushort[] secondMaterials = new ushort[materials.Length];
    True(world.TryBuildBaseTerrainMesh(key, secondVertices, secondIndices, secondMaterials, out _, out _));
    True(vertices.AsSpan().SequenceEqual(secondVertices));
    True(indices.AsSpan().SequenceEqual(secondIndices));
    True(materials.AsSpan().SequenceEqual(secondMaterials));
    string hash = ComputeMeshHash(vertices, indices, materials);
    Equal("943EC206C09A4E539F252C8B117C926B19FFC0E422185152F3662CBD7D5C1B6D", hash);
    Equal(hash, ComputeMeshHash(secondVertices, secondIndices, secondMaterials));
}

static void BaseTerrainMeshSharesExactFramesAcrossChunkSeams()
{
    BoundWorldPack world = CreateBoundTerrainWorld(-2, 1, -2, 1, SmoothHeight, QueryMaterial, reverse: true);
    var leftKey = new SpatialChunkKey(world.Space, -1, 0);
    var rightKey = new SpatialChunkKey(world.Space, 0, 0);
    TerrainBaseMeshVertex[] left = Build(leftKey);
    TerrainBaseMeshVertex[] right = Build(rightKey);
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        TerrainBaseMeshVertex first = left[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, z))];
        TerrainBaseMeshVertex second = right[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, z))];
        Equal(checked((long)leftKey.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
            first.LocalXMillimeters),
            checked((long)rightKey.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                second.LocalXMillimeters));
        Equal(first.AbsoluteYMillimeters, second.AbsoluteYMillimeters);
        Equal(first.NormalX, second.NormalX);
        Equal(first.NormalY, second.NormalY);
        Equal(first.NormalZ, second.NormalZ);
        Equal(first.TangentX, second.TangentX);
        Equal(first.TangentY, second.TangentY);
        Equal(first.TangentZ, second.TangentZ);
    }

    TerrainBaseMeshVertex[] Build(SpatialChunkKey key)
    {
        var vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
        var materials = new ushort[TerrainChunkFormat.TriangleCount];
        True(world.TryBuildBaseTerrainMesh(key, vertices, indices, materials, out _, out _));
        return vertices;
    }
}

static void BaseTerrainMeshHandlesMaximumReliefAcrossASeam()
{
    int minimum = TerrainChunkFormat.MinimumQuantizedHeightMillimeters;
    int relief = TerrainChunkFormat.MaximumHeightReliefMillimeters;
    BoundWorldPack world = CreateBoundTerrainWorld(-1, 0, 0, 0,
        (x, _) => x < 0 ? minimum : x == 0 ? checked(minimum + relief) : checked(minimum + relief * 2),
        QueryMaterial);
    TerrainBaseMeshVertex[] left = Build(new SpatialChunkKey(world.Space, -1, 0));
    TerrainBaseMeshVertex[] right = Build(new SpatialChunkKey(world.Space, 0, 0));
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        TerrainBaseMeshVertex first = left[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, z))];
        TerrainBaseMeshVertex second = right[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, z))];
        Equal(checked(minimum + relief), first.AbsoluteYMillimeters);
        Equal(first.AbsoluteYMillimeters, second.AbsoluteYMillimeters);
        Equal(first.NormalX, second.NormalX);
        Equal(first.NormalY, second.NormalY);
        Equal(first.NormalZ, second.NormalZ);
        True(first.NormalY > 0);
        Equal(0L, checked((long)first.NormalX * first.TangentX +
            (long)first.NormalY * first.TangentY + (long)first.NormalZ * first.TangentZ));
    }

    TerrainBaseMeshVertex[] Build(SpatialChunkKey key)
    {
        var vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
        var materials = new ushort[TerrainChunkFormat.TriangleCount];
        True(world.TryBuildBaseTerrainMesh(key, vertices, indices, materials, out _, out _));
        return vertices;
    }
}

static void BaseTerrainMeshFailuresAreTransactionalAndWarmedPathsAllocateZero()
{
    BoundWorldPack world = CreateBoundTerrainWorld(-1, 1, -1, 1, SmoothHeight, QueryMaterial);
    var key = new SpatialChunkKey(world.Space, 0, 0);
    var sentinel = new TerrainBaseMeshVertex(123, 123, 123, 123, 123, 123, 123, 123, 123);
    TerrainBaseMeshVertex[] vertices = Enumerable.Repeat(sentinel, TerrainChunkFormat.HeightVertexCount).ToArray();
    ushort[] indices = Enumerable.Repeat(ushort.MaxValue, TerrainChunkFormat.TriangleIndexCount).ToArray();
    ushort[] materials = Enumerable.Repeat(ushort.MaxValue, TerrainChunkFormat.TriangleCount).ToArray();
    False(world.TryBuildBaseTerrainMesh(key, vertices.AsSpan(1), indices, materials,
        out TerrainBaseMeshSummary summary, out TerrainBaseMeshBuildError error));
    Equal(default, summary);
    Equal(TerrainBaseMeshBuildError.DestinationTooSmall, error);
    True(vertices.All(value => value == sentinel));
    True(indices.All(value => value == ushort.MaxValue));
    True(materials.All(value => value == ushort.MaxValue));

    int vertexByteCount = TerrainChunkFormat.HeightVertexCount * Marshal.SizeOf<TerrainBaseMeshVertex>();
    var overlappingStorage = new byte[vertexByteCount];
    Span<TerrainBaseMeshVertex> overlappingVertices =
        MemoryMarshal.Cast<byte, TerrainBaseMeshVertex>(overlappingStorage);
    Span<ushort> overlappingIndices = MemoryMarshal.Cast<byte, ushort>(
        overlappingStorage.AsSpan(0, TerrainChunkFormat.TriangleIndexCount * sizeof(ushort)));
    False(world.TryBuildBaseTerrainMesh(key, overlappingVertices, overlappingIndices, materials,
        out summary, out error));
    Equal(default, summary);
    Equal(TerrainBaseMeshBuildError.OverlappingDestinations, error);
    True(overlappingStorage.All(value => value == 0));
    True(materials.All(value => value == ushort.MaxValue));

    False(world.TryBuildBaseTerrainMesh(new SpatialChunkKey(world.Space, 99, 99), vertices, indices, materials,
        out summary, out error));
    Equal(default, summary);
    Equal(TerrainBaseMeshBuildError.MissingChunk, error);
    True(vertices.All(value => value == sentinel));
    True(indices.All(value => value == ushort.MaxValue));
    True(materials.All(value => value == ushort.MaxValue));

    True(world.TryBuildBaseTerrainMesh(key, vertices, indices, materials, out _, out _));
    var probe = new WorldPosition(0, 0, 12_345, 6_789, 0);
    True(world.TrySampleBaseTerrainHeight(probe, out _));
    True(world.TryResolveBaseTerrainMaterial(probe, out _));
    long before = GC.GetAllocatedBytesForCurrentThread();
    long checksum = 0;
    for (int iteration = 0; iteration < 64; iteration++)
    {
        True(world.TryBuildBaseTerrainMesh(key, vertices, indices, materials, out summary, out _));
        checksum += vertices[iteration].NormalY + indices[iteration] + materials[iteration];
    }
    for (int iteration = 0; iteration < 100_000; iteration++)
    {
        var location = new WorldPosition(0, 0, checked((ushort)(iteration % 16_000)),
            checked((ushort)(iteration * 37 % 16_000)), 0);
        True(world.TrySampleBaseTerrainHeight(location, out int height));
        True(world.TryResolveBaseTerrainMaterial(location, out ushort material));
        checksum += height + material;
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
}

static BoundWorldPack CreateBoundTerrainWorld(
    int minimumChunkX,
    int maximumChunkX,
    int minimumChunkZ,
    int maximumChunkZ,
    Func<int, int, int> heightSource,
    Func<int, int, ushort> materialSource,
    bool reverse = false)
{
    int chunkCount = checked((maximumChunkX - minimumChunkX + 1) *
        (maximumChunkZ - minimumChunkZ + 1));
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(16 * 1024 * 1024, chunkCount, 8));
    var coordinates = new List<(int X, int Z)>(chunkCount);
    for (int chunkZ = minimumChunkZ; chunkZ <= maximumChunkZ; chunkZ++)
        for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
            coordinates.Add((chunkX, chunkZ));
    if (reverse) coordinates.Reverse();
    foreach ((int chunkX, int chunkZ) in coordinates)
    {
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        for (byte z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                materials[TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(x, z))] =
                    materialSource(globalX, globalZ);
            }
        }
        var heights = new int[TerrainChunkFormat.HeightVertexCount];
        for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                heights[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z))] =
                    heightSource(globalX, globalZ);
            }
        }
        var encoded = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        True(TerrainHeightEncoding.TryEncodePlane(heights, encoded, out TerrainHeightPlaneBase heightBase,
            out _, out _));
        True(builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, chunkX, chunkZ),
            materials, heightBase, encoded, out WorldPackBuildError error));
        Equal(WorldPackBuildError.None, error);
    }
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError));
    Equal(WorldPackBuildError.None, buildError);
    True(artifact is not null);
    WorldPack pack = artifact!.Open();
    var binding = new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]);
    True(pack.TryBind(binding, out BoundWorldPack? bound, out WorldPackBindingError bindError));
    Equal(WorldPackBindingError.None, bindError);
    True(bound is not null);
    return bound!;
}

static int SmoothHeight(int globalVertexX, int globalVertexZ)
{
    long shape = (long)globalVertexX * globalVertexX +
        3L * globalVertexZ * globalVertexZ + (long)globalVertexX * globalVertexZ;
    long pattern = shape % 17;
    if (pattern < 0) pattern += 17;
    return checked((int)(750_000L + globalVertexX * 25L + globalVertexZ * 15L + pattern * 5L));
}

static ushort QueryMaterial(int globalCellX, int globalCellZ)
{
    int value = (globalCellX + globalCellZ * 2) % 5;
    if (value < 0) value += 5;
    return checked((ushort)(value + 1));
}

static long FloorDivideReference(long value, int positiveDivisor)
{
    long quotient = value / positiveDivisor;
    if (value % positiveDivisor < 0) quotient--;
    return quotient;
}

static string ComputeMeshHash(
    ReadOnlySpan<TerrainBaseMeshVertex> vertices,
    ReadOnlySpan<ushort> indices,
    ReadOnlySpan<ushort> materials)
{
    int length = checked(vertices.Length * 9 * sizeof(int) +
        indices.Length * sizeof(ushort) + materials.Length * sizeof(ushort));
    var bytes = new byte[length];
    int offset = 0;
    foreach (TerrainBaseMeshVertex vertex in vertices)
    {
        Write(vertex.LocalXMillimeters);
        Write(vertex.AbsoluteYMillimeters);
        Write(vertex.LocalZMillimeters);
        Write(vertex.NormalX);
        Write(vertex.NormalY);
        Write(vertex.NormalZ);
        Write(vertex.TangentX);
        Write(vertex.TangentY);
        Write(vertex.TangentZ);
    }
    foreach (ushort index in indices)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), index);
        offset += sizeof(ushort);
    }
    foreach (ushort material in materials)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), material);
        offset += sizeof(ushort);
    }
    Equal(bytes.Length, offset);
    return Convert.ToHexString(SHA256.HashData(bytes));

    void Write(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        offset += sizeof(int);
    }
}

static WorldPackArtifact CreateWorldPackArtifact(
    bool reverse,
    WorldContentFingerprint? content = null)
{
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space,
        content ?? WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 64, 8));
    var keys = new List<SpatialChunkKey>();
    for (int z = -2; z < 2; z++)
        for (int x = -2; x < 2; x++)
            keys.Add(new SpatialChunkKey(WorldPackTestProfile.Space, x, z));
    if (reverse) keys.Reverse();
    foreach (SpatialChunkKey key in keys)
    {
        CreatePackPlanes(key.Coord.X, key.Coord.Z, out ushort[] materials,
            out TerrainHeightPlaneBase heightBase, out TerrainHeightOffset[] heights);
        True(builder.TryAddBaseTerrain(key, materials, heightBase, heights, out WorldPackBuildError error));
        Equal(WorldPackBuildError.None, error);
    }
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError));
    Equal(WorldPackBuildError.None, buildError);
    True(artifact is not null);
    return artifact!;
}

static void CreatePackPlanes(
    int chunkX,
    int chunkZ,
    out ushort[] materials,
    out TerrainHeightPlaneBase heightBase,
    out TerrainHeightOffset[] heights)
{
    materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    for (byte z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        for (byte x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
            materials[TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(x, z))] =
                PackMaterial(chunkX, chunkZ, x, z);
    var source = new int[TerrainChunkFormat.HeightVertexCount];
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            source[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z))] =
                PackHeight(chunkX, chunkZ, x, z);
    heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightEncoding.TryEncodePlane(source, heights, out heightBase, out _, out _));
}

static ushort PackMaterial(int chunkX, int chunkZ, int localX, int localZ)
{
    int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + localX);
    int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ);
    uint hash = unchecked((uint)(globalX * 31) ^ (uint)(globalZ * 17));
    return checked((ushort)(hash % 5 + 1));
}

static int PackHeight(int chunkX, int chunkZ, int localX, int localZ)
{
    int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + localX);
    int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ);
    return checked(1_200_003 + globalX * 35 + globalZ * 20);
}

static int QuantizePackHeight(int millimeters) => checked(
    (int)Math.Round(millimeters / (double)TerrainChunkFormat.HeightQuantumMillimeters,
        MidpointRounding.AwayFromZero) * TerrainChunkFormat.HeightQuantumMillimeters);

static void AssertPackOpenFails(byte[] bytes, WorldPackLimits limits, WorldPackOpenError expected)
{
    False(WorldPack.TryOpen(bytes, limits, WorldPackTestProfile.Content, out WorldPack? pack, out WorldPackOpenError error));
    Equal(null, pack);
    Equal(expected, error);
}

static void RewritePackFingerprint(byte[] bytes)
{
    bytes.AsSpan(WorldPackFormat.PackFingerprintOffset, WorldPackFormat.PackFingerprintBytes).Clear();
    Span<byte> digest = stackalloc byte[WorldPackFormat.PackFingerprintBytes];
    SHA256.HashData(bytes, digest);
    digest.CopyTo(bytes.AsSpan(WorldPackFormat.PackFingerprintOffset, WorldPackFormat.PackFingerprintBytes));
}

static void RewriteSectionDigest(byte[] bytes, int rowOffset)
{
    int payloadOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(rowOffset + 8)));
    int payloadLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(rowOffset + 16)));
    Span<byte> digest = stackalloc byte[32];
    SHA256.HashData(bytes.AsSpan(payloadOffset, payloadLength), digest);
    digest[..16].CopyTo(bytes.AsSpan(rowOffset + 32, 16));
}

static void AssertSharedTerrainEdgeWithDifferentBases(int elevationOffset)
{
    var leftSource = new int[TerrainChunkFormat.HeightVertexCount];
    var rightSource = new int[TerrainChunkFormat.HeightVertexCount];
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
        {
            int index = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z));
            leftSource[index] = checked(elevationOffset + x * 137 + z * 250);
            int globalRightX = GameWorldSpatialProfile.TerrainCellsPerChunkEdge + x;
            rightSource[index] = checked(elevationOffset + globalRightX * 137 + z * 250);
        }
    }
    var left = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    var right = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightEncoding.TryEncodePlane(leftSource, left, out TerrainHeightPlaneBase leftBase,
        out _, out _));
    True(TerrainHeightEncoding.TryEncodePlane(rightSource, right, out TerrainHeightPlaneBase rightBase,
        out _, out _));
    False(leftBase == rightBase);
    for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
    {
        int leftIndex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, z));
        int rightIndex = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, z));
        int leftAbsolute = checked(leftBase.Millimeters + left[leftIndex].OffsetMillimeters);
        int rightAbsolute = checked(rightBase.Millimeters + right[rightIndex].OffsetMillimeters);
        Equal(leftAbsolute, rightAbsolute);
    }
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

static class WorldPackTestProfile
{
    internal static SpatialSpaceId Space { get; } = new(901);
    internal static WorldContentFingerprint Content { get; } =
        new(0x1122_3344_5566_7788UL, 0x8877_6655_4433_2211UL);
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
