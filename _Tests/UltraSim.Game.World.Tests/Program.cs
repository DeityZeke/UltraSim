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
    ("FrozenStaticsAndStaticMultisCompileCanonically", FrozenStaticsAndStaticMultisCompileCanonically),
    ("FrozenStructureSectionsAreIndependentlyOptional", FrozenStructureSectionsAreIndependentlyOptional),
    ("FrozenStructureReadsAndRenderGroupsAllocateZero", FrozenStructureReadsAndRenderGroupsAllocateZero),
    ("StaticBoundsQueryIsExactBoundedAndTransactional", StaticBoundsQueryIsExactBoundedAndTransactional),
    ("FrozenStructureCompilerRejectsInvalidIdentityPlacementAndCapacity",
        FrozenStructureCompilerRejectsInvalidIdentityPlacementAndCapacity),
    ("FrozenStructureOpenRejectsMalformedSectionPayloads",
        FrozenStructureOpenRejectsMalformedSectionPayloads),
    ("StaticDefinitionsBindBeforeGenerationPublication", StaticDefinitionsBindBeforeGenerationPublication),
    ("BaseTerrainQueriesHonorParityBoundariesAndSignedRounding",
        BaseTerrainQueriesHonorParityBoundariesAndSignedRounding),
    ("BaseTerrainQueriesOwnNegativeAndCrossChunkCoordinates",
        BaseTerrainQueriesOwnNegativeAndCrossChunkCoordinates),
    ("BaseTerrainMeshIsCanonicalBoundedAndMatchesQueries",
        BaseTerrainMeshIsCanonicalBoundedAndMatchesQueries),
    ("BaseTerrainMeshSharesExactFramesAcrossChunkSeams",
        BaseTerrainMeshSharesExactFramesAcrossChunkSeams),
    ("BaseTerrainMeshSharesExactFrameAtNegativeFourWayCorner",
        BaseTerrainMeshSharesExactFrameAtNegativeFourWayCorner),
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
    ("PublicEligibilityAndFinalPostStepTruthAreExplicit", PublicEligibilityAndFinalPostStepTruthAreExplicit),
    ("PublicationSnapshotDeltaAndNoChangeSemanticsAreDeterministic",
        PublicationSnapshotDeltaAndNoChangeSemanticsAreDeterministic),
    ("CrossChunkPublicationOrdersRemoveBeforePresentAndFencesReuse",
        CrossChunkPublicationOrdersRemoveBeforePresentAndFencesReuse),
    ("PublicationCursorLineageAndChunkRecreationAreFenced",
        PublicationCursorLineageAndChunkRecreationAreFenced),
    ("PublicGenerationSupersessionIsGlobalAcrossChunkPages",
        PublicGenerationSupersessionIsGlobalAcrossChunkPages),
    ("PublicationHistoryLossFallsBackWithoutRejectingCurrentState",
        PublicationHistoryLossFallsBackWithoutRejectingCurrentState),
    ("PublicationSemanticEncodingIsCanonicalTransactionalAndAllocationFree",
        PublicationSemanticEncodingIsCanonicalTransactionalAndAllocationFree),
    ("PublicationCapacityMustAddressEcsSlots", PublicationCapacityMustAddressEcsSlots),
    ("PublicationStoreMatchesReferenceModelUnderBoundedChurn",
        PublicationStoreMatchesReferenceModelUnderBoundedChurn),
    ("ChunkInterestBoundaryCornerAndCanonicalOrderingAreExact",
        ChunkInterestBoundaryCornerAndCanonicalOrderingAreExact),
    ("ChunkInterestHysteresisResistsOneMillimeterOscillation",
        ChunkInterestHysteresisResistsOneMillimeterOscillation),
    ("ChunkInterestCapacityAndInvalidInputAreExplicit",
        ChunkInterestCapacityAndInvalidInputAreExplicit),
    ("ChunkInterestMatchesReferenceModelUnderBoundedChurn",
        ChunkInterestMatchesReferenceModelUnderBoundedChurn),
    ("LocalChunkStreamingOracleConvergesSnapshotsDeltasAndFallbacks",
        LocalChunkStreamingOracleConvergesSnapshotsDeltasAndFallbacks),
    ("ChunkInterestAndLocalNoChangeStreamingAllocateZero",
        ChunkInterestAndLocalNoChangeStreamingAllocateZero),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Terrain T0.0-T0.7 world tests passed.");

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
    Equal(1U, WorldPackFormat.FrozenStaticsSchemaVersion);
    Equal(1U, WorldPackFormat.MultiFragmentsSchemaVersion);
    Equal(16, Marshal.SizeOf<WorldContentFingerprint>());
    Equal(32, Marshal.SizeOf<WorldPackFingerprint>());
    Equal(1U, (uint)WorldPackSectionKind.TerrainMaterials);
    Equal(2U, (uint)WorldPackSectionKind.TerrainHeights);
    Equal(3U, (uint)WorldPackSectionKind.FrozenStatics);
    Equal(4U, (uint)WorldPackSectionKind.MultiFragments);
    Equal(4, Marshal.SizeOf<WorldStaticDefinitionId>());
    Equal(8, Marshal.SizeOf<WorldStaticId>());
    Equal(8, Marshal.SizeOf<MultiInstanceId>());
    Equal(4, Marshal.SizeOf<MultiPieceId>());
    Equal(2, Marshal.SizeOf<WorldYaw>());
    Equal(65_536, WorldYaw.UnitsPerTurn);
    Equal((ushort)16_384, WorldYaw.QuarterTurn.Units);
    Equal((ushort)32_768, WorldYaw.HalfTurn.Units);
    Equal((ushort)49_152, WorldYaw.ThreeQuarterTurn.Units);
    Equal((ushort)65_535, new WorldYaw(ushort.MaxValue).Units);
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
    Throws<ArgumentException>(() => _ = new WorldContentBinding(GameContent.WorldFingerprint, [1], [0U]));
    Throws<ArgumentException>(() => _ = new WorldContentBinding(GameContent.WorldFingerprint, [1], [2U, 2U]));
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

    WorldPack wrongSpaceGamePack = CreateWorldPackArtifact(
        reverse: true,
        GameContent.WorldFingerprint,
        WorldPackTestProfile.Space).Open();
    var wrongSpaceComposition = new UltraSimGameComposition(baseWorldPack: wrongSpaceGamePack);
    using (var wrongSpaceRuntime = new ServerRuntime(wrongSpaceComposition,
        new ServerRuntimeOptions(UltraSimGameComposition.CreateWorldOptions(), WorldClockOptions.Default)))
    {
        Throws<ArgumentException>(() => wrongSpaceRuntime.Initialize());
        Equal(ServerRuntimeState.Faulted, wrongSpaceRuntime.State);
    }

    WorldPack gamePack = CreateWorldPackArtifact(
        reverse: true,
        GameContent.WorldFingerprint,
        GameWorldState.DefaultSpace).Open();
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
        Equal(GameWorldState.DefaultSpace, game.WorldState.BaseWorld.Space);
        var location = new WorldPosition(-1, 0, 12_345, 6_789, 0);
        True(game.WorldState.BaseWorld.TrySampleBaseTerrainHeight(location, out int immutableHeight));
        True(game.WorldState.TrySampleBaseTerrainHeight(location, out int authoritativeHeight));
        Equal(immutableHeight, authoritativeHeight);
        True(game.WorldState.BaseWorld.TryResolveBaseTerrainMaterial(location, out ushort immutableMaterial));
        True(game.WorldState.TryResolveBaseTerrainMaterial(location, out ushort authoritativeMaterial));
        Equal(immutableMaterial, authoritativeMaterial);
        return true;
    }, out bool published));
    True(published);
}

static void FrozenStaticsAndStaticMultisCompileCanonically()
{
    WorldPackArtifact forward = CreateStructuredWorldPackArtifact(reverse: false);
    WorldPackArtifact reverse = CreateStructuredWorldPackArtifact(reverse: true);
    Equal(forward.Length, reverse.Length);
    Equal(forward.Fingerprint, reverse.Fingerprint);
    var first = new byte[forward.Length];
    var second = new byte[reverse.Length];
    forward.CopyTo(first);
    reverse.CopyTo(second);
    True(first.AsSpan().SequenceEqual(second));

    WorldPack pack = forward.Open();
    Equal(4, pack.SectionCount);
    Equal(3, pack.FrozenStaticCount);
    Equal(2, pack.MultiInstanceCount);
    Equal(4, pack.MultiFragmentCount);
    var zero = new SpatialChunkKey(pack.Space, 0, 0);
    True(pack.TryGetFrozenStatics(zero, out WorldFrozenStaticChunkView statics));
    Equal(2, statics.Count);
    Equal(new WorldStaticId(10), statics.GetRecord(0).Id);
    Equal(new WorldStaticId(30), statics.GetRecord(1).Id);
    Equal(WorldYaw.QuarterTurn, statics.GetRecord(1).Orientation);
    Equal(1, statics.GetRenderGroupCount());
    Span<WorldStaticRenderGroup> groups = stackalloc WorldStaticRenderGroup[1];
    True(statics.TryCopyRenderGroups(groups, out int required));
    Equal(1, required);
    Equal(2, groups[0].Count);

    True(pack.TryGetMultiFragments(zero, out WorldMultiFragmentChunkView fragments));
    Equal(2, fragments.Count);
    WorldMultiFragmentRecord roof = fragments.GetRecord(0);
    Equal(new MultiInstanceId(700), roof.MultiId);
    Equal(new MultiPieceId(3), roof.PieceId);
    Equal(WorldYaw.ThreeQuarterTurn, roof.Orientation);
    True(pack.TryGetMultiFragments(new SpatialChunkKey(pack.Space, -1, -1),
        out WorldMultiFragmentChunkView cabinLeft));
    True(pack.TryGetMultiFragments(new SpatialChunkKey(pack.Space, 0, -1),
        out WorldMultiFragmentChunkView cabinRight));
    Equal(new MultiInstanceId(700), cabinLeft.GetRecord(0).MultiId);
    Equal(new MultiInstanceId(700), cabinRight.GetRecord(0).MultiId);
    Equal(new MultiPieceId(1), cabinLeft.GetRecord(0).PieceId);
    Equal(new MultiPieceId(2), cabinRight.GetRecord(0).PieceId);
    True(pack.TryGetMultiInstance(new MultiInstanceId(700), out WorldMultiInstanceRecord cabin));
    Equal(3, cabin.PieceCount);
    Equal(new SpatialChunkCoord(-1, -1), cabin.Root.Chunk);
    Equal(WorldYaw.HalfTurn, cabin.Orientation);
    True(pack.TryGetMultiInstance(new MultiInstanceId(600), out WorldMultiInstanceRecord shelf));
    Equal(1, shelf.PieceCount);
    False(pack.TryGetMultiInstance(new MultiInstanceId(999), out _));
}

static void FrozenStructureSectionsAreIndependentlyOptional()
{
    WorldPackBuilder staticsOnly = CreateTerrainBuilder();
    True(staticsOnly.TryAddFrozenStatic(CreateFrozenStatic(1, 1,
        new WorldPosition(0, 0, 1_000, 1_000, 0)), out _));
    True(staticsOnly.TryBuild(out WorldPackArtifact? staticArtifact, out _));
    WorldPack staticPack = staticArtifact!.Open();
    Equal(3, staticPack.SectionCount);
    Equal(1, staticPack.FrozenStaticCount);
    Equal(0, staticPack.MultiInstanceCount);

    WorldPackBuilder multiOnly = CreateTerrainBuilder();
    var root = new WorldPosition(0, 0, 1_000, 1_000, 0);
    WorldMultiPieceSource[] pieces = [CreateMultiPiece(1, 3, root)];
    True(multiOnly.TryAddStaticMulti(new MultiInstanceId(1), root, WorldYaw.Zero, pieces, out _));
    True(multiOnly.TryBuild(out WorldPackArtifact? multiArtifact, out _));
    WorldPack multiPack = multiArtifact!.Open();
    Equal(3, multiPack.SectionCount);
    Equal(0, multiPack.FrozenStaticCount);
    Equal(1, multiPack.MultiInstanceCount);
    Equal(1, multiPack.MultiFragmentCount);
}

static void FrozenStructureReadsAndRenderGroupsAllocateZero()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(reverse: false).Open();
    var key = new SpatialChunkKey(pack.Space, 0, 0);
    var staticGroups = new WorldStaticRenderGroup[4];
    var multiGroups = new WorldStaticRenderGroup[4];
    ulong expected = Read(1_000);
    long before = GC.GetAllocatedBytesForCurrentThread();
    ulong actual = Read(100_000);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(expected != 0);
    True(actual != 0);

    ulong Read(int iterations)
    {
        ulong checksum = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            True(pack.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics));
            True(statics.TryCopyRenderGroups(staticGroups, out int staticRequired));
            True(pack.TryGetMultiFragments(key, out WorldMultiFragmentChunkView multis));
            True(multis.TryCopyRenderGroups(multiGroups, out int multiRequired));
            WorldFrozenStaticRecord value = statics.GetRecord(iteration % statics.Count);
            checksum = unchecked(checksum + value.Id.Value + (uint)value.Position.YMillimeters +
                (uint)staticRequired + (uint)multiRequired + multis.GetRecord(0).MultiId.Value);
        }
        return checksum;
    }
}

static void StaticBoundsQueryIsExactBoundedAndTransactional()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(reverse: false).Open();
    var query = new WorldStaticBounds(-500, -1_000, -500, 2_500, 3_000, 2_500);
    var hits = new WorldStaticQueryHit[16];
    True(pack.TryQueryStaticBounds(query, 16, hits, out int count, out int required,
        out WorldStaticQueryError error));
    Equal(WorldStaticQueryError.None, error);
    Equal(required, count);
    True(count >= 4);
    int expectedCount = count;
    True(hits.AsSpan(0, count).ToArray().Any(hit => hit.StaticId == new WorldStaticId(20)));
    True(hits.AsSpan(0, count).ToArray().Any(hit => hit.Kind == WorldStaticQueryKind.MultiFragment));

    WorldStaticQueryHit sentinel = hits[0];
    var tooSmall = new WorldStaticQueryHit[1] { sentinel };
    False(pack.TryQueryStaticBounds(query, 16, tooSmall, out count, out required, out error));
    Equal(0, count);
    True(required > tooSmall.Length);
    Equal(WorldStaticQueryError.DestinationTooSmall, error);
    Equal(sentinel, tooSmall[0]);

    False(pack.TryQueryStaticBounds(query, 8, hits, out count, out required, out error));
    Equal(0, count);
    Equal(0, required);
    Equal(WorldStaticQueryError.CandidateLimitExceeded, error);
    False(pack.TryQueryStaticBounds(default, 16, hits, out count, out required, out error));
    Equal(WorldStaticQueryError.InvalidBounds, error);

    using (RawSpatialFixture empty = new(lifecycleCapacity: 16))
    {
        False(empty.State.TryQueryStaticBounds(default, 16, hits, out count, out required, out error));
        Equal(0, count);
        Equal(0, required);
        Equal(WorldStaticQueryError.InvalidBounds, error);
        Throws<ArgumentOutOfRangeException>(() => QueryWithNonpositiveCandidateBudget(empty));
        True(empty.State.TryQueryStaticBounds(query, 16, hits, out count, out required, out error));
        Equal(0, count);
        Equal(0, required);
        Equal(WorldStaticQueryError.None, error);
    }

    WorldPack gamePack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    var binding = new WorldContentBinding(GameContent.WorldFingerprint, [1, 2, 3, 4, 5],
        [1U, 2U, 3U, 4U, 5U]);
    True(gamePack.TryBind(binding, out BoundWorldPack? bound, out _));
    using RawSpatialFixture fixture = new(lifecycleCapacity: 16, baseWorld: bound);
    True(fixture.State.TryQueryStaticBounds(query, 16, hits, out int gameCount, out int gameRequired,
        out error));
    Equal(expectedCount, gameRequired);
    Equal(gameRequired, gameCount);

    static void QueryWithNonpositiveCandidateBudget(RawSpatialFixture fixture)
    {
        var destination = new WorldStaticQueryHit[1];
        var valid = new WorldStaticBounds(0, 0, 0, 1, 1, 1);
        fixture.State.TryQueryStaticBounds(valid, 0, destination, out _, out _, out _);
    }
}

static void FrozenStructureCompilerRejectsInvalidIdentityPlacementAndCapacity()
{
    WorldPackBuilder duplicate = CreateTerrainBuilder();
    WorldFrozenStaticSource source = CreateFrozenStatic(1, 1, new WorldPosition(0, 0, 1_000, 1_000, 0));
    True(duplicate.TryAddFrozenStatic(source, out _));
    False(duplicate.TryAddFrozenStatic(source, out WorldPackBuildError error));
    Equal(WorldPackBuildError.DuplicateStaticId, error);

    var position = new WorldPosition(0, 0, 1_000, 1_000, 0);
    var farBounds = new WorldStaticBounds(100_000, 0, 100_000, 101_000, 1_000, 101_000);
    var far = source with { Id = new WorldStaticId(2), Bounds = farBounds };
    False(duplicate.TryAddFrozenStatic(far, out error));
    Equal(WorldPackBuildError.InvalidStaticPlacement, error);
    False(duplicate.TryAddFrozenStatic(source with
    {
        Id = new WorldStaticId(3),
        Bounds = default,
    }, out error));
    Equal(WorldPackBuildError.InvalidStaticPlacement, error);

    WorldPackBuilder missing = CreateTerrainBuilder();
    True(missing.TryAddFrozenStatic(CreateFrozenStatic(9, 1,
        new WorldPosition(20, 20, 1_000, 1_000, 0)), out _));
    False(missing.TryBuild(out _, out error));
    Equal(WorldPackBuildError.MissingChunk, error);

    WorldPackBuilder malformedMulti = CreateTerrainBuilder();
    WorldMultiPieceSource piece = CreateMultiPiece(1, 3, new WorldPosition(0, 0, 1_000, 1_000, 0));
    WorldMultiPieceSource[] duplicatePieces = [piece, piece];
    False(malformedMulti.TryAddStaticMulti(new MultiInstanceId(1), position, WorldYaw.Zero,
        duplicatePieces, out error));
    Equal(WorldPackBuildError.InvalidMulti, error);

    var tight = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 1, 8, maximumFrozenStatics: 1,
            maximumMultiInstances: 1, maximumMultiFragments: 1));
    CreatePackPlanes(0, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights);
    True(tight.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, out _));
    True(tight.TryAddFrozenStatic(source, out _));
    False(tight.TryAddFrozenStatic(source with { Id = new WorldStaticId(8) }, out error));
    Equal(WorldPackBuildError.CapacityExceeded, error);

    var sectionLimited = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 1, 2, maximumFrozenStatics: 1));
    True(sectionLimited.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, out _));
    True(sectionLimited.TryAddFrozenStatic(source, out _));
    False(sectionLimited.TryBuild(out _, out error));
    Equal(WorldPackBuildError.CapacityExceeded, error);
}

static void FrozenStructureOpenRejectsMalformedSectionPayloads()
{
    WorldPackArtifact artifact = CreateStructuredWorldPackArtifact(reverse: false);
    var valid = new byte[artifact.Length];
    artifact.CopyTo(valid);
    var limits = new WorldPackLimits(4 * 1024 * 1024, 64, 8);
    int chunks = BinaryPrimitives.ReadInt32LittleEndian(valid.AsSpan(64));
    int sections = WorldPackFormat.HeaderBytes + chunks * WorldPackFormat.ChunkDirectoryRowBytes;
    int staticRow = sections + 2 * WorldPackFormat.SectionDirectoryRowBytes;
    int multiRow = sections + 3 * WorldPackFormat.SectionDirectoryRowBytes;

    byte[] badStatic = (byte[])valid.Clone();
    int staticPayload = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(badStatic.AsSpan(staticRow + 8)));
    badStatic[staticPayload + 24] = 1;
    RewriteSectionDigest(badStatic, staticRow);
    RewritePackFingerprint(badStatic);
    AssertPackOpenFails(badStatic, limits, WorldPackOpenError.InvalidFrozenStaticPayload);

    byte[] badMulti = (byte[])valid.Clone();
    int multiPayload = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(badMulti.AsSpan(multiRow + 8)));
    badMulti[multiPayload + 36] = 1;
    RewriteSectionDigest(badMulti, multiRow);
    RewritePackFingerprint(badMulti);
    AssertPackOpenFails(badMulti, limits, WorldPackOpenError.InvalidMultiPayload);

    byte[] unsupported = (byte[])valid.Clone();
    BinaryPrimitives.WriteUInt32LittleEndian(unsupported.AsSpan(staticRow + 4), 2);
    RewritePackFingerprint(unsupported);
    AssertPackOpenFails(unsupported, limits, WorldPackOpenError.UnsupportedRequiredSection);
}

static void StaticDefinitionsBindBeforeGenerationPublication()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    var incomplete = new WorldContentBinding(GameContent.WorldFingerprint,
        [GameTerrainMaterialIds.Water, GameTerrainMaterialIds.Sand, GameTerrainMaterialIds.Grass,
            GameTerrainMaterialIds.Rock, GameTerrainMaterialIds.Snow],
        [GameWorldStaticDefinitionIds.Tree]);
    False(pack.TryBind(incomplete, out BoundWorldPack? bound, out WorldPackBindingError error));
    Equal(null, bound);
    Equal(WorldPackBindingError.UnknownStaticDefinition, error);

    var composition = new UltraSimGameComposition(baseWorldPack: pack);
    using var runtime = new ServerRuntime(composition,
        new ServerRuntimeOptions(UltraSimGameComposition.CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((_, _, game) =>
    {
        Equal(3, game.WorldState.BaseWorld!.FrozenStaticCount);
        Equal(2, game.WorldState.BaseWorld.MultiInstanceCount);
        var query = new WorldStaticBounds(-500, -1_000, -500, 2_500, 3_000, 2_500);
        Span<WorldStaticQueryHit> hits = stackalloc WorldStaticQueryHit[16];
        True(game.WorldState.TryQueryStaticBounds(query, 16, hits, out int count, out int required,
            out WorldStaticQueryError queryError));
        Equal(WorldStaticQueryError.None, queryError);
        Equal(required, count);
        True(count > 0);
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

static void BaseTerrainMeshSharesExactFrameAtNegativeFourWayCorner()
{
    BoundWorldPack world = CreateBoundTerrainWorld(-2, 1, -2, 1, SmoothHeight, QueryMaterial, reverse: true);
    SpatialChunkKey[] keys =
    [
        new(world.Space, -1, -1),
        new(world.Space, 0, -1),
        new(world.Space, -1, 0),
        new(world.Space, 0, 0),
    ];
    TerrainVertexCoord[] coordinates =
    [
        new(32, 32),
        new(0, 32),
        new(32, 0),
        new(0, 0),
    ];

    TerrainBaseMeshVertex expected = default;
    for (int index = 0; index < keys.Length; index++)
    {
        var vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
        var materials = new ushort[TerrainChunkFormat.TriangleCount];
        True(world.TryBuildBaseTerrainMesh(keys[index], vertices, indices, materials, out _, out _));
        TerrainBaseMeshVertex actual = vertices[TerrainChunkFormat.GetHeightIndex(coordinates[index])];
        Equal(0L, checked((long)keys[index].Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
            actual.LocalXMillimeters));
        Equal(0L, checked((long)keys[index].Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits +
            actual.LocalZMillimeters));
        if (index == 0)
        {
            expected = actual;
            continue;
        }
        Equal(expected.AbsoluteYMillimeters, actual.AbsoluteYMillimeters);
        Equal(expected.NormalX, actual.NormalX);
        Equal(expected.NormalY, actual.NormalY);
        Equal(expected.NormalZ, actual.NormalZ);
        Equal(expected.TangentX, actual.TangentX);
        Equal(expected.TangentY, actual.TangentY);
        Equal(expected.TangentZ, actual.TangentZ);
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

static WorldPackArtifact CreateStructuredWorldPackArtifact(
    bool reverse,
    WorldContentFingerprint? content = null,
    SpatialSpaceId? space = null)
{
    WorldPackBuilder builder = CreateTerrainBuilder(content, space);
    WorldFrozenStaticSource[] statics =
    [
        CreateFrozenStatic(30, 1, new WorldPosition(0, 0, 1_000, 1_000, 0)) with
        {
            Orientation = WorldYaw.QuarterTurn,
        },
        CreateFrozenStatic(10, 1, new WorldPosition(0, 0, 2_000, 2_000, 0)),
        CreateFrozenStatic(20, 2, new WorldPosition(-1, 0, 15_900, 200, 0), halfExtent: 300),
    ];
    WorldMultiPieceSource[] cabin =
    [
        CreateMultiPiece(1, 3, new WorldPosition(-1, -1, 15_500, 15_500, 0)),
        CreateMultiPiece(2, 3, new WorldPosition(0, -1, 500, 15_500, 0)),
        CreateMultiPiece(3, 4, new WorldPosition(0, 0, 500, 500, 2_000)) with
        {
            Orientation = WorldYaw.ThreeQuarterTurn,
        },
    ];
    WorldMultiPieceSource[] shelf =
    [
        CreateMultiPiece(1, 5, new WorldPosition(0, 0, 1_500, 1_500, 0)),
    ];
    if (reverse)
    {
        Array.Reverse(statics);
        Array.Reverse(cabin);
        True(builder.TryAddStaticMulti(new MultiInstanceId(700),
            new WorldPosition(-1, -1, 15_500, 15_500, 0), WorldYaw.HalfTurn, cabin, out _));
        True(builder.TryAddStaticMulti(new MultiInstanceId(600),
            new WorldPosition(0, 0, 1_500, 1_500, 0), WorldYaw.Zero, shelf, out _));
    }
    else
    {
        True(builder.TryAddStaticMulti(new MultiInstanceId(600),
            new WorldPosition(0, 0, 1_500, 1_500, 0), WorldYaw.Zero, shelf, out _));
        True(builder.TryAddStaticMulti(new MultiInstanceId(700),
            new WorldPosition(-1, -1, 15_500, 15_500, 0), WorldYaw.HalfTurn, cabin, out _));
    }
    foreach (WorldFrozenStaticSource value in statics)
        True(builder.TryAddFrozenStatic(value, out _));
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error));
    Equal(WorldPackBuildError.None, error);
    True(artifact is not null);
    return artifact!;
}

static WorldPackBuilder CreateTerrainBuilder(
    WorldContentFingerprint? content = null,
    SpatialSpaceId? space = null)
{
    SpatialSpaceId selectedSpace = space ?? WorldPackTestProfile.Space;
    var builder = new WorldPackBuilder(selectedSpace, content ?? WorldPackTestProfile.Content,
        new WorldPackLimits(4 * 1024 * 1024, 64, 8));
    for (int z = -2; z < 2; z++)
    {
        for (int x = -2; x < 2; x++)
        {
            CreatePackPlanes(x, z, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
                out TerrainHeightOffset[] heights);
            True(builder.TryAddBaseTerrain(new SpatialChunkKey(selectedSpace, x, z),
                materials, heightBase, heights, out _));
        }
    }
    return builder;
}

static WorldFrozenStaticSource CreateFrozenStatic(
    ulong id,
    uint definition,
    WorldPosition position,
    int halfExtent = 200) => new(
        new WorldStaticId(id),
        new WorldStaticDefinitionId(definition),
        position,
        WorldYaw.Zero,
        0,
        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
        BoundsAround(position, halfExtent));

static WorldMultiPieceSource CreateMultiPiece(
    uint piece,
    uint definition,
    WorldPosition position,
    int halfExtent = 400) => new(
        new MultiPieceId(piece),
        new WorldStaticDefinitionId(definition),
        position,
        WorldYaw.Zero,
        0,
        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
        BoundsAround(position, halfExtent));

static WorldStaticBounds BoundsAround(WorldPosition position, int halfExtent) => new(
    checked(position.GlobalXMillimeters - halfExtent),
    checked((long)position.YMillimeters - halfExtent),
    checked(position.GlobalZMillimeters - halfExtent),
    checked(position.GlobalXMillimeters + halfExtent),
    checked((long)position.YMillimeters + halfExtent),
    checked(position.GlobalZMillimeters + halfExtent));

static WorldPackArtifact CreateWorldPackArtifact(
    bool reverse,
    WorldContentFingerprint? content = null,
    SpatialSpaceId? space = null)
{
    SpatialSpaceId selectedSpace = space ?? WorldPackTestProfile.Space;
    var builder = new WorldPackBuilder(selectedSpace,
        content ?? WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 64, 8));
    var keys = new List<SpatialChunkKey>();
    for (int z = -2; z < 2; z++)
        for (int x = -2; x < 2; x++)
            keys.Add(new SpatialChunkKey(selectedSpace, x, z));
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
    Equal(9, schema.ComponentCount);
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
    PublicationEpoch priorPublicationEpoch = fixture.State.Publications.Epoch;
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
    True(snapshot.Publication.Epoch.Value > priorPublicationEpoch.Value);
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
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity entity));
    True(fixture.State.TrySetPosition(fixture.World, entity, new WorldPosition(1, 2, 3, 4, 5)));
    True(fixture.State.TrySetPublic(fixture.World, entity,
        new WorldPublicDescriptor(91, WorldYaw.QuarterTurn, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    WorldSpatialIndex published = fixture.State.Index;
    ChunkPublicationStore publications = fixture.State.Publications;
    PublicationEpoch epoch = publications.Epoch;
    SpatialChunkKey publicationKey =
        new(GameWorldState.DefaultSpace, new SpatialChunkCoord(1, 2));
    var beforeRows = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        publications.TryCopySnapshot(publicationKey, beforeRows, out ChunkPublicationBatch before));
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
    True(ReferenceEquals(publications, fixture.State.Publications));
    Equal(epoch, fixture.State.Publications.Epoch);
    True(published.TryGetHome(entity, out SpatialChunkKey oldHome));
    Equal(new SpatialChunkCoord(1, 2), oldHome.Coord);
    var afterRows = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        publications.TryCopySnapshot(publicationKey, afterRows, out ChunkPublicationBatch after));
    Equal(before.Cursor, after.Cursor);
    True(beforeRows.AsSpan().SequenceEqual(afterRows));
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

static void PublicEligibilityAndFinalPostStepTruthAreExplicit()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 64);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity entity));
    True(fixture.State.TrySetPosition(
        fixture.World, entity, new WorldPosition(2, -3, 10, 20, 30)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(0, fixture.State.Publications.GetSnapshot().PublicEntityCount);

    True(fixture.World.TryTransition(entity, fixture.AddPublic));
    Throws<InvalidOperationException>(() => fixture.State.CommitAfterEcsStep(fixture.World));
    Equal(0, fixture.State.Publications.GetSnapshot().PublicEntityCount);
    True(fixture.State.Index.TryGetHome(entity, out _));
    for (int index = 0; index < 10; index++)
    {
        True(fixture.State.TrySetPosition(fixture.World, entity,
            new WorldPosition(2, -3, checked((ushort)(100 + index)), 200, index)));
        True(fixture.State.TrySetPublic(fixture.World, entity,
            new WorldPublicDescriptor(77, new WorldYaw(checked((ushort)(1_000 + index))),
                WorldPublicStateFlags.Visible)));
    }
    fixture.State.CommitAfterEcsStep(fixture.World);

    var records = new WorldPublicRecord[1];
    SpatialChunkKey key = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(2, -3));
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(key, records, out ChunkPublicationBatch batch));
    Equal(1, batch.Count);
    Equal(77U, records[0].PrefabId);
    Equal(new WorldPosition(2, -3, 109, 200, 9), records[0].Position);
    Equal(new WorldYaw(1_009), records[0].Orientation);
    Equal(WorldPublicStateFlags.Visible, records[0].StateFlags);
    Equal(1UL, records[0].EntityPublicVersion);
    True(records[0].Id.IsValid);
    Equal(1, fixture.State.Publications.GetSnapshot().PublicEntityCount);
    WorldPublicEntityId publicId = records[0].Id;
    True(fixture.World.TryTransition(entity, fixture.RemovePublic));
    fixture.State.CommitAfterEcsStep(fixture.World);
    var removed = new WorldPublicDeltaRecord[1];
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(key, batch.Cursor, removed, out _));
    Equal(WorldPublicOperation.Remove, removed[0].Operation);
    Equal(publicId, removed[0].Id);
    Equal(2UL, removed[0].EntityPublicVersion);
    Equal(0, fixture.State.Publications.GetSnapshot().PublicEntityCount);
}

static void PublicationSnapshotDeltaAndNoChangeSemanticsAreDeterministic()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 64);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity entity));
    True(fixture.State.TrySetPosition(
        fixture.World, entity, new WorldPosition(-1, 4, 100, 200, 300)));
    True(fixture.State.TrySetPublic(fixture.World, entity,
        new WorldPublicDescriptor(9, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey key = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(-1, 4));
    var snapshot = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(key, snapshot, out ChunkPublicationBatch initial));
    fixture.State.CommitAfterEcsStep(fixture.World);
    var delta = new WorldPublicDeltaRecord[1];
    Equal(ChunkPublicationReadStatus.NoChanges,
        fixture.State.Publications.TryCopyDelta(key, initial.Cursor, delta, out ChunkPublicationBatch none));
    Equal(initial.Cursor, none.Cursor);

    True(fixture.State.TrySetPosition(
        fixture.World, entity, new WorldPosition(-1, 4, 150, 250, 350)));
    True(fixture.State.TrySetPublic(fixture.World, entity,
        new WorldPublicDescriptor(9, WorldYaw.QuarterTurn, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(key, initial.Cursor, delta, out ChunkPublicationBatch changed));
    Equal(1, changed.Count);
    Equal(WorldPublicOperation.Present, delta[0].Operation);
    Equal(WorldPublicFieldMask.Transform | WorldPublicFieldMask.Orientation, delta[0].Fields);
    Equal(2UL, delta[0].EntityPublicVersion);
    Equal(new WorldPosition(-1, 4, 150, 250, 350), delta[0].Current.Position);
    Equal(WorldYaw.QuarterTurn, delta[0].Current.Orientation);
    True(changed.Cursor.Revision.Value > initial.Cursor.Revision.Value);
}

static void CrossChunkPublicationOrdersRemoveBeforePresentAndFencesReuse()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 64, entityCapacity: 4);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity first));
    True(fixture.State.TrySetPosition(
        fixture.World, first, new WorldPosition(0, 0, 1, 2, 3)));
    True(fixture.State.TrySetPublic(fixture.World, first,
        new WorldPublicDescriptor(41, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey source = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(0, 0));
    SpatialChunkKey target = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(1, 0));
    var records = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(source, records, out ChunkPublicationBatch sourceBefore));
    WorldPublicRecord original = records[0];

    True(fixture.State.TrySetPosition(
        fixture.World, first, new WorldPosition(1, 0, 4, 5, 6)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    var deltas = new WorldPublicDeltaRecord[2];
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(source, sourceBefore.Cursor, deltas,
            out ChunkPublicationBatch sourceAfter));
    Equal(1, sourceAfter.Count);
    Equal(WorldPublicOperation.Remove, deltas[0].Operation);
    Equal(original.Id, deltas[0].Id);
    Equal(2UL, deltas[0].EntityPublicVersion);
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(target, records, out ChunkPublicationBatch targetBefore));
    Equal(original.Id, records[0].Id);
    Equal(3UL, records[0].EntityPublicVersion);
    WorldPublicRecord moved = records[0];
    var oracle = new PublicOracleState(original, present: true);
    ApplyPublicOracle(ref oracle, new WorldPublicDeltaRecord(
        WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
        moved.Id, moved.EntityPublicVersion, moved));
    ApplyPublicOracle(ref oracle, deltas[0]);
    True(oracle.Present);
    Equal(moved.Position, oracle.Record.Position);
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(source, sourceBefore.Cursor, deltas, out sourceAfter));
    Equal(WorldPublicOperation.Remove, deltas[0].Operation);

    True(fixture.World.TryDestroy(first));
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity replacement));
    Equal(first.Index, replacement.Index);
    True(replacement.Generation > first.Generation);
    True(fixture.State.TrySetPosition(
        fixture.World, replacement, new WorldPosition(1, 0, 7, 8, 9)));
    True(fixture.State.TrySetPublic(fixture.World, replacement,
        new WorldPublicDescriptor(42, WorldYaw.HalfTurn, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(target, targetBefore.Cursor, deltas,
            out ChunkPublicationBatch replacementBatch));
    Equal(1, replacementBatch.Count);
    Equal(WorldPublicOperation.Present, deltas[0].Operation);
    Equal(original.Id.Index, deltas[0].Id.Index);
    True(deltas[0].Id.Generation > original.Id.Generation);
    Equal(1UL, deltas[0].EntityPublicVersion);
    Equal(42U, deltas[0].Current.PrefabId);
    ApplyPublicOracle(ref oracle, deltas[0]);
    ApplyPublicOracle(ref oracle, new WorldPublicDeltaRecord(
        WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
        moved.Id, moved.EntityPublicVersion, moved));
    True(oracle.Present);
    Equal(42U, oracle.Record.PrefabId);
    Equal(deltas[0].Id, oracle.Record.Id);
}

static void PublicationCursorLineageAndChunkRecreationAreFenced()
{
    Equal(32, Marshal.SizeOf<ChunkPublicationCursor>());
    Equal(48, ChunkPublicationSemanticCodec.HeaderBytes);
    var limits = new ChunkPublicationLimits(
        MaximumPublicEntities: 2,
        MaximumResidentChunks: 4,
        RowsPerPage: 1,
        HistoryEntryCapacity: 16);
    using RawSpatialFixture fixture = new(
        lifecycleCapacity: 32, entityCapacity: 2, publicationLimits: limits);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity entity));
    var descriptor = new WorldPublicDescriptor(101, WorldYaw.Zero, WorldPublicStateFlags.Visible);
    True(fixture.State.TrySetPosition(fixture.World, entity, new WorldPosition(0, 0, 1, 2, 3)));
    True(fixture.State.TrySetPublic(fixture.World, entity, descriptor));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey originalKey = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(0, 0));
    var rows = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(
            originalKey, rows, out ChunkPublicationBatch original));

    for (int chunkX = 1; chunkX <= 4; chunkX++)
    {
        True(fixture.State.TrySetPosition(
            fixture.World, entity, new WorldPosition(chunkX, 0, 1, 2, chunkX)));
        fixture.State.CommitAfterEcsStep(fixture.World);
    }

    Equal(ChunkPublicationReadStatus.ChunkAbsent,
        fixture.State.Publications.TryCopySnapshot(
            originalKey, rows, out ChunkPublicationBatch absent));
    Equal(ChunkPublicationReadStatus.ChunkAbsent, absent.Status);
    False(absent.Cursor.IsValid);
    Equal(0, absent.Count);

    True(fixture.State.TrySetPosition(fixture.World, entity, new WorldPosition(0, 0, 7, 8, 9)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    var deltas = new WorldPublicDeltaRecord[1];
    Equal(ChunkPublicationReadStatus.SnapshotRequired,
        fixture.State.Publications.TryCopyDelta(originalKey, original.Cursor, deltas, out _));

    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(
            originalKey, rows, out ChunkPublicationBatch recreated));
    True(recreated.HistoryFloor.IsValid);
    True(recreated.HistoryFloor.Value > original.Cursor.Revision.Value);
    Equal(recreated.Cursor.Revision, recreated.HistoryFloor);
    Equal(ChunkPublicationReadStatus.NoChanges,
        fixture.State.Publications.TryCopyDelta(originalKey, recreated.Cursor, deltas, out _));

    SpatialChunkKey priorKey = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(4, 0));
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(priorKey, rows, out ChunkPublicationBatch prior));
    Equal(0, prior.Count);
    Equal(ChunkPublicationReadStatus.SnapshotRequired,
        fixture.State.Publications.TryCopyDelta(originalKey, prior.Cursor, deltas, out _));
}

static void PublicGenerationSupersessionIsGlobalAcrossChunkPages()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 32, entityCapacity: 2);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity first));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(-1, 0, 1, 2, 3)));
    True(fixture.State.TrySetPublic(fixture.World, first,
        new WorldPublicDescriptor(111, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey oldKey = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(-1, 0));
    var rows = new WorldPublicRecord[1];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(oldKey, rows, out _));
    WorldPublicRecord oldPresent = rows[0];

    True(fixture.World.TryDestroy(first));
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity replacement));
    Equal(first.Index, replacement.Index);
    True(replacement.Generation > first.Generation);
    True(fixture.State.TrySetPosition(fixture.World, replacement, new WorldPosition(2, 0, 4, 5, 6)));
    True(fixture.State.TrySetPublic(fixture.World, replacement,
        new WorldPublicDescriptor(112, WorldYaw.HalfTurn, WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey newKey = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(2, 0));
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(newKey, rows, out _));
    WorldPublicRecord newPresent = rows[0];
    Equal(oldPresent.Id.Index, newPresent.Id.Index);
    True(newPresent.Id.Generation > oldPresent.Id.Generation);

    var pages = new Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)>();
    var floors = new Dictionary<uint, PublicationConsumerFloor>();
    ApplyPublicationConsumerPresent(pages, floors, oldKey, oldPresent);
    ApplyPublicationConsumerPresent(pages, floors, newKey, newPresent);
    ApplyPublicationConsumerPresent(pages, floors, oldKey, oldPresent);
    ApplyPublicationConsumerDelta(pages, floors, oldKey, new WorldPublicDeltaRecord(
        WorldPublicOperation.Remove,
        WorldPublicFieldMask.None,
        oldPresent.Id,
        checked(oldPresent.EntityPublicVersion + 1),
        default));

    Equal(1, pages.Count);
    True(pages.TryGetValue(newPresent.Id.Index, out var retained));
    Equal(newKey, retained.Key);
    Equal(newPresent, retained.Record);
}

static void PublicationHistoryLossFallsBackWithoutRejectingCurrentState()
{
    var limits = new ChunkPublicationLimits(
        MaximumPublicEntities: 4,
        MaximumResidentChunks: 8,
        RowsPerPage: 2,
        HistoryEntryCapacity: 2);
    using RawSpatialFixture fixture = new(
        lifecycleCapacity: 64, entityCapacity: 4, publicationLimits: limits);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity first));
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity second));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(0, 0, 1, 1, 1)));
    True(fixture.State.TrySetPosition(fixture.World, second, new WorldPosition(0, 0, 2, 2, 2)));
    var descriptor = new WorldPublicDescriptor(5, WorldYaw.Zero, WorldPublicStateFlags.Visible);
    True(fixture.State.TrySetPublic(fixture.World, first, descriptor));
    True(fixture.State.TrySetPublic(fixture.World, second, descriptor));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey source = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(0, 0));
    var sourceRows = new WorldPublicRecord[2];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(source, sourceRows, out ChunkPublicationBatch before));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(1, 0, 3, 3, 3)));
    True(fixture.State.TrySetPosition(fixture.World, second, new WorldPosition(2, 0, 4, 4, 4)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    var deltas = new WorldPublicDeltaRecord[4];
    Equal(ChunkPublicationReadStatus.SnapshotRequired,
        fixture.State.Publications.TryCopyDelta(source, before.Cursor, deltas, out _));
    ChunkPublicationStoreSnapshot state = fixture.State.Publications.GetSnapshot();
    Equal(2, state.PublicEntityCount);
    Equal(1UL, state.HistoryInvalidations);
    Equal(4, state.LargestDirtyCommit);
    var destinationRows = new WorldPublicRecord[1];
    SpatialChunkKey firstDestination =
        new(GameWorldState.DefaultSpace, new SpatialChunkCoord(1, 0));
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(
            firstDestination, destinationRows, out ChunkPublicationBatch afterOverflow));
    True(destinationRows[0].Id.IsValid);
    Equal(new WorldPosition(1, 0, 3, 3, 3), destinationRows[0].Position);

    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(1, 0, 5, 5, 5)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(
            firstDestination, afterOverflow.Cursor, deltas, out _));
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(1, 0, 6, 6, 6)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(1, 0, 7, 7, 7)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.SnapshotRequired,
        fixture.State.Publications.TryCopyDelta(
            firstDestination, afterOverflow.Cursor, deltas, out _));
}

static void PublicationSemanticEncodingIsCanonicalTransactionalAndAllocationFree()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 64, entityCapacity: 4);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity first));
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity second));
    var descriptor = new WorldPublicDescriptor(11, WorldYaw.ThreeQuarterTurn,
        WorldPublicStateFlags.Visible);
    True(fixture.State.TrySetPosition(fixture.World, first, new WorldPosition(-2, -2, 8, 9, 10)));
    True(fixture.State.TrySetPosition(fixture.World, second, new WorldPosition(-2, -2, 6, 7, 8)));
    True(fixture.State.TrySetPublic(fixture.World, first, descriptor));
    True(fixture.State.TrySetPublic(fixture.World, second, descriptor));
    fixture.State.CommitAfterEcsStep(fixture.World);

    SpatialChunkKey key = new(GameWorldState.DefaultSpace, new SpatialChunkCoord(-2, -2));
    var records = new WorldPublicRecord[2];
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(key, records, out ChunkPublicationBatch batch));
    True(records[0].Id.CompareTo(records[1].Id) < 0);
    var bytes = new byte[ChunkPublicationSemanticCodec.MeasureSnapshot(records.Length)];
    True(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, bytes, out int written));
    Equal(bytes.Length, written);
    var repeated = new byte[bytes.Length];
    True(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, repeated, out _));
    True(bytes.AsSpan().SequenceEqual(repeated));

    var tooSmall = new byte[bytes.Length - 1];
    Array.Fill(tooSmall, (byte)0xA5);
    False(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, tooSmall, out int rejected));
    Equal(0, rejected);
    True(tooSmall.AsSpan().IndexOfAnyExcept((byte)0xA5) < 0);
    (records[0], records[1]) = (records[1], records[0]);
    Array.Fill(repeated, (byte)0x5A);
    False(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, repeated, out rejected));
    Equal(0, rejected);
    True(repeated.AsSpan().IndexOfAnyExcept((byte)0x5A) < 0);
    (records[0], records[1]) = (records[1], records[0]);

    int sourceBytes = checked(Marshal.SizeOf<WorldPublicRecord>() * records.Length);
    var overlapping = new byte[Math.Max(bytes.Length, sourceBytes)];
    Span<WorldPublicRecord> overlappingRecords =
        MemoryMarshal.Cast<byte, WorldPublicRecord>(overlapping.AsSpan(0, sourceBytes));
    records.CopyTo(overlappingRecords);
    byte[] overlapBefore = overlapping.ToArray();
    False(ChunkPublicationSemanticCodec.TryWriteSnapshot(
        batch, overlappingRecords, overlapping, out rejected));
    Equal(0, rejected);
    True(overlapping.AsSpan().SequenceEqual(overlapBefore));

    for (int index = 0; index < 1_000; index++)
    {
        True(fixture.State.TrySetPosition(fixture.World, first,
            new WorldPosition(-2, -2, checked((ushort)(100 + (index & 255))), 9, index)));
        fixture.State.CommitAfterEcsStep(fixture.World);
        Equal(ChunkPublicationReadStatus.Snapshot,
            fixture.State.Publications.TryCopySnapshot(key, records, out batch));
        True(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, bytes, out _));
        True(fixture.State.Publications.TryGetCommittedView(key, out ChunkPublicationView warmView));
        foreach (ChunkPublicationPageView page in warmView)
            True(page.Count > 0);
    }
    long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
    ulong checksum = 0;
    for (int index = 0; index < 100_000; index++)
    {
        True(fixture.State.TrySetPosition(fixture.World, first,
            new WorldPosition(-2, -2, checked((ushort)(100 + (index & 255))), 9, index)));
        fixture.State.CommitAfterEcsStep(fixture.World);
        Equal(ChunkPublicationReadStatus.Snapshot,
            fixture.State.Publications.TryCopySnapshot(key, records, out batch));
        True(ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, records, bytes, out _));
        checksum += bytes[0];
        True(fixture.State.Publications.TryGetCommittedView(key, out ChunkPublicationView view));
        foreach (ChunkPublicationPageView page in view)
            checksum += checked((ulong)page.Count);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
    Equal(0L, allocated);
    True(checksum != 0);
}

static void PublicationCapacityMustAddressEcsSlots()
{
    using var world = new EcsWorld(
        GameSchema.CreateSchema(),
        new EcsWorldOptions(
            new EntityCapacity(8),
            maximumArchetypes: 2,
            maximumPreparedPlans: 8,
            lifecycleRecordCapacity: 16));
    var undersized = new ChunkPublicationLimits(
        MaximumPublicEntities: 4,
        MaximumResidentChunks: 8,
        RowsPerPage: 2,
        HistoryEntryCapacity: 8);
    Throws<ArgumentException>(() => _ = new GameWorldState(world, publicationLimits: undersized));
}

static void PublicationStoreMatchesReferenceModelUnderBoundedChurn()
{
    const int entityCapacity = 8;
    const int operations = 20_000;
    var limits = new ChunkPublicationLimits(
        MaximumPublicEntities: entityCapacity,
        MaximumResidentChunks: entityCapacity * 2,
        RowsPerPage: 2,
        HistoryEntryCapacity: 6);
    using RawSpatialFixture fixture = new(
        lifecycleCapacity: 64,
        entityCapacity: entityCapacity,
        publicationLimits: limits);
    var random = new Random(0x5EED_6006);
    var actors = new PublicationChurnActor[entityCapacity];
    var knownKeys = new HashSet<SpatialChunkKey>();
    var cursors = new Dictionary<SpatialChunkKey, ChunkPublicationCursor>();
    var consumer = new Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)>();
    var consumerFloors = new Dictionary<uint, PublicationConsumerFloor>();
    var rows = new WorldPublicRecord[entityCapacity];
    var deltas = new WorldPublicDeltaRecord[entityCapacity];
    uint nextPrefab = 1_000;

    for (int index = 0; index < 4; index++)
        CreateChurnActor(fixture, actors, knownKeys, random, ref nextPrefab, makePublic: true);
    fixture.State.CommitAfterEcsStep(fixture.World);
    AssertPublicationTruth(fixture.State.Publications, actors, knownKeys, rows);
    SynchronizePublicationConsumer(
        fixture.State.Publications, actors, knownKeys, cursors, consumer,
        consumerFloors, rows, deltas, random);

    for (int operation = 0; operation < operations; operation++)
    {
        int mutations = 1 + random.Next(3);
        for (int mutation = 0; mutation < mutations; mutation++)
            MutateChurnActor(fixture, actors, knownKeys, random, ref nextPrefab);

        if (operation % 257 == 0)
        {
            for (int index = 0; index < actors.Length; index++)
            {
                ref PublicationChurnActor actor = ref actors[index];
                if (!actor.Alive || !actor.Public) continue;
                actor.Position = RandomChurnPosition(random, actor.Position, forceCrossChunk: true);
                knownKeys.Add(actor.Position.ToChunkKey(GameWorldState.DefaultSpace));
                True(fixture.State.TrySetPosition(fixture.World, actor.Entity, actor.Position));
            }
        }

        fixture.State.CommitAfterEcsStep(fixture.World);
        AssertPublicationTruth(fixture.State.Publications, actors, knownKeys, rows);
        if (operation % 17 == 0)
        {
            SynchronizePublicationConsumer(
                fixture.State.Publications, actors, knownKeys, cursors, consumer,
                consumerFloors, rows, deltas, random);
        }
    }

    SynchronizePublicationConsumer(
        fixture.State.Publications, actors, knownKeys, cursors, consumer,
        consumerFloors, rows, deltas, random);
    ChunkPublicationStoreSnapshot snapshot = fixture.State.Publications.GetSnapshot();
    True(snapshot.HistoryInvalidations > 0);
    True(snapshot.SnapshotFallbacks > 0);
    True(snapshot.LargestDirtyCommit > limits.HistoryEntryCapacity);
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

static bool CreateChurnActor(
    RawSpatialFixture fixture,
    PublicationChurnActor[] actors,
    HashSet<SpatialChunkKey> knownKeys,
    Random random,
    ref uint nextPrefab,
    bool makePublic)
{
    int free = FindChurnActor(actors, random, requireAlive: false);
    if (free < 0) return false;
    ArchetypeHandle archetype = makePublic
        ? fixture.PublicSpatialArchetype
        : fixture.SpatialArchetype;
    True(fixture.World.TryCreate(archetype, out Entity entity));
    ref PublicationChurnActor actor = ref actors[entity.Index];
    actor.Entity = entity;
    actor.Alive = true;
    actor.Public = makePublic;
    actor.Position = RandomChurnPosition(random, default, forceCrossChunk: true);
    actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
    knownKeys.Add(actor.Position.ToChunkKey(GameWorldState.DefaultSpace));
    True(fixture.State.TrySetPosition(fixture.World, entity, actor.Position));
    if (makePublic)
        True(fixture.State.TrySetPublic(fixture.World, entity, actor.Descriptor));
    return true;
}

static void MutateChurnActor(
    RawSpatialFixture fixture,
    PublicationChurnActor[] actors,
    HashSet<SpatialChunkKey> knownKeys,
    Random random,
    ref uint nextPrefab)
{
    int action = random.Next(7);
    if (action == 0)
    {
        _ = CreateChurnActor(
            fixture, actors, knownKeys, random, ref nextPrefab, random.Next(2) == 0);
        return;
    }

    int index = FindChurnActor(actors, random, requireAlive: true);
    if (index < 0)
    {
        _ = CreateChurnActor(fixture, actors, knownKeys, random, ref nextPrefab, makePublic: true);
        return;
    }

    ref PublicationChurnActor actor = ref actors[index];
    switch (action)
    {
        case 1:
            True(fixture.World.TryDestroy(actor.Entity));
            actor.Alive = false;
            actor.Public = false;
            break;
        case 2:
            if (actor.Public)
            {
                True(fixture.World.TryTransition(actor.Entity, fixture.RemovePublic));
                actor.Public = false;
            }
            else
            {
                True(fixture.World.TryTransition(actor.Entity, fixture.AddPublic));
                actor.Public = true;
                actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
                True(fixture.State.TrySetPublic(fixture.World, actor.Entity, actor.Descriptor));
            }
            break;
        case 3:
        {
            int writes = 1 + random.Next(5);
            for (int write = 0; write < writes; write++)
            {
                actor.Position = RandomChurnPosition(
                    random, actor.Position, forceCrossChunk: write == writes - 1 && random.Next(2) == 0);
                knownKeys.Add(actor.Position.ToChunkKey(GameWorldState.DefaultSpace));
                True(fixture.State.TrySetPosition(fixture.World, actor.Entity, actor.Position));
            }
            break;
        }
        case 4:
            if (actor.Public)
            {
                int writes = 1 + random.Next(4);
                for (int write = 0; write < writes; write++)
                {
                    actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
                    True(fixture.State.TrySetPublic(fixture.World, actor.Entity, actor.Descriptor));
                }
            }
            break;
        case 5:
        {
            Entity retired = actor.Entity;
            True(fixture.World.TryDestroy(retired));
            bool makePublic = random.Next(2) == 0;
            True(fixture.World.TryCreate(
                makePublic ? fixture.PublicSpatialArchetype : fixture.SpatialArchetype,
                out Entity replacement));
            Equal(retired.Index, replacement.Index);
            True(replacement.Generation > retired.Generation);
            actor.Entity = replacement;
            actor.Alive = true;
            actor.Public = makePublic;
            actor.Position = RandomChurnPosition(random, default, forceCrossChunk: true);
            actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
            knownKeys.Add(actor.Position.ToChunkKey(GameWorldState.DefaultSpace));
            True(fixture.State.TrySetPosition(fixture.World, replacement, actor.Position));
            if (makePublic)
                True(fixture.State.TrySetPublic(fixture.World, replacement, actor.Descriptor));
            break;
        }
        default:
            actor.Position = RandomChurnPosition(random, actor.Position, forceCrossChunk: false);
            knownKeys.Add(actor.Position.ToChunkKey(GameWorldState.DefaultSpace));
            True(fixture.State.TrySetPosition(fixture.World, actor.Entity, actor.Position));
            if (actor.Public)
            {
                actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
                True(fixture.State.TrySetPublic(fixture.World, actor.Entity, actor.Descriptor));
            }
            break;
    }
}

static int FindChurnActor(PublicationChurnActor[] actors, Random random, bool requireAlive)
{
    int start = random.Next(actors.Length);
    for (int offset = 0; offset < actors.Length; offset++)
    {
        int index = (start + offset) % actors.Length;
        if (actors[index].Alive == requireAlive)
            return index;
    }
    return -1;
}

static WorldPosition RandomChurnPosition(
    Random random,
    in WorldPosition previous,
    bool forceCrossChunk)
{
    int chunkX;
    int chunkZ;
    bool hasPrevious = previous != default;
    if (!forceCrossChunk && hasPrevious && random.Next(3) == 0)
    {
        chunkX = previous.Chunk.X;
        chunkZ = previous.Chunk.Z;
    }
    else
    {
        do
        {
            chunkX = random.Next(-4, 5);
            chunkZ = random.Next(-4, 5);
        }
        while (forceCrossChunk && hasPrevious &&
               chunkX == previous.Chunk.X && chunkZ == previous.Chunk.Z);
    }
    return new WorldPosition(
        chunkX,
        chunkZ,
        checked((ushort)random.Next(GameWorldSpatialProfile.WorldChunkEdgeUnits)),
        checked((ushort)random.Next(GameWorldSpatialProfile.WorldChunkEdgeUnits)),
        random.Next(-100_000, 100_001));
}

static WorldPublicDescriptor NextChurnDescriptor(Random random, ref uint nextPrefab)
{
    uint prefab = nextPrefab++;
    if (prefab == 0) throw new InvalidOperationException("Churn prefab identity wrapped.");
    return new WorldPublicDescriptor(
        prefab,
        new WorldYaw(checked((ushort)random.Next(ushort.MaxValue + 1))),
        random.Next(2) == 0 ? WorldPublicStateFlags.None : WorldPublicStateFlags.Visible);
}

static void AssertPublicationTruth(
    ChunkPublicationStore store,
    PublicationChurnActor[] actors,
    HashSet<SpatialChunkKey> knownKeys,
    WorldPublicRecord[] rows)
{
    var actual = new Dictionary<uint, WorldPublicRecord>();
    foreach (SpatialChunkKey key in knownKeys)
    {
        ChunkPublicationReadStatus status = store.TryCopySnapshot(key, rows, out ChunkPublicationBatch batch);
        if (status == ChunkPublicationReadStatus.ChunkAbsent)
            continue;
        Equal(ChunkPublicationReadStatus.Snapshot, status);
        Equal(key, batch.Cursor.Key);
        for (int row = 0; row < batch.Count; row++)
            True(actual.TryAdd(rows[row].PrefabId, rows[row]));
    }

    int expectedCount = 0;
    for (int index = 0; index < actors.Length; index++)
    {
        ref PublicationChurnActor actor = ref actors[index];
        if (!actor.Alive || !actor.Public)
        {
            actor.ObservedPublic = false;
            continue;
        }
        expectedCount++;
        True(actual.TryGetValue(actor.Descriptor.PrefabId, out WorldPublicRecord record));
        Equal(actor.Position, record.Position);
        Equal(actor.Descriptor.PrefabId, record.PrefabId);
        Equal(actor.Descriptor.Orientation, record.Orientation);
        Equal(actor.Descriptor.Flags, record.StateFlags);
        True(record.LastChangedRevision.IsValid);
        if (actor.ObservedPublic)
        {
            if (actor.LastObservedEntity == actor.Entity)
            {
                Equal(actor.LastObservedId, record.Id);
            }
            else
            {
                Equal(actor.LastObservedId.Index, record.Id.Index);
                True(record.Id.Generation > actor.LastObservedId.Generation);
            }
        }
        else if (actor.LastObservedId.IsValid && actor.LastObservedId.Index == record.Id.Index)
        {
            True(record.Id.Generation > actor.LastObservedId.Generation);
        }
        actor.LastObservedEntity = actor.Entity;
        actor.LastObservedId = record.Id;
        actor.ObservedPublic = true;
    }
    Equal(expectedCount, actual.Count);
    Equal(expectedCount, store.GetSnapshot().PublicEntityCount);
}

static void SynchronizePublicationConsumer(
    ChunkPublicationStore store,
    PublicationChurnActor[] actors,
    HashSet<SpatialChunkKey> knownKeys,
    Dictionary<SpatialChunkKey, ChunkPublicationCursor> cursors,
    Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)> consumer,
    Dictionary<uint, PublicationConsumerFloor> floors,
    WorldPublicRecord[] rows,
    WorldPublicDeltaRecord[] deltas,
    Random random)
{
    SpatialChunkKey[] order = knownKeys.ToArray();
    random.Shuffle(order);
    if (cursors.Count >= 2)
    {
        KeyValuePair<SpatialChunkKey, ChunkPublicationCursor>[] retained = cursors.Take(2).ToArray();
        if (retained[0].Key != retained[1].Key)
        {
            Equal(ChunkPublicationReadStatus.SnapshotRequired,
                store.TryCopyDelta(retained[1].Key, retained[0].Value, deltas, out _));
        }
    }

    foreach (SpatialChunkKey key in order)
    {
        ChunkPublicationReadStatus status;
        ChunkPublicationBatch batch;
        if (cursors.TryGetValue(key, out ChunkPublicationCursor cursor))
        {
            status = store.TryCopyDelta(key, cursor, deltas, out batch);
            if (status == ChunkPublicationReadStatus.Delta)
            {
                for (int index = 0; index < batch.Count; index++)
                    ApplyPublicationConsumerDelta(consumer, floors, key, deltas[index]);
                cursors[key] = batch.Cursor;
                continue;
            }
            if (status == ChunkPublicationReadStatus.NoChanges)
            {
                cursors[key] = batch.Cursor;
                continue;
            }
            Equal(ChunkPublicationReadStatus.SnapshotRequired, status);
        }

        status = store.TryCopySnapshot(key, rows, out batch);
        ClearPublicationConsumerKey(consumer, key);
        if (status == ChunkPublicationReadStatus.ChunkAbsent)
        {
            cursors.Remove(key);
            continue;
        }
        Equal(ChunkPublicationReadStatus.Snapshot, status);
        for (int index = 0; index < batch.Count; index++)
            ApplyPublicationConsumerPresent(consumer, floors, key, rows[index]);
        cursors[key] = batch.Cursor;
    }

    var byPrefab = new Dictionary<uint, WorldPublicRecord>();
    foreach ((_, WorldPublicRecord record) in consumer.Values)
        True(byPrefab.TryAdd(record.PrefabId, record));
    int expected = 0;
    for (int index = 0; index < actors.Length; index++)
    {
        ref readonly PublicationChurnActor actor = ref actors[index];
        if (!actor.Alive || !actor.Public) continue;
        expected++;
        True(byPrefab.TryGetValue(actor.Descriptor.PrefabId, out WorldPublicRecord record));
        Equal(actor.Position, record.Position);
        Equal(actor.Descriptor.Orientation, record.Orientation);
        Equal(actor.Descriptor.Flags, record.StateFlags);
    }
    Equal(expected, byPrefab.Count);
}

static void ChunkInterestBoundaryCornerAndCanonicalOrderingAreExact()
{
    var destination = new ChunkInterestEntry[1_024];
    ChunkInterestProfile profile = GameChunkInterestProfile.Default;
    SpatialSpaceId space = GameWorldState.DefaultSpace;

    WorldPosition middle = new(0, 0, 8_000, 8_000, 0);
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, middle, profile, [], destination, out int middleCount,
            out int middleRequired));
    Equal(middleCount, middleRequired);
    Equal(1, CountTier(destination.AsSpan(0, middleCount), ChunkInterestTier.Immediate));
    AssertCanonicalInterest(destination.AsSpan(0, middleCount));

    WorldPosition boundary = new(0, 0, 12_000, 8_000, 0);
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, boundary, profile, [], destination, out int boundaryCount, out _));
    Equal(2, CountTier(destination.AsSpan(0, boundaryCount), ChunkInterestTier.Immediate));
    True(FindInterest(destination.AsSpan(0, boundaryCount), new SpatialChunkKey(space, 0, 0), out _) >= 0);
    True(FindInterest(destination.AsSpan(0, boundaryCount), new SpatialChunkKey(space, 1, 0), out _) >= 0);

    WorldPosition sameTopology = new(0, 0, 15_900, 8_000, 0);
    var later = new ChunkInterestEntry[1_024];
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, sameTopology, profile, destination.AsSpan(0, boundaryCount), later,
            out int laterCount, out _));
    Equal(2, CountTier(later.AsSpan(0, laterCount), ChunkInterestTier.Immediate));
    True(FindInterest(later.AsSpan(0, laterCount), new SpatialChunkKey(space, 0, 0), out
        ChunkInterestEntry retainedHome) >= 0);
    Equal(ChunkInterestTier.Immediate, retainedHome.Tier);
    True(FindInterest(later.AsSpan(0, laterCount), new SpatialChunkKey(space, 1, 0), out
        ChunkInterestEntry retainedNeighbor) >= 0);
    Equal(ChunkInterestTier.Immediate, retainedNeighbor.Tier);

    WorldPosition corner = new(0, 0, 15_999, 15_999, 0);
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, corner, profile, [], destination, out int cornerCount, out _));
    Equal(4, CountTier(destination.AsSpan(0, cornerCount), ChunkInterestTier.Immediate));
    True(FindInterest(destination.AsSpan(0, cornerCount), new SpatialChunkKey(space, 0, 0), out _) >= 0);
    True(FindInterest(destination.AsSpan(0, cornerCount), new SpatialChunkKey(space, 0, 1), out _) >= 0);
    True(FindInterest(destination.AsSpan(0, cornerCount), new SpatialChunkKey(space, 1, 0), out _) >= 0);
    True(FindInterest(destination.AsSpan(0, cornerCount), new SpatialChunkKey(space, 1, 1), out _) >= 0);
    AssertCanonicalInterest(destination.AsSpan(0, cornerCount));

    WorldPosition negativeCorner = new(-1, -1, 15_999, 15_999, 0);
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, negativeCorner, profile, [], destination, out int negativeCount, out _));
    Equal(4, CountTier(destination.AsSpan(0, negativeCount), ChunkInterestTier.Immediate));
    True(FindInterest(destination.AsSpan(0, negativeCount), new SpatialChunkKey(space, -1, -1), out _) >= 0);
    True(FindInterest(destination.AsSpan(0, negativeCount), new SpatialChunkKey(space, 0, 0), out _) >= 0);
    AssertCanonicalInterest(destination.AsSpan(0, negativeCount));
}

static void ChunkInterestHysteresisResistsOneMillimeterOscillation()
{
    var current = new ChunkInterestEntry[1_024];
    var next = new ChunkInterestEntry[1_024];
    SpatialSpaceId space = GameWorldState.DefaultSpace;
    ChunkInterestProfile profile = GameChunkInterestProfile.Default;
    SpatialChunkKey immediateTarget = new(space, 1, 0);

    QueryInterest(new WorldPosition(0, 0, 11_000, 8_000, 0), [], current, out int count);
    Equal(ChunkInterestTier.Immediate, InterestTier(current.AsSpan(0, count), immediateTarget));
    QueryInterest(new WorldPosition(0, 0, 10_999, 8_000, 0), current.AsSpan(0, count), next, out int nextCount);
    Equal(ChunkInterestTier.Immediate, InterestTier(next.AsSpan(0, nextCount), immediateTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(0, 0, 10_500, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Immediate, InterestTier(next.AsSpan(0, nextCount), immediateTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(0, 0, 10_499, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Near, InterestTier(next.AsSpan(0, nextCount), immediateTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(0, 0, 10_500, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Near, InterestTier(next.AsSpan(0, nextCount), immediateTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(0, 0, 11_000, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Immediate, InterestTier(next.AsSpan(0, nextCount), immediateTarget));

    SpatialChunkKey nearTarget = new(space, 6, 0);
    QueryInterest(new WorldPosition(0, 0, 0, 8_000, 0), [], current, out count);
    Equal(ChunkInterestTier.Near, InterestTier(current.AsSpan(0, count), nearTarget));
    QueryInterest(new WorldPosition(-1, 0, 15_999, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Near, InterestTier(next.AsSpan(0, nextCount), nearTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(-1, 0, 15_500, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Near, InterestTier(next.AsSpan(0, nextCount), nearTarget));
    Swap(ref current, ref next, ref count, nextCount);
    QueryInterest(new WorldPosition(-1, 0, 15_499, 8_000, 0), current.AsSpan(0, count), next, out nextCount);
    Equal(ChunkInterestTier.Far, InterestTier(next.AsSpan(0, nextCount), nearTarget));

    void QueryInterest(
        in WorldPosition anchor,
        ReadOnlySpan<ChunkInterestEntry> previous,
        Span<ChunkInterestEntry> output,
        out int resultCount)
    {
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(space, anchor, profile, previous, output, out resultCount, out int required));
        Equal(resultCount, required);
    }
}

static void ChunkInterestCapacityAndInvalidInputAreExplicit()
{
    SpatialSpaceId space = GameWorldState.DefaultSpace;
    WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
    var complete = new ChunkInterestEntry[1_024];
    Equal(ChunkInterestQueryStatus.Success,
        ChunkInterestQuery.TryQuery(space, anchor, GameChunkInterestProfile.Default, [], complete,
            out int fullCount, out int fullRequired));
    Equal(fullCount, fullRequired);
    var sentinel = new ChunkInterestEntry(new SpatialChunkKey(space, 77, 88), ChunkInterestTier.Far);
    var small = Enumerable.Repeat(sentinel, fullCount - 1).ToArray();
    Equal(ChunkInterestQueryStatus.DestinationTooSmall,
        ChunkInterestQuery.TryQuery(space, anchor, GameChunkInterestProfile.Default, [], small,
            out int count, out int required));
    Equal(0, count);
    Equal(fullCount, required);
    // A sizing failure makes the destination explicitly unusable; no partial count is published.

    ChunkInterestEntry[] invalidPrevious =
    [
        new(new SpatialChunkKey(space, 0, 0), ChunkInterestTier.Immediate),
        new(new SpatialChunkKey(space, 0, 0), ChunkInterestTier.Near),
    ];
    var untouched = Enumerable.Repeat(sentinel, 4).ToArray();
    Equal(ChunkInterestQueryStatus.InvalidInput,
        ChunkInterestQuery.TryQuery(space, anchor, GameChunkInterestProfile.Default, invalidPrevious, untouched,
            out count, out required));
    Equal(0, count);
    Equal(0, required);
    True(untouched.All(value => value == sentinel));
    Equal(ChunkInterestQueryStatus.InvalidInput,
        ChunkInterestQuery.TryQuery(default, anchor, GameChunkInterestProfile.Default, [], untouched,
            out count, out required));

    var source = new ChunkPublicationStore(PublicationEpoch.Create(), new ChunkPublicationLimits(1, 2, 1, 1));
    var constrained = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(8, 1),
        GameChunkInterestProfile.Default,
        LocalChunkStreamingCadence.EvidenceProfile);
    Equal(LocalChunkStreamingStatus.SubscriptionCapacityExceeded,
        constrained.Synchronize(source, space, anchor, 0, out int oracleRequired));
    True(oracleRequired > 8);
    Equal(0, constrained.GetSnapshot().SubscriptionCount);
    Equal(0, constrained.GetSnapshot().ResidentRecordCount);
}

static void ChunkInterestMatchesReferenceModelUnderBoundedChurn()
{
    const int operations = 2_000;
    SpatialSpaceId space = GameWorldState.DefaultSpace;
    ChunkInterestProfile profile = GameChunkInterestProfile.Default;
    var current = new ChunkInterestEntry[1_024];
    var next = new ChunkInterestEntry[1_024];
    int currentCount = 0;
    long x = 8_000;
    long z = 8_000;
    var random = new Random(0x70_07_2026);
    for (int operation = 0; operation < operations; operation++)
    {
        if (operation % 211 == 0)
        {
            x = random.Next(-12, 13) * (long)GameWorldSpatialProfile.WorldChunkEdgeUnits +
                random.Next(0, GameWorldSpatialProfile.WorldChunkEdgeUnits);
            z = random.Next(-12, 13) * (long)GameWorldSpatialProfile.WorldChunkEdgeUnits +
                random.Next(0, GameWorldSpatialProfile.WorldChunkEdgeUnits);
        }
        else
        {
            x += random.Next(-600, 601);
            z += random.Next(-600, 601);
        }
        True(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition anchor));
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(space, anchor, profile, current.AsSpan(0, currentCount), next,
                out int nextCount, out int required));
        Equal(nextCount, required);
        AssertCanonicalInterest(next.AsSpan(0, nextCount));

        var previous = new Dictionary<SpatialChunkKey, ChunkInterestTier>();
        for (int index = 0; index < currentCount; index++)
            previous.Add(current[index].Key, current[index].Tier);
        var expected = new List<ChunkInterestEntry>();
        int homeX = anchor.Chunk.X;
        int homeZ = anchor.Chunk.Z;
        for (int chunkX = homeX - 14; chunkX <= homeX + 14; chunkX++)
        {
            for (int chunkZ = homeZ - 14; chunkZ <= homeZ + 14; chunkZ++)
            {
                var key = new SpatialChunkKey(space, chunkX, chunkZ);
                previous.TryGetValue(key, out ChunkInterestTier prior);
                Int128 squared = GameWorldSpatialProfile.ChunkGeometry.GetBounds(key.Coord)
                    .SquaredDistanceTo(anchor.GlobalXMillimeters, anchor.GlobalZMillimeters);
                ChunkInterestTier? tier = ReferenceInterestTier(squared, prior, profile);
                if (tier.HasValue) expected.Add(new ChunkInterestEntry(key, tier.Value));
            }
        }
        expected.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        Equal(expected.Count, nextCount);
        for (int index = 0; index < nextCount; index++) Equal(expected[index], next[index]);
        (current, next) = (next, current);
        currentCount = nextCount;
    }
}

static ChunkInterestTier? ReferenceInterestTier(
    Int128 distanceSquared,
    ChunkInterestTier previous,
    in ChunkInterestProfile profile)
{
    static bool Within(Int128 squared, int radius)
    {
        Int128 wide = radius;
        return squared <= wide * wide;
    }
    if (previous == ChunkInterestTier.Immediate)
    {
        if (Within(distanceSquared, profile.ImmediateLeaveRadiusMillimeters)) return ChunkInterestTier.Immediate;
        if (Within(distanceSquared, profile.NearLeaveRadiusMillimeters)) return ChunkInterestTier.Near;
        if (Within(distanceSquared, profile.FarLeaveRadiusMillimeters)) return ChunkInterestTier.Far;
        return null;
    }
    if (previous == ChunkInterestTier.Near)
    {
        if (Within(distanceSquared, profile.ImmediateEnterRadiusMillimeters)) return ChunkInterestTier.Immediate;
        if (Within(distanceSquared, profile.NearLeaveRadiusMillimeters)) return ChunkInterestTier.Near;
        if (Within(distanceSquared, profile.FarLeaveRadiusMillimeters)) return ChunkInterestTier.Far;
        return null;
    }
    if (previous == ChunkInterestTier.Far)
    {
        if (Within(distanceSquared, profile.ImmediateEnterRadiusMillimeters)) return ChunkInterestTier.Immediate;
        if (Within(distanceSquared, profile.NearEnterRadiusMillimeters)) return ChunkInterestTier.Near;
        if (Within(distanceSquared, profile.FarLeaveRadiusMillimeters)) return ChunkInterestTier.Far;
        return null;
    }
    if (Within(distanceSquared, profile.ImmediateEnterRadiusMillimeters)) return ChunkInterestTier.Immediate;
    if (Within(distanceSquared, profile.NearEnterRadiusMillimeters)) return ChunkInterestTier.Near;
    if (Within(distanceSquared, profile.FarEnterRadiusMillimeters)) return ChunkInterestTier.Far;
    return null;
}

static void LocalChunkStreamingOracleConvergesSnapshotsDeltasAndFallbacks()
{
    var publicationLimits = new ChunkPublicationLimits(32, 64, 8, 2);
    using var fixture = new RawSpatialFixture(64, 32, publicationLimits: publicationLimits);
    var descriptor = new WorldPublicDescriptor(71, WorldYaw.Zero, WorldPublicStateFlags.Visible);
    True(fixture.World.TryCreate(fixture.PublicSpatialArchetype, out Entity entity));
    True(fixture.World.TryWrite(entity, fixture.RawPositionLookup,
        new WorldPositionComponent(new WorldPosition(0, 0, 1_000, 1_000, 10))));
    True(fixture.World.TryWrite(entity, fixture.RawPublicLookup, new WorldPublicComponent(descriptor)));
    fixture.State.CommitAfterEcsStep(fixture.World);

    var oracle = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(1_024, 32),
        GameChunkInterestProfile.Default,
        LocalChunkStreamingCadence.EvidenceProfile);
    WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 0, out int required));
    True(required > 100);
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);
    LocalChunkSubscription initialHome = FindSubscription(oracle.Subscriptions,
        new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0));
    True(initialHome.Cursor.IsValid);
    ClientSemanticChunkSlot stableSlot = initialHome.ResidentSlot;

    descriptor = descriptor with { Orientation = WorldYaw.QuarterTurn };
    True(fixture.World.TryWrite(entity, fixture.RawPublicLookup, new WorldPublicComponent(descriptor)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 1, out _));
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);
    Equal(stableSlot, FindSubscription(oracle.Subscriptions,
        new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0)).ResidentSlot);

    // More dirty facts than the two-entry history can retain force the oracle through snapshot recovery.
    for (int step = 0; step < 3; step++)
    {
        descriptor = descriptor with { Orientation = new WorldYaw(checked((ushort)(20_000 + step))) };
        True(fixture.World.TryWrite(entity, fixture.RawPublicLookup, new WorldPublicComponent(descriptor)));
        fixture.State.CommitAfterEcsStep(fixture.World);
    }
    ulong fallbacksBefore = oracle.GetSnapshot().SnapshotFallbacks;
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 2, out _));
    True(oracle.GetSnapshot().SnapshotFallbacks > fallbacksBefore);
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);

    True(fixture.State.TrySetPosition(fixture.World, entity,
        new WorldPosition(1, 0, 2_000, 1_000, 11)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 3, out _));
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);

    PublicationEpoch oldEpoch = oracle.GetSnapshot().SourceEpoch;
    fixture.State.Rebuild(fixture.World);
    True(fixture.State.Publications.Epoch != oldEpoch);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 4, out _));
    Equal(fixture.State.Publications.Epoch, oracle.GetSnapshot().SourceEpoch);
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);

    WorldPosition remoteAnchor = new(100, 0, 8_000, 8_000, 0);
    ChunkPublicationStoreSnapshot beforeLeave = fixture.State.Publications.GetSnapshot();
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, remoteAnchor, 5, out _));
    Equal(0, oracle.GetSnapshot().ResidentRecordCount);
    ChunkPublicationStoreSnapshot afterLeave = fixture.State.Publications.GetSnapshot();
    Equal(beforeLeave.PublicEntityCount, afterLeave.PublicEntityCount);
    Equal(beforeLeave.CommitCount, afterLeave.CommitCount);
    Equal(beforeLeave.PublishedChunkRevisions, afterLeave.PublishedChunkRevisions);
}

static void ChunkInterestAndLocalNoChangeStreamingAllocateZero()
{
    using var fixture = new RawSpatialFixture(64, 32);
    var oracle = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(1_024, 32),
        GameChunkInterestProfile.Default,
        LocalChunkStreamingCadence.EvidenceProfile);
    WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 0, out _));
    var previous = new ChunkInterestEntry[1_024];
    var destination = new ChunkInterestEntry[1_024];
    for (int index = 0; index < oracle.Subscriptions.Length; index++)
        previous[index] = oracle.Subscriptions[index].Interest;
    int previousCount = oracle.Subscriptions.Length;

    for (int warmup = 0; warmup < 32; warmup++)
    {
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(GameWorldState.DefaultSpace, anchor, GameChunkInterestProfile.Default,
                previous.AsSpan(0, previousCount), destination, out _, out _));
        Equal(LocalChunkStreamingStatus.Synchronized,
            oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor,
                checked((ulong)warmup + 1), out _));
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int iteration = 0; iteration < 1_000; iteration++)
    {
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(GameWorldState.DefaultSpace, anchor, GameChunkInterestProfile.Default,
                previous.AsSpan(0, previousCount), destination, out int count, out int required));
        Equal(count, required);
        Equal(LocalChunkStreamingStatus.Synchronized,
            oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor,
                checked((ulong)iteration + 100), out _));
    }
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static int CountTier(ReadOnlySpan<ChunkInterestEntry> entries, ChunkInterestTier tier)
{
    int count = 0;
    for (int index = 0; index < entries.Length; index++)
        if (entries[index].Tier == tier) count++;
    return count;
}

static void AssertCanonicalInterest(ReadOnlySpan<ChunkInterestEntry> entries)
{
    for (int index = 0; index < entries.Length; index++)
    {
        True(entries[index].IsValid);
        if (index != 0) True(entries[index - 1].Key.CompareTo(entries[index].Key) < 0);
    }
}

static int FindInterest(
    ReadOnlySpan<ChunkInterestEntry> entries,
    SpatialChunkKey key,
    out ChunkInterestEntry entry)
{
    for (int index = 0; index < entries.Length; index++)
    {
        if (entries[index].Key != key) continue;
        entry = entries[index];
        return index;
    }
    entry = default;
    return -1;
}

static ChunkInterestTier InterestTier(ReadOnlySpan<ChunkInterestEntry> entries, SpatialChunkKey key)
{
    True(FindInterest(entries, key, out ChunkInterestEntry entry) >= 0);
    return entry.Tier;
}

static void Swap(
    ref ChunkInterestEntry[] current,
    ref ChunkInterestEntry[] next,
    ref int currentCount,
    int nextCount)
{
    (current, next) = (next, current);
    currentCount = nextCount;
}

static LocalChunkSubscription FindSubscription(
    ReadOnlySpan<LocalChunkSubscription> subscriptions,
    SpatialChunkKey key)
{
    for (int index = 0; index < subscriptions.Length; index++)
        if (subscriptions[index].Interest.Key == key) return subscriptions[index];
    throw new InvalidOperationException($"Subscription {key} was not found.");
}

static void AssertOracleMatchesInterestedPublication(
    ChunkPublicationStore source,
    LocalChunkStreamingOracle oracle)
{
    var expected = new Dictionary<uint, LocalChunkSemanticRecord>();
    var rows = new WorldPublicRecord[source.Limits.MaximumPublicEntities];
    foreach (LocalChunkSubscription subscription in oracle.Subscriptions)
    {
        ChunkPublicationReadStatus status = source.TryCopySnapshot(subscription.Interest.Key, rows,
            out ChunkPublicationBatch batch);
        if (status == ChunkPublicationReadStatus.ChunkAbsent) continue;
        Equal(ChunkPublicationReadStatus.Snapshot, status);
        for (int index = 0; index < batch.Count; index++)
        {
            WorldPublicRecord record = rows[index];
            if (expected.TryGetValue(record.Id.Index, out LocalChunkSemanticRecord retained) &&
                retained.Record.Id.Generation > record.Id.Generation)
                continue;
            expected[record.Id.Index] = new LocalChunkSemanticRecord(subscription.Interest.Key, record);
        }
    }
    var actual = new LocalChunkSemanticRecord[source.Limits.MaximumPublicEntities];
    True(oracle.TryCopyResidentRecords(actual, out int count, out int required));
    Equal(expected.Count, required);
    Equal(expected.Count, count);
    for (int index = 0; index < count; index++)
    {
        True(expected.TryGetValue(actual[index].Record.Id.Index, out LocalChunkSemanticRecord record));
        Equal(record, actual[index]);
    }
}

static void ClearPublicationConsumerKey(
    Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)> consumer,
    SpatialChunkKey key)
{
    uint[] stale = consumer
        .Where(pair => pair.Value.Key == key)
        .Select(static pair => pair.Key)
        .ToArray();
    for (int index = 0; index < stale.Length; index++)
        consumer.Remove(stale[index]);
}

static void ApplyPublicationConsumerPresent(
    Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)> consumer,
    Dictionary<uint, PublicationConsumerFloor> floors,
    SpatialChunkKey key,
    in WorldPublicRecord record) =>
    ApplyPublicationConsumerDelta(consumer, floors, key, new WorldPublicDeltaRecord(
        WorldPublicOperation.Present,
        WorldPublicFieldMask.Complete,
        record.Id,
        record.EntityPublicVersion,
        record));

static void ApplyPublicationConsumerDelta(
    Dictionary<uint, (SpatialChunkKey Key, WorldPublicRecord Record)> consumer,
    Dictionary<uint, PublicationConsumerFloor> floors,
    SpatialChunkKey key,
    in WorldPublicDeltaRecord delta)
{
    if (floors.TryGetValue(delta.Id.Index, out PublicationConsumerFloor floor))
    {
        int generation = delta.Id.Generation.CompareTo(floor.Generation);
        if (generation < 0 || generation == 0 && delta.EntityPublicVersion < floor.Version)
            return;
        if (generation == 0 && delta.EntityPublicVersion == floor.Version)
        {
            if (delta.Operation != floor.Operation) return;
            if (delta.Operation == WorldPublicOperation.Remove) return;
            if (consumer.TryGetValue(delta.Id.Index, out var retained))
            {
                Equal(key, retained.Key);
                Equal(delta.Current, retained.Record);
                return;
            }
        }
    }

    consumer.Remove(delta.Id.Index);
    floors[delta.Id.Index] = new PublicationConsumerFloor(
        delta.Id.Generation, delta.EntityPublicVersion, delta.Operation);
    if (delta.Operation == WorldPublicOperation.Present)
        consumer[delta.Id.Index] = (key, delta.Current);
}

static void ApplyPublicOracle(ref PublicOracleState state, in WorldPublicDeltaRecord delta)
{
    int generation = delta.Id.Generation.CompareTo(state.Record.Id.Generation);
    if (generation < 0 || generation == 0 && delta.EntityPublicVersion <= state.Record.EntityPublicVersion)
        return;
    if (delta.Operation == WorldPublicOperation.Remove)
    {
        state.Record = state.Record with
        {
            Id = delta.Id,
            EntityPublicVersion = delta.EntityPublicVersion,
        };
        state.Present = false;
    }
    else
    {
        state.Record = delta.Current;
        state.Present = true;
    }
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

readonly record struct PublicationConsumerFloor(
    uint Generation,
    ulong Version,
    WorldPublicOperation Operation);

struct PublicationChurnActor
{
    internal Entity Entity;
    internal WorldPosition Position;
    internal WorldPublicDescriptor Descriptor;
    internal WorldPublicEntityId LastObservedId;
    internal Entity LastObservedEntity;
    internal bool Alive;
    internal bool Public;
    internal bool ObservedPublic;
}

struct PublicOracleState
{
    internal PublicOracleState(WorldPublicRecord record, bool present) =>
        (Record, Present) = (record, present);
    internal WorldPublicRecord Record;
    internal bool Present;
}

sealed class RawSpatialFixture : IDisposable
{
    internal RawSpatialFixture(
        int lifecycleCapacity,
        int entityCapacity = 32,
        BoundWorldPack? baseWorld = null,
        ChunkPublicationLimits? publicationLimits = null)
    {
        var options = new EcsWorldOptions(
            new EntityCapacity(entityCapacity),
            maximumArchetypes: 4,
            maximumPreparedPlans: 8,
            maximumPreparedTransitions: 4,
            lifecycleRecordCapacity: lifecycleCapacity);
        World = new EcsWorld(GameSchema.CreateSchema(), options);
        State = new GameWorldState(World, baseWorld, publicationLimits);
        RawPositionLookup = World.PrepareLookup<WorldPositionComponent>();
        RawPublicLookup = World.PrepareLookup<WorldPublicComponent>();
        ComponentId position = GameSchema.GetComponentId<WorldPositionComponent>();
        ComponentId publication = GameSchema.GetComponentId<WorldPublicComponent>();
        NonSpatialArchetype = World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        Span<ComponentId> spatialSignature = stackalloc ComponentId[1] { position };
        SpatialArchetype = World.RegisterArchetype(
            spatialSignature,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        Span<ComponentId> publicSpatialSignature = stackalloc ComponentId[2] { position, publication };
        PublicSpatialArchetype = World.RegisterArchetype(
            publicSpatialSignature,
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        RemovePosition = World.PrepareTransition(SpatialArchetype, NonSpatialArchetype);
        AddPosition = World.PrepareTransition(NonSpatialArchetype, SpatialArchetype);
        RemovePublic = World.PrepareTransition(PublicSpatialArchetype, SpatialArchetype);
        AddPublic = World.PrepareTransition(SpatialArchetype, PublicSpatialArchetype);
        World.Seal();
        World.Initialize();
        World.BeginRun();
        State.Initialize(World);
    }

    internal EcsWorld World { get; }
    internal GameWorldState State { get; }
    internal ComponentLookup<WorldPositionComponent> RawPositionLookup { get; }
    internal ComponentLookup<WorldPublicComponent> RawPublicLookup { get; }
    internal ArchetypeHandle SpatialArchetype { get; }
    internal ArchetypeHandle PublicSpatialArchetype { get; }
    internal ArchetypeHandle NonSpatialArchetype { get; }
    internal ArchetypeTransition RemovePosition { get; }
    internal ArchetypeTransition AddPosition { get; }
    internal ArchetypeTransition RemovePublic { get; }
    internal ArchetypeTransition AddPublic { get; }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}
