using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.Physics;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.World.Testing;

if (args.Length == 1 && args[0] == "--w2-full-pack")
{
    FullDeclaredWorldPackAcceptance();
    return;
}

var tests = new (string Name, Action Body)[]
{
    ("FrozenProfileAndPayloadWidths", FrozenProfileAndPayloadWidths),
    ("WorldPackAddressValuesAndBoundsAreCanonical", WorldPackAddressValuesAndBoundsAreCanonical),
    ("CenteredWorldPackMappingOwnsSignedSeams", CenteredWorldPackMappingOwnsSignedSeams),
    ("WorldPackAddressBoundariesAndExtremesRoundTrip", WorldPackAddressBoundariesAndExtremesRoundTrip),
    ("WorldPackInverseRejectsOutOfSpatialRange", WorldPackInverseRejectsOutOfSpatialRange),
    ("WorldPackAddressingWarmedAllocationZero", WorldPackAddressingWarmedAllocationZero),
    ("WorldPackDeclaredCapacityAndByteCeilingAreCanonical", WorldPackDeclaredCapacityAndByteCeilingAreCanonical),
    ("DeclaredPackBuilderRejectsOutsideWindowEarly", DeclaredPackBuilderRejectsOutsideWindowEarly),
    ("OpenedPackValidationRejectsWrongAndCrossWindowDeclarations", OpenedPackValidationRejectsWrongAndCrossWindowDeclarations),
    ("DeclaredAndLegacyPackBuildsHaveIdenticalV1Bytes", DeclaredAndLegacyPackBuildsHaveIdenticalV1Bytes),
    ("ConceptDemoRemainsLegacyCrossWindowArtifact", ConceptDemoRemainsLegacyCrossWindowArtifact),
    ("ConceptDemoCanonicalTwoPackMatchesLegacyTruth", ConceptDemoCanonicalTwoPackMatchesLegacyTruth),
    ("StructuralManifestEmptyAndRevisionIdentityAreSeparate", StructuralManifestEmptyAndRevisionIdentityAreSeparate),
    ("StructuralManifestOnePackAndSparseLookupAreCanonical", StructuralManifestOnePackAndSparseLookupAreCanonical),
    ("StructuralManifestInputOrderAndMutationsDetermineIdentity", StructuralManifestInputOrderAndMutationsDetermineIdentity),
    ("StructuralManifestOpenRejectsMalformedBytes", StructuralManifestOpenRejectsMalformedBytes),
    ("StructuralManifestSparseAndCapacityBoundsAreExact", StructuralManifestSparseAndCapacityBoundsAreExact),
    ("PreparedCompositionValidatesCompleteCandidateAndSparseSubset", PreparedCompositionValidatesCompleteCandidateAndSparseSubset),
    ("W6StructuralStateHierarchyAndCompatibility", W6StructuralStateHierarchyAndCompatibility),
    ("W6ConceptDemoStructuralStatesAreCanonical", W6ConceptDemoStructuralStatesAreCanonical),
    ("PreparedCompositionMatchesOnePackQueriesAndOverlay", PreparedCompositionMatchesOnePackQueriesAndOverlay),
    ("PreparedCompositionRoutesDistantAndCrossPackStatics", PreparedCompositionRoutesDistantAndCrossPackStatics),
    ("PreparedCompositionTerrainMeshHaloReadsNeighborPack", PreparedCompositionTerrainMeshHaloReadsNeighborPack),
    ("GameWorldStateCanonicalCompositionIsExclusiveAndNotWorldviewV1", GameWorldStateCanonicalCompositionIsExclusiveAndNotWorldviewV1),
    ("PreparedCompositionWarmedQueriesAllocateZero", PreparedCompositionWarmedQueriesAllocateZero),
    ("W5CompleteTwoPackValidationAndIdentityCollisions", W5CompleteTwoPackValidationAndIdentityCollisions),
    ("W5WorldviewV2RowsAndResolverFailures", W5WorldviewV2RowsAndResolverFailures),
    ("FloorDivisionOwnsNegativeBoundaries", FloorDivisionOwnsNegativeBoundaries),
    ("AllCrossingDirectionsAndCornersNormalize", AllCrossingDirectionsAndCornersNormalize),
    ("GlobalLocalRoundTripsAcrossFullProfile", GlobalLocalRoundTripsAcrossFullProfile),
    ("NormalizationRejectsUnaddressableChunks", NormalizationRejectsUnaddressableChunks),
    ("MalformedValuesRejectWithoutSilentNormalization", MalformedValuesRejectWithoutSilentNormalization),
    ("CheckedTryGuardsRejectSignedOutOfRangeInputs", CheckedTryGuardsRejectSignedOutOfRangeInputs),
    ("SpatialMapCapacityRejectsUnrepresentableConfiguration", SpatialMapCapacityRejectsUnrepresentableConfiguration),
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
    ("BaseSurfacePresencePackingIsCanonicalAndLegacyCompatible",
        BaseSurfacePresencePackingIsCanonicalAndLegacyCompatible),
    ("BaseSurfaceProbeDistinguishesVoidFromUnavailable",
        BaseSurfaceProbeDistinguishesVoidFromUnavailable),
    ("BaseSurfaceVoidMeshOmitsGeometryDeterministically",
        BaseSurfaceVoidMeshOmitsGeometryDeterministically),
    ("TerrainReplacementPreservesAuthoredBaseTopology",
        TerrainReplacementPreservesAuthoredBaseTopology),
    ("AdditionalTerrainShellsCompileCanonicallyAndSpanChunks",
        AdditionalTerrainShellsCompileCanonicallyAndSpanChunks),
    ("AdditionalTerrainColumnsUseGeometryNotLayerRank",
        AdditionalTerrainColumnsUseGeometryNotLayerRank),
    ("AdditionalTerrainQueriesAndMeshesShareBaseParity",
        AdditionalTerrainQueriesAndMeshesShareBaseParity),
    ("AdditionalTerrainMeshIsBoundedClosedAndDeterministic",
        AdditionalTerrainMeshIsBoundedClosedAndDeterministic),
    ("AdditionalTerrainValidationAndCompatibilityAreFailClosed",
        AdditionalTerrainValidationAndCompatibilityAreFailClosed),
    ("WorldPackCompilationIsCanonicalAndOrderIndependent", WorldPackCompilationIsCanonicalAndOrderIndependent),
    ("WorldPackRandomTerrainReadsAreExactAndAllocationFree", WorldPackRandomTerrainReadsAreExactAndAllocationFree),
    ("WorldPackCompilerRejectsInvalidPlanesAndBrokenSeams", WorldPackCompilerRejectsInvalidPlanesAndBrokenSeams),
    ("WorldPackOpenRejectsCorruptionAndMalformedStructure", WorldPackOpenRejectsCorruptionAndMalformedStructure),
    ("WorldPackFileReopensWithStableFingerprint", WorldPackFileReopensWithStableFingerprint),
    ("WorldPackStreamOpenIsExactBoundedAndSingleCopy", WorldPackStreamOpenIsExactBoundedAndSingleCopy),
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
    ("SpatialIndexTurnoverDoesNotRetainHistoricalProbeChains", SpatialIndexTurnoverDoesNotRetainHistoricalProbeChains),
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
    ("ChunkInterestClipsAtWorldAddressExtremes",
        ChunkInterestClipsAtWorldAddressExtremes),
    ("LocalChunkStreamingOracleConvergesSnapshotsDeltasAndFallbacks",
        LocalChunkStreamingOracleConvergesSnapshotsDeltasAndFallbacks),
    ("LocalChunkSemanticSlotsFenceReuse",
        LocalChunkSemanticSlotsFenceReuse),
    ("LocalChunkSemanticApplicationRejectsContradictoryVersions",
        LocalChunkSemanticApplicationRejectsContradictoryVersions),
    ("LocalChunkStreamingOracleMatchesReferenceModelUnderChurn",
        LocalChunkStreamingOracleMatchesReferenceModelUnderChurn),
    ("ChunkInterestAndLocalNoChangeStreamingAllocateZero",
        ChunkInterestAndLocalNoChangeStreamingAllocateZero),
    ("MutableWorldOverlayPublishesCompleteTerrainAndStaticReplacements",
        MutableWorldOverlayPublishesCompleteTerrainAndStaticReplacements),
    ("EmptyAndIdenticalOverlayTransactionsRemainCanonicalAndRestorable",
        EmptyAndIdenticalOverlayTransactionsRemainCanonicalAndRestorable),
    ("MultiOverlayRootsFragmentsAndPersistenceRoundTrip",
        MultiOverlayRootsFragmentsAndPersistenceRoundTrip),
    ("WorldViewArtifactIsCanonicalBoundedAndFailClosed",
        WorldViewArtifactIsCanonicalBoundedAndFailClosed),
    ("PreparedWorldSaveRestoresExactPackAndOverlayAtomically",
        PreparedWorldSaveRestoresExactPackAndOverlayAtomically),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Terrain T0.0-T1.1 world tests passed.");

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
    Equal(8, Marshal.SizeOf<WorldPackCoord>());
    Equal(2, Marshal.SizeOf<WorldPackLocalChunkCoord>());
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

static void WorldPackAddressValuesAndBoundsAreCanonical()
{
    Equal(256, WorldPackAddressing.ChunksPerWorldPackEdge);
    Equal(128, WorldPackAddressing.CenteringBiasChunks);
    Equal(4_096_000, WorldPackAddressing.WorldPackEdgeUnits);
    Equal(4_096, WorldPackAddressing.WorldPackEdgeMeters);

    var pack = new WorldPackCoord(-2, 3);
    Equal(pack, new WorldPackCoord(-2, 3));
    Equal(pack.GetHashCode(), new WorldPackCoord(-2, 3).GetHashCode());
    True(pack.CompareTo(new WorldPackCoord(-2, 4)) < 0);
    True(pack.CompareTo(new WorldPackCoord(-1, -100)) < 0);
    Equal("Pack(-2,3)", pack.ToString());
    False(pack == new WorldPackCoord(-2, 4));

    var local = new WorldPackLocalChunkCoord(0, 255);
    Equal(local, new WorldPackLocalChunkCoord(0, 255));
    Equal(local.GetHashCode(), new WorldPackLocalChunkCoord(0, 255).GetHashCode());
    True(local.CompareTo(new WorldPackLocalChunkCoord(1, 0)) < 0);
    Equal("Local(0,255)", local.ToString());
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLocalChunkCoord(-1, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLocalChunkCoord(256, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLocalChunkCoord(0, -1));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLocalChunkCoord(0, 256));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldPackLocalChunkCoord(int.MaxValue, 0));
}

static void CenteredWorldPackMappingOwnsSignedSeams()
{
    (int Chunk, int Pack, int Local)[] cases =
    {
        (-384, -1, 0), (-129, -1, 255),
        (-128, 0, 0), (-1, 0, 127), (0, 0, 128), (127, 0, 255),
        (128, 1, 0), (383, 1, 255), (384, 2, 0),
    };
    foreach ((int chunk, int expectedPack, int expectedLocal) in cases)
    {
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(chunk, 0),
            out WorldPackCoord packX, out WorldPackLocalChunkCoord localX);
        Equal(new WorldPackCoord(expectedPack, 0), packX);
        Equal(new WorldPackLocalChunkCoord(expectedLocal, 128), localX);
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(0, chunk),
            out WorldPackCoord packZ, out WorldPackLocalChunkCoord localZ);
        Equal(new WorldPackCoord(0, expectedPack), packZ);
        Equal(new WorldPackLocalChunkCoord(128, expectedLocal), localZ);
        True(WorldPackAddressing.Contains(packX, new SpatialChunkCoord(chunk, 0)));
        False(WorldPackAddressing.Contains(new WorldPackCoord(expectedPack + 1, 0),
            new SpatialChunkCoord(chunk, 0)));
    }

    var mixed = new SpatialChunkCoord(-129, 384);
    WorldPackAddressing.ToPackLocal(mixed, out WorldPackCoord mixedPack, out WorldPackLocalChunkCoord mixedLocal);
    Equal(new WorldPackCoord(-1, 2), mixedPack);
    Equal(new WorldPackLocalChunkCoord(255, 0), mixedLocal);
    True(WorldPackAddressing.TryToGlobal(mixedPack, mixedLocal, out SpatialChunkCoord reconstructed));
    Equal(mixed, reconstructed);

    // Space identity encloses the same numerical Pack address; mapping never changes it.
    var first = new SpatialChunkKey(new SpatialSpaceId(1), mixed);
    var second = new SpatialChunkKey(new SpatialSpaceId(2), mixed);
    False(first == second);
    WorldPackAddressing.ToPackLocal(first.Coord, out WorldPackCoord firstPack, out _);
    WorldPackAddressing.ToPackLocal(second.Coord, out WorldPackCoord secondPack, out _);
    Equal(firstPack, secondPack);
}

static void WorldPackAddressBoundariesAndExtremesRoundTrip()
{
    var seen = new Dictionary<(WorldPackCoord Pack, WorldPackLocalChunkCoord Local), SpatialChunkCoord>();
    int[] extremes = { int.MinValue, int.MinValue + 1, int.MaxValue - 1, int.MaxValue };
    foreach (int x in extremes)
    foreach (int z in extremes)
        AssertWorldPackRoundTrip(new SpatialChunkCoord(x, z), seen);
    WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(int.MinValue, int.MinValue + 1),
        out WorldPackCoord minimumPack, out WorldPackLocalChunkCoord minimumLocal);
    Equal(new WorldPackCoord(-8_388_608, -8_388_608), minimumPack);
    Equal(new WorldPackLocalChunkCoord(128, 129), minimumLocal);
    WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(int.MaxValue - 1, int.MaxValue),
        out WorldPackCoord maximumPack, out WorldPackLocalChunkCoord maximumLocal);
    Equal(new WorldPackCoord(8_388_608, 8_388_608), maximumPack);
    Equal(new WorldPackLocalChunkCoord(126, 127), maximumLocal);

    // The first and last representable Packs have partial windows; interior Pack seams are complete.
    for (int pack = -2_048; pack <= 2_048; pack++)
    {
        long seam = (long)pack * WorldPackAddressing.ChunksPerWorldPackEdge -
            WorldPackAddressing.CenteringBiasChunks;
        foreach (int offset in new[] { -1, 0, 1 })
        {
            int chunk = checked((int)(seam + offset));
            AssertWorldPackRoundTrip(new SpatialChunkCoord(chunk, 0), seen);
            AssertWorldPackRoundTrip(new SpatialChunkCoord(0, chunk), seen);
        }

        int before = checked((int)(seam - 1));
        int at = checked((int)seam);
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(before, 0),
            out WorldPackCoord previousPack, out WorldPackLocalChunkCoord previousLocal);
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(at, 0),
            out WorldPackCoord nextPack, out WorldPackLocalChunkCoord nextLocal);
        Equal(pack - 1, previousPack.X);
        Equal(255, (int)previousLocal.X);
        Equal(pack, nextPack.X);
        Equal(0, (int)nextLocal.X);
    }
}

static void AssertWorldPackRoundTrip(
    SpatialChunkCoord original,
    Dictionary<(WorldPackCoord Pack, WorldPackLocalChunkCoord Local), SpatialChunkCoord> seen)
{
    WorldPackAddressing.ToPackLocal(original, out WorldPackCoord pack, out WorldPackLocalChunkCoord local);
    True(WorldPackAddressing.TryToGlobal(pack, local, out SpatialChunkCoord restored));
    Equal(original, restored);
    if (seen.TryGetValue((pack, local), out SpatialChunkCoord prior))
        Equal(prior, original);
    else
        seen.Add((pack, local), original);
}

static void WorldPackInverseRejectsOutOfSpatialRange()
{
    foreach (WorldPackCoord pack in new[]
    {
        new WorldPackCoord(int.MinValue, 0), new WorldPackCoord(int.MaxValue, 0),
        new WorldPackCoord(0, int.MinValue), new WorldPackCoord(0, int.MaxValue),
    })
    {
        False(WorldPackAddressing.TryToGlobal(pack, default, out SpatialChunkCoord rejected));
        Equal(default, rejected);
    }

    WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(int.MinValue, int.MaxValue),
        out WorldPackCoord edgePack, out WorldPackLocalChunkCoord edgeLocal);
    True(WorldPackAddressing.TryToGlobal(edgePack, edgeLocal, out SpatialChunkCoord restored));
    Equal(new SpatialChunkCoord(int.MinValue, int.MaxValue), restored);
    False(WorldPackAddressing.TryToGlobal(edgePack, new WorldPackLocalChunkCoord(0, 255), out _));
    False(WorldPackAddressing.TryToGlobal(new WorldPackCoord(8_388_608, 0),
        new WorldPackLocalChunkCoord(128, 128), out _));
}

static void WorldPackAddressingWarmedAllocationZero()
{
    long checksum = 0;
    for (int iteration = 0; iteration < 1_000; iteration++)
    {
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(iteration - 500, 500 - iteration),
            out WorldPackCoord pack, out WorldPackLocalChunkCoord local);
        if (!WorldPackAddressing.TryToGlobal(pack, local, out SpatialChunkCoord global))
            throw new Exception("A forward Pack address must round-trip.");
        checksum += global.X;
    }

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int iteration = 0; iteration < 100_000; iteration++)
    {
        WorldPackAddressing.ToPackLocal(new SpatialChunkCoord(iteration - 50_000, 50_000 - iteration),
            out WorldPackCoord pack, out WorldPackLocalChunkCoord local);
        if (!WorldPackAddressing.TryToGlobal(pack, local, out SpatialChunkCoord global) ||
            !WorldPackAddressing.Contains(pack, global))
            throw new Exception("A forward Pack address must round-trip and be contained.");
        checksum += global.Z;
    }
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    True(checksum != 0);
}

static void WorldPackDeclaredCapacityAndByteCeilingAreCanonical()
{
    Equal(65_536, WorldPackAddressing.MaximumChunksPerWorldPack);
    Equal(65_536, WorldPackLimits.Default.MaximumChunks);
    Equal(512 * 1024 * 1024, WorldPackLimits.Default.MaximumPackBytes);
    foreach (int count in new[] { 1, 100, 10_000, 65_536 })
        Equal(count, new WorldPackLimits(WorldPackFormat.HeaderBytes, count, 2).MaximumChunks);
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new WorldPackLimits(512 * 1024 * 1024, 65_537, 2));

    // Stream admission must reject a declared length above the new default before body allocation/read.
    var oversizedHeader = new byte[WorldPackFormat.HeaderBytes];
    BinaryPrimitives.WriteUInt64LittleEndian(oversizedHeader, WorldPackFormat.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(oversizedHeader.AsSpan(8), WorldPackFormat.ContainerVersion);
    BinaryPrimitives.WriteUInt64LittleEndian(oversizedHeader.AsSpan(16),
        (ulong)WorldPackLimits.Default.MaximumPackBytes + 1);
    using var oversized = new ChunkedReadStream(oversizedHeader, maximumRead: 17);
    False(WorldPack.TryOpen(oversized, WorldPackLimits.Default, WorldPackTestProfile.Content,
        out WorldPack? rejected, out WorldPackOpenError openError));
    Equal(null, rejected);
    Equal(WorldPackOpenError.CapacityExceeded, openError);
}

static void DeclaredPackBuilderRejectsOutsideWindowEarly()
{
    var limits = new WorldPackLimits(1024 * 1024, 8, 3);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    var origin = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        limits, new WorldPackCoord(0, 0));
    foreach ((int x, int z) in new[] { (-128, -128), (0, 0), (127, 127) })
        True(origin.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, z),
            materials, heightBase, heights, out _));
    Equal(3, origin.Count);
    foreach ((int x, int z) in new[] { (128, 0), (-129, 0), (0, 128), (0, -129) })
    {
        False(origin.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, z),
            materials, heightBase, heights, out WorldPackBuildError error));
        Equal(WorldPackBuildError.ChunkOutsidePackWindow, error);
        Equal(3, origin.Count);
    }
    False(origin.TryAddBaseTerrain(new SpatialChunkKey(new SpatialSpaceId(99), 128, 0),
        materials, heightBase, heights, out WorldPackBuildError wrongSpace));
    Equal(WorldPackBuildError.SpaceMismatch, wrongSpace);
    // Storage belongs to chunk (127,127); its valid static geometry may cross the Pack edge.
    True(origin.TryAddFrozenStatic(CreateFrozenStatic(1, 1,
        new WorldPosition(127, 127, 15_900, 15_900, 0), halfExtent: 300), out _));
    True(origin.TryBuild(out WorldPackArtifact? sparseArtifact, out _));
    WorldPack sparse = sparseArtifact!.Open();
    Equal(3, sparse.ChunkCount);
    True(sparse.Length < 20_000);
    True(sparse.TryValidateSpatialWindow(new WorldPackCoord(0, 0), out _, out _));

    var east = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        limits, new WorldPackCoord(1, 0));
    foreach (int x in new[] { 128, 383 })
        True(east.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, 0),
            materials, heightBase, heights, out _));
    False(east.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 384, 0),
        materials, heightBase, heights, out WorldPackBuildError eastError));
    Equal(WorldPackBuildError.ChunkOutsidePackWindow, eastError);

    var west = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        limits, new WorldPackCoord(-1, 0));
    foreach (int x in new[] { -384, -129 })
        True(west.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, 0),
            materials, heightBase, heights, out _));
    False(west.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, -128, 0),
        materials, heightBase, heights, out WorldPackBuildError westError));
    Equal(WorldPackBuildError.ChunkOutsidePackWindow, westError);

    var maximum = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        limits, new WorldPackCoord(8_388_608, -8_388_608));
    True(maximum.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space,
        int.MaxValue, int.MinValue), materials, heightBase, heights, out _));
    True(maximum.TryBuild(out WorldPackArtifact? edgeArtifact, out _));
    True(edgeArtifact!.Open().TryValidateSpatialWindow(new WorldPackCoord(8_388_608, -8_388_608),
        out _, out _));
    False(edgeArtifact.Open().TryValidateSpatialWindow(new WorldPackCoord(8_388_608, 0),
        out _, out WorldPackSpatialValidationError edgeError));
    Equal(WorldPackSpatialValidationError.ChunkOutsidePackWindow, edgeError);
}

static void OpenedPackValidationRejectsWrongAndCrossWindowDeclarations()
{
    WorldPack valid = GamePlayableWorld.CreateWorldPack();
    True(valid.TryValidateSpatialWindow(new WorldPackCoord(0, 0),
        out SpatialChunkKey noOffender, out WorldPackSpatialValidationError accepted));
    Equal(default, noOffender);
    Equal(WorldPackSpatialValidationError.None, accepted);
    long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int iteration = 0; iteration < 10_000; iteration++)
        if (!valid.TryValidateSpatialWindow(new WorldPackCoord(0, 0), out _, out _))
            throw new Exception("A valid declared Pack must remain valid.");
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocationBefore);
    WorldPackFingerprint before = valid.Fingerprint;
    False(valid.TryValidateSpatialWindow(new WorldPackCoord(1, 0),
        out SpatialChunkKey wrongKey, out WorldPackSpatialValidationError rejected));
    Equal(WorldPackSpatialValidationError.ChunkOutsidePackWindow, rejected);
    True(wrongKey.Space == valid.Space);
    True(WorldPackAddressing.Contains(new WorldPackCoord(0, 0), wrongKey.Coord));
    Equal(before, valid.Fingerprint);

    var limits = new WorldPackLimits(1024 * 1024, 2, 2);
    var legacy = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content, limits);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    foreach (int x in new[] { 127, 128 })
        True(legacy.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, 0),
            materials, heightBase, heights, out _));
    True(legacy.TryBuild(out WorldPackArtifact? crossArtifact, out _));
    WorldPack cross = crossArtifact!.Open();
    foreach (WorldPackCoord declaration in new[] { new WorldPackCoord(0, 0), new WorldPackCoord(1, 0) })
    {
        False(cross.TryValidateSpatialWindow(declaration, out SpatialChunkKey offending,
            out WorldPackSpatialValidationError error));
        Equal(WorldPackSpatialValidationError.ChunkOutsidePackWindow, error);
        False(WorldPackAddressing.Contains(declaration, offending.Coord));
    }
}

static void DeclaredAndLegacyPackBuildsHaveIdenticalV1Bytes()
{
    WorldPackArtifact legacy = BuildCabinAddressParityArtifact(declared: false);
    WorldPackArtifact declared = BuildCabinAddressParityArtifact(declared: true);
    Equal(legacy.Length, declared.Length);
    Equal(legacy.ContentFingerprint, declared.ContentFingerprint);
    Equal(legacy.Fingerprint, declared.Fingerprint);
    var first = new byte[legacy.Length];
    var second = new byte[declared.Length];
    legacy.CopyTo(first);
    declared.CopyTo(second);
    True(first.AsSpan().SequenceEqual(second));
    Equal(WorldPackFormat.ContainerVersion, BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(8)));
    True(declared.Open().TryValidateSpatialWindow(new WorldPackCoord(0, 0), out _, out _));
}

static WorldPackArtifact BuildCabinAddressParityArtifact(bool declared)
{
    var limits = new WorldPackLimits(1024 * 1024, 25, 2);
    var builder = declared
        ? new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
            limits, new WorldPackCoord(0, 0))
        : new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content, limits);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    for (int z = -2; z <= 2; z++)
    for (int x = -2; x <= 2; x++)
        True(builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, x, z),
            materials, heightBase, heights, out _));
    True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
    return artifact!;
}

static void ConceptDemoRemainsLegacyCrossWindowArtifact()
{
    WorldPack demo = GamePlayableWorld.CreateConceptDemoWorldPack();
    Equal(GamePlayableWorld.ConceptChunkCount, demo.ChunkCount);
    False(demo.TryValidateSpatialWindow(new WorldPackCoord(0, 0), out _, out _));
    False(demo.TryValidateSpatialWindow(new WorldPackCoord(1, 0), out _, out _));
    True(demo.TryGetBaseTerrain(new SpatialChunkKey(demo.Space, 127, 0), out _));
    True(demo.TryGetBaseTerrain(new SpatialChunkKey(demo.Space, 128, 0), out _));
}

static void ConceptDemoCanonicalTwoPackMatchesLegacyTruth()
{
    WorldPack legacy = GamePlayableWorld.CreateConceptDemoWorldPack();
    Equal(731_384, legacy.Length);
    var source = GamePlayableWorld.CreateConceptDemoStructuralSource();
    PreparedWorldComposition canonical = source.Prepare(ConceptDemoStructuralSource.CreateBinding());
    Equal(117, source.West.ChunkCount);
    Equal(45, source.East.ChunkCount);
    Equal("A76FAE182AA566BE91D312E26FCE29EA6E2854E84402D50579A6C5C2009553CB",
        source.West.Fingerprint.ToString());
    Equal("CE9F0CCDB591F3C3E592B8105E43C1AEB8323A5CB686533FC4735A8AA15D7B1D",
        source.East.Fingerprint.ToString());
    Equal("41DB6F68C26AC85DF5ABEB60BEDA3F3F07DD1E76CF30A5164874446D902B6B53",
        source.Manifest.Fingerprint.ToString());
    Equal("323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795",
        legacy.Fingerprint.ToString());
    Equal(2, canonical.ManifestEntryCount);
    Equal(162, canonical.PreparedChunkCount);
    True(canonical.TryValidateComplete(out _));
    ConceptDemoStructuralSource again = GamePlayableWorld.CreateConceptDemoStructuralSource();
    Equal(source.West.Fingerprint, again.West.Fingerprint);
    Equal(source.East.Fingerprint, again.East.Fingerprint);
    Equal(source.Manifest.Fingerprint, again.Manifest.Fingerprint);
    Console.WriteLine($"W5R CONCEPT west={source.West.Fingerprint} bytes={source.West.Length} " +
        $"east={source.East.Fingerprint} bytes={source.East.Length} " +
        $"manifest={source.Manifest.Fingerprint} legacy={legacy.Fingerprint}");
    for (int index = 0; index < legacy.ChunkCount; index++)
    {
        True(legacy.TryGetChunkKey(index, out SpatialChunkKey key));
        True(legacy.TryGetBaseTerrain(key, out WorldTerrainChunkView expected));
        True(canonical.TryGetBaseTerrain(key, out WorldTerrainChunkView actual));
        for (int cell = 0; cell < expected.MaterialCount; cell++)
        {
            Equal(expected.GetMaterial(cell), actual.GetMaterial(cell));
            Equal(expected.IsSurfacePresent(cell), actual.IsSurfacePresent(cell));
        }
        for (int vertex = 0; vertex < expected.HeightVertexCount; vertex++)
            Equal(expected.GetAbsoluteHeightMillimeters(vertex),
                actual.GetAbsoluteHeightMillimeters(vertex));
        bool hasExpectedStatics = legacy.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView expectedStatics);
        bool hasActualStatics = canonical.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView actualStatics);
        Equal(hasExpectedStatics ? expectedStatics.Count : 0,
            hasActualStatics ? actualStatics.Count : 0);
        if (hasExpectedStatics && expectedStatics.Count != 0)
        {
            Equal(expectedStatics.Count, actualStatics.Count);
            for (int row = 0; row < expectedStatics.Count; row++)
                Equal(expectedStatics.GetRecord(row), actualStatics.GetRecord(row));
        }
        bool hasExpectedFragments = legacy.TryGetMultiFragments(key, out WorldMultiFragmentChunkView expectedFragments);
        bool hasActualFragments = canonical.TryGetMultiFragments(key, out WorldMultiFragmentChunkView actualFragments);
        Equal(hasExpectedFragments ? expectedFragments.Count : 0,
            hasActualFragments ? actualFragments.Count : 0);
        if (hasExpectedFragments && expectedFragments.Count != 0)
        {
            Equal(expectedFragments.Count, actualFragments.Count);
            for (int row = 0; row < expectedFragments.Count; row++)
                Equal(expectedFragments.GetRecord(row), actualFragments.GetRecord(row));
        }
        bool hasExpectedBands = legacy.TryGetAdditionalTerrainSurfaceBands(
            key, out WorldTerrainSurfaceBandChunkView expectedBands);
        bool hasActualBands = canonical.TryGetAdditionalTerrainSurfaceBands(
            key, out WorldTerrainSurfaceBandChunkView actualBands);
        Equal(hasExpectedBands ? expectedBands.Count : 0,
            hasActualBands ? actualBands.Count : 0);
        if (hasExpectedBands && expectedBands.Count != 0)
            for (int row = 0; row < expectedBands.Count; row++)
            {
                WorldTerrainSurfaceBandView before = expectedBands.GetBand(row);
                WorldTerrainSurfaceBandView after = actualBands.GetBand(row);
                Equal(before.Id, after.Id);
                Equal(before.Bounds, after.Bounds);
                Equal(before.Faces, after.Faces);
                Equal(before.SideMaterialId, after.SideMaterialId);
                for (int z = 0; z < before.Bounds.CellDepth; z++)
                for (int x = 0; x < before.Bounds.CellWidth; x++)
                {
                    Equal(before.IsCellPresent(x, z), after.IsCellPresent(x, z));
                    Equal(before.GetEdgeConnections(x, z), after.GetEdgeConnections(x, z));
                    if (before.HasTop)
                        Equal(before.GetMaterial(WorldTerrainSurfaceFace.Top, x, z),
                            after.GetMaterial(WorldTerrainSurfaceFace.Top, x, z));
                    if (before.HasBottom)
                        Equal(before.GetMaterial(WorldTerrainSurfaceFace.Bottom, x, z),
                            after.GetMaterial(WorldTerrainSurfaceFace.Bottom, x, z));
                }
                for (int z = 0; z <= before.Bounds.CellDepth; z++)
                for (int x = 0; x <= before.Bounds.CellWidth; x++)
                {
                    if (before.HasTop)
                        Equal(before.GetHeight(WorldTerrainSurfaceFace.Top, x, z),
                            after.GetHeight(WorldTerrainSurfaceFace.Top, x, z));
                    if (before.HasBottom)
                        Equal(before.GetHeight(WorldTerrainSurfaceFace.Bottom, x, z),
                            after.GetHeight(WorldTerrainSurfaceFace.Bottom, x, z));
                }
            }
    }
    int canonicalMultiCount = 0;
    for (int index = 0; index < canonical.PreparedPackCount; index++)
    {
        True(canonical.TryGetPreparedPackAt(index, out PreparedWorldPack prepared));
        for (int row = 0; row < prepared.View.BaseWorld.MultiInstanceCount; row++)
        {
            True(prepared.View.BaseWorld.TryGetMultiInstanceAt(row, out WorldMultiInstanceRecord actual));
            True(legacy.TryGetMultiInstance(actual.Id, out WorldMultiInstanceRecord expected));
            Equal(expected, actual);
            canonicalMultiCount++;
        }
    }
    Equal(legacy.MultiInstanceCount, canonicalMultiCount);
}

static WorldPackFingerprint StructuralTestFingerprint(byte seed)
{
    byte[] digest = SHA256.HashData(new byte[] { seed });
    True(WorldPackFingerprint.TryParse(Convert.ToHexString(digest), out WorldPackFingerprint fingerprint));
    return fingerprint;
}

static WorldStructuralManifest StructuralManifest(params WorldStructuralManifestEntry[] entries)
{
    True(WorldStructuralManifest.TryCreate(entries, out WorldStructuralManifest? manifest,
        out WorldStructuralManifestError error));
    Equal(WorldStructuralManifestError.None, error);
    return manifest!;
}

static byte[] StructuralManifestBytes(WorldStructuralManifest manifest)
{
    var bytes = new byte[manifest.Length];
    manifest.CopyTo(bytes);
    return bytes;
}

static void ResignStructuralManifestBytes(byte[] bytes)
{
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(bytes.AsSpan(0, WorldStructuralManifestFormat.FingerprintOffset));
    hash.AppendData(bytes.AsSpan(WorldStructuralManifestFormat.HeaderBytes));
    byte[] digest = hash.GetHashAndReset();
    digest.CopyTo(bytes.AsSpan(WorldStructuralManifestFormat.FingerprintOffset));
}

static void StructuralManifestEmptyAndRevisionIdentityAreSeparate()
{
    Equal(32, Marshal.SizeOf<WorldStructuralManifestFingerprint>());
    Equal(8, Marshal.SizeOf<WorldCompositionRevision>());
    Equal(64, WorldStructuralManifestFormat.HeaderBytes);
    Equal(40, WorldStructuralManifestFormat.EntryBytes);
    Equal(65_536, WorldStructuralManifestLimits.Default.MaximumPacks);
    Equal(2_621_504, WorldStructuralManifestLimits.Default.MaximumManifestBytes);
    Equal(1u, WorldStructuralManifestFormat.Version);
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldStructuralManifestLimits(65_537, 2_621_504));
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldStructuralManifestLimits(1, 63));

    WorldStructuralManifest empty = StructuralManifest();
    Equal(0, empty.EntryCount);
    Equal(64, empty.Length);
    True(empty.Fingerprint.IsValid);
    False(empty.TryGetEntry(0, out _));
    False(empty.TryGetPack(new WorldPackCoord(0, 0), out _));
    byte[] bytes = StructuralManifestBytes(empty);
    True(WorldStructuralManifest.TryOpen(bytes, out WorldStructuralManifest? reopened,
        out WorldStructuralManifestError openError));
    Equal(WorldStructuralManifestError.None, openError);
    Equal(empty.Fingerprint, reopened!.Fingerprint);
    True(bytes.AsSpan().SequenceEqual(StructuralManifestBytes(StructuralManifest())));
    Equal(empty.Fingerprint, StructuralManifest().Fingerprint);

    var first = new WorldCompositionRevision(1);
    var second = new WorldCompositionRevision(2);
    True(first.IsValid);
    True(second.CompareTo(first) > 0);
    False(default(WorldCompositionRevision).IsValid);
    // Revision, WorldId, SpatialSpaceId and residency have no input slot in canonical bytes.
    Equal(empty.Fingerprint, reopened.Fingerprint);
    Console.WriteLine($"W3 EMPTY MANIFEST fingerprint={empty.Fingerprint} bytes={empty.Length}");
}

static void StructuralManifestOnePackAndSparseLookupAreCanonical()
{
    WorldPackArtifact validShard = BuildCabinAddressParityArtifact(declared: true);
    var oneEntry = new WorldStructuralManifestEntry(new WorldPackCoord(0, 0), validShard.Fingerprint);
    var mutableSource = new[] { oneEntry };
    WorldStructuralManifest one = StructuralManifest(mutableSource);
    mutableSource[0] = new WorldStructuralManifestEntry(new WorldPackCoord(1, 0), StructuralTestFingerprint(9));
    Equal(1, one.EntryCount);
    Equal(104, one.Length);
    True(one.TryGetEntry(0, out WorldStructuralManifestEntry copied));
    Equal(oneEntry, copied);
    True(one.TryGetPack(oneEntry.Coord, out WorldPackFingerprint found));
    Equal(validShard.Fingerprint, found);
    False(one.TryGetPack(new WorldPackCoord(1, 0), out _));
    byte[] bytes = StructuralManifestBytes(one);
    True(WorldStructuralManifest.TryOpen(bytes, out WorldStructuralManifest? opened, out _));
    Equal(one.Fingerprint, opened!.Fingerprint);
    Equal(oneEntry, GetStructuralEntry(opened, 0));
    // Opening and copying must not retain or expose the caller's mutable byte buffer.
    bytes[WorldStructuralManifestFormat.HeaderBytes] ^= 1;
    Equal(oneEntry, GetStructuralEntry(opened, 0));

    WorldPackFingerprint reused = StructuralTestFingerprint(8);
    WorldStructuralManifest sparse = StructuralManifest(
        new(new WorldPackCoord(2000, -500), reused),
        new(new WorldPackCoord(0, 0), StructuralTestFingerprint(3)),
        new(new WorldPackCoord(-4, 7), reused),
        new(new WorldPackCoord(1, 0), StructuralTestFingerprint(4)));
    Equal(4, sparse.EntryCount);
    Equal(224, sparse.Length);
    Equal(new WorldPackCoord(-4, 7), GetStructuralEntry(sparse, 0).Coord);
    Equal(new WorldPackCoord(0, 0), GetStructuralEntry(sparse, 1).Coord);
    Equal(new WorldPackCoord(1, 0), GetStructuralEntry(sparse, 2).Coord);
    Equal(new WorldPackCoord(2000, -500), GetStructuralEntry(sparse, 3).Coord);
    True(sparse.TryGetPack(new WorldPackCoord(2000, -500), out WorldPackFingerprint far));
    Equal(reused, far);
    False(sparse.TryGetPack(new WorldPackCoord(1999, -500), out _));
    Console.WriteLine($"W3 ONE PACK fingerprint={one.Fingerprint} bytes={one.Length}");
    Console.WriteLine($"W3 SPARSE FOUR fingerprint={sparse.Fingerprint} bytes={sparse.Length}");
}

static WorldStructuralManifestEntry GetStructuralEntry(WorldStructuralManifest manifest, int index)
{
    True(manifest.TryGetEntry(index, out WorldStructuralManifestEntry entry));
    return entry;
}

static void StructuralManifestInputOrderAndMutationsDetermineIdentity()
{
    WorldPackFingerprint a = StructuralTestFingerprint(1);
    WorldPackFingerprint b = StructuralTestFingerprint(2);
    WorldPackFingerprint c = StructuralTestFingerprint(3);
    WorldStructuralManifestEntry first = new(new WorldPackCoord(0, 0), a);
    WorldStructuralManifestEntry second = new(new WorldPackCoord(1, 0), b);
    WorldStructuralManifest original = StructuralManifest(second, first);
    WorldStructuralManifest reversed = StructuralManifest(first, second);
    Equal(original.Fingerprint, reversed.Fingerprint);
    True(StructuralManifestBytes(original).AsSpan().SequenceEqual(StructuralManifestBytes(reversed)));
    for (int index = 0; index < original.EntryCount; index++)
        Equal(GetStructuralEntry(original, index), GetStructuralEntry(reversed, index));
    False(WorldStructuralManifest.TryCreate(new[] { first, first }, out _, out WorldStructuralManifestError duplicateSame));
    Equal(WorldStructuralManifestError.DuplicatePackCoord, duplicateSame);
    False(WorldStructuralManifest.TryCreate(new[] { first, new WorldStructuralManifestEntry(first.Coord, b) },
        out _, out WorldStructuralManifestError duplicateDifferent));
    Equal(WorldStructuralManifestError.DuplicatePackCoord, duplicateDifferent);
    False(WorldStructuralManifest.TryCreate(new[] { new WorldStructuralManifestEntry(first.Coord, default) },
        out _, out WorldStructuralManifestError invalid));
    Equal(WorldStructuralManifestError.InvalidPackFingerprint, invalid);

    WorldStructuralManifest[] changed =
    [
        StructuralManifest(first, new(new WorldPackCoord(1, 0), c)),
        StructuralManifest(first, new(new WorldPackCoord(2, 0), b)),
        StructuralManifest(first),
        StructuralManifest(first, second, new(new WorldPackCoord(2, 0), c)),
    ];
    foreach (WorldStructuralManifest variant in changed)
        False(original.Fingerprint == variant.Fingerprint);
    var sufficient = new WorldStructuralManifestLimits(2, 144);
    True(WorldStructuralManifest.TryCreate(new[] { first, second }, out WorldStructuralManifest? limited,
        out _, sufficient));
    Equal(original.Fingerprint, limited!.Fingerprint);
    True(WorldStructuralManifest.TryOpen(StructuralManifestBytes(original), out WorldStructuralManifest? limitedOpen,
        out _, sufficient));
    Equal(original.Fingerprint, limitedOpen!.Fingerprint);
    False(WorldStructuralManifest.TryCreate(new[] { first, second }, out _,
        out WorldStructuralManifestError smallByteError, new WorldStructuralManifestLimits(2, 143)));
    Equal(WorldStructuralManifestError.CapacityExceeded, smallByteError);
    Equal(original.Fingerprint, StructuralManifest(first, second).Fingerprint);
}

static void StructuralManifestOpenRejectsMalformedBytes()
{
    WorldStructuralManifest original = StructuralManifest(
        new(new WorldPackCoord(0, 0), StructuralTestFingerprint(1)),
        new(new WorldPackCoord(1, 0), StructuralTestFingerprint(2)));
    byte[] pristine = StructuralManifestBytes(original);
    static void Reject(byte[] bytes, WorldStructuralManifestError expected)
    {
        False(WorldStructuralManifest.TryOpen(bytes, out WorldStructuralManifest? opened,
            out WorldStructuralManifestError actual));
        Equal(null, opened);
        Equal(expected, actual);
    }
    byte[] changed = (byte[])pristine.Clone();
    changed[0] ^= 1;
    Reject(changed, WorldStructuralManifestError.InvalidMagic);
    changed = (byte[])pristine.Clone();
    changed[8] ^= 1;
    Reject(changed, WorldStructuralManifestError.UnsupportedVersion);
    changed = (byte[])pristine.Clone();
    changed[12] ^= 1;
    Reject(changed, WorldStructuralManifestError.UnsupportedProfile);
    changed = (byte[])pristine.Clone();
    changed[16] ^= 1;
    Reject(changed, WorldStructuralManifestError.UnsupportedProfile);
    changed = (byte[])pristine.Clone();
    changed[20] ^= 1;
    Reject(changed, WorldStructuralManifestError.UnsupportedProfile);
    changed = (byte[])pristine.Clone();
    changed[24] ^= 1;
    Reject(changed, WorldStructuralManifestError.UnsupportedProfile);
    changed = (byte[])pristine.Clone();
    changed[28] = 3;
    Reject(changed, WorldStructuralManifestError.Truncated);
    Reject(pristine[..63], WorldStructuralManifestError.Truncated);
    Reject(pristine[..^1], WorldStructuralManifestError.Truncated);
    Reject([.. pristine, 0], WorldStructuralManifestError.LengthMismatch);
    changed = (byte[])pristine.Clone();
    changed[32] ^= 1;
    Reject(changed, WorldStructuralManifestError.HashMismatch);
    changed = (byte[])pristine.Clone();
    changed[WorldStructuralManifestFormat.HeaderBytes + 4] ^= 1;
    Reject(changed, WorldStructuralManifestError.HashMismatch);
    changed = (byte[])pristine.Clone();
    changed[WorldStructuralManifestFormat.HeaderBytes + 8] ^= 1;
    Reject(changed, WorldStructuralManifestError.HashMismatch);
    changed = (byte[])pristine.Clone();
    pristine.AsSpan(WorldStructuralManifestFormat.HeaderBytes, 8).CopyTo(
        changed.AsSpan(WorldStructuralManifestFormat.HeaderBytes + WorldStructuralManifestFormat.EntryBytes));
    ResignStructuralManifestBytes(changed);
    Reject(changed, WorldStructuralManifestError.NonCanonicalEntries);
    changed = (byte[])pristine.Clone();
    changed.AsSpan(WorldStructuralManifestFormat.HeaderBytes,
        WorldStructuralManifestFormat.EntryBytes).CopyTo(
        changed.AsSpan(WorldStructuralManifestFormat.HeaderBytes + WorldStructuralManifestFormat.EntryBytes));
    ResignStructuralManifestBytes(changed);
    Reject(changed, WorldStructuralManifestError.NonCanonicalEntries);
    changed = (byte[])pristine.Clone();
    changed.AsSpan(WorldStructuralManifestFormat.HeaderBytes + 8, 32).Clear();
    ResignStructuralManifestBytes(changed);
    Reject(changed, WorldStructuralManifestError.InvalidPackFingerprint);
    changed = (byte[])pristine.Clone();
    BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(28), 65_537);
    Reject(changed, WorldStructuralManifestError.CapacityExceeded);
    Reject(new byte[WorldStructuralManifestFormat.MaximumManifestBytes + 1],
        WorldStructuralManifestError.CapacityExceeded);
}

static void StructuralManifestSparseAndCapacityBoundsAreExact()
{
    WorldPackFingerprint common = StructuralTestFingerprint(25);
    var watch = Stopwatch.StartNew();
    WorldStructuralManifest tenThousand = StructuralManifest(
        Enumerable.Range(0, 10_000).Select(index =>
            new WorldStructuralManifestEntry(new WorldPackCoord(index * 197 - 985_000, index * 11), common)).ToArray());
    long buildMilliseconds = watch.ElapsedMilliseconds;
    Equal(10_000, tenThousand.EntryCount);
    Equal(400_064, tenThousand.Length);
    True(tenThousand.TryGetPack(new WorldPackCoord(-985_000, 0), out _));
    True(tenThousand.TryGetPack(new WorldPackCoord(984_803, 109_989), out _));
    False(tenThousand.TryGetPack(new WorldPackCoord(984_804, 109_989), out _));
    watch.Restart();
    True(WorldStructuralManifest.TryOpen(StructuralManifestBytes(tenThousand),
        out WorldStructuralManifest? reopened, out _));
    long openMilliseconds = watch.ElapsedMilliseconds;
    Equal(tenThousand.Fingerprint, reopened!.Fingerprint);
    long checksum = 0;
    long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 100_000; index++)
    {
        int coordinate = index % 10_000;
        if (!reopened.TryGetPack(new WorldPackCoord(coordinate * 197 - 985_000, coordinate * 11), out _))
            throw new Exception("A canonical sparse entry was lost.");
        checksum += coordinate;
    }
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocationBefore);
    True(checksum != 0);

    var maximumEntries = new WorldStructuralManifestEntry[WorldStructuralManifestFormat.MaximumPacks];
    for (int index = 0; index < maximumEntries.Length; index++)
        maximumEntries[index] = new WorldStructuralManifestEntry(new WorldPackCoord(index, -index), common);
    True(WorldStructuralManifest.TryCreate(maximumEntries, out WorldStructuralManifest? maximum,
        out WorldStructuralManifestError maximumError));
    Equal(WorldStructuralManifestError.None, maximumError);
    Equal(2_621_504, maximum!.Length);
    True(WorldStructuralManifest.TryOpen(StructuralManifestBytes(maximum),
        out WorldStructuralManifest? maximumReopened, out _));
    Equal(maximum.Fingerprint, maximumReopened!.Fingerprint);
    var oneOver = new WorldStructuralManifestEntry[maximumEntries.Length + 1];
    maximumEntries.CopyTo(oneOver, 0);
    oneOver[^1] = new WorldStructuralManifestEntry(new WorldPackCoord(maximumEntries.Length, 0), common);
    False(WorldStructuralManifest.TryCreate(oneOver, out _, out WorldStructuralManifestError oneOverError));
    Equal(WorldStructuralManifestError.CapacityExceeded, oneOverError);
    Console.WriteLine($"W3 SPARSE 10000 bytes={tenThousand.Length} entry_payload_bytes={tenThousand.EntryCount * 40} " +
        $"build_ms={buildMilliseconds} open_ms={openMilliseconds} lookup_allocated_bytes=0");
    Console.WriteLine($"W3 MAXIMUM entries={maximum.EntryCount} bytes={maximum.Length} one_over={oneOverError}");
}

static WorldPreparedView W4Shard(
    WorldPackCoord coord,
    int localX = 128,
    int localZ = 128,
    SpatialSpaceId? space = null,
    WorldContentFingerprint? content = null,
    ulong staticId = 0)
{
    SpatialSpaceId selectedSpace = space ?? WorldPackTestProfile.Space;
    WorldContentFingerprint selectedContent = content ?? WorldPackTestProfile.Content;
    True(WorldPackAddressing.TryToGlobal(coord, new WorldPackLocalChunkCoord(localX, localZ),
        out SpatialChunkCoord global));
    var builder = new WorldPackBuilder(selectedSpace, selectedContent,
        new WorldPackLimits(1024 * 1024, 1, 3), coord);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    True(builder.TryAddBaseTerrain(new SpatialChunkKey(selectedSpace, global.X, global.Z),
        materials, heightBase, heights, out _));
    if (staticId != 0)
    {
        var position = new WorldPosition(global.X, global.Z, (ushort)(localX == 255 ? 15_900 : 100),
            1_000, 0);
        True(builder.TryAddFrozenStatic(CreateFrozenStatic(staticId, 1, position, 300), out _));
    }
    True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
    True(artifact!.Open().TryBind(new WorldContentBinding(selectedContent, [1, 2], [1]),
        out BoundWorldPack? bound, out _));
    return new WorldPreparedView(bound!);
}

static PreparedWorldComposition W4Composition(
    SpatialSpaceId space,
    WorldStructuralManifest manifest,
    WorldCompositionRevision revision,
    params PreparedWorldPack[] packs)
{
    True(PreparedWorldComposition.TryCreate(space, revision, manifest, packs,
        out PreparedWorldComposition? composition, out PreparedWorldCompositionError error));
    Equal(PreparedWorldCompositionError.None, error);
    return composition!;
}

static void W6StructuralStateHierarchyAndCompatibility()
{
    SpatialSpaceId space = WorldPackTestProfile.Space;
    WorldPack pack = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false).Open();
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? bound, out _));
    var origin = new WorldPackCoord(0, 0);
    var east = new WorldPackCoord(1, 0);
    WorldPackFingerprint eastFingerprint = StructuralTestFingerprint(77);
    WorldStructuralManifest manifest = StructuralManifest(
        new(origin, pack.Fingerprint), new(east, eastFingerprint));
    PreparedWorldComposition composition = W4Composition(space, manifest, new(1),
        new PreparedWorldPack(origin, new WorldPreparedView(bound!)));
    var absentPack = new SpatialChunkKey(space, 512, 0);
    var unprepared = new SpatialChunkKey(space, 128, 0);
    var missingChunk = new SpatialChunkKey(space, 20, 0);
    var authored = new SpatialChunkKey(space, 0, 0);
    var present = new WorldPosition(0, 0, 1_000, 1_000, int.MaxValue);
    var voidCell = new WorldPosition(0, 0, 1_750, 2_250, int.MinValue);
    var missingPoint = new WorldPosition(20, 0, 1_750, 2_250, 0);
    var unpreparedPoint = new WorldPosition(128, 0, 1_750, 2_250, 0);
    var absentPackPoint = new WorldPosition(512, 0, 1_750, 2_250, 0);

    (SpatialChunkKey Key, WorldStructuralChunkStatus Expected, WorldPackFingerprint Fingerprint)[] chunks =
    [
        (absentPack, WorldStructuralChunkStatus.NotAuthoredPack, default),
        (unprepared, WorldStructuralChunkStatus.KnownPackButNotPrepared, eastFingerprint),
        (missingChunk, WorldStructuralChunkStatus.AuthoredPackMissingChunk, pack.Fingerprint),
        (authored, WorldStructuralChunkStatus.PreparedChunk, pack.Fingerprint),
    ];
    for (int repeat = 0; repeat < 3; repeat++)
        for (int index = chunks.Length - 1; index >= 0; index--)
        {
            var row = chunks[(index + repeat) % chunks.Length];
            True(composition.TryProbeStructuralChunk(row.Key, out WorldStructuralChunkProbe probe));
            Equal(row.Expected, probe.Status);
            Equal(row.Fingerprint, probe.ExpectedPackFingerprint);
            WorldPackAddressing.ToPackLocal(row.Key.Coord, out WorldPackCoord owner, out _);
            Equal(owner, probe.OwnerPack);
        }
    False(composition.TryProbeStructuralChunk(new SpatialChunkKey(new SpatialSpaceId(902), 512, 0),
        out WorldStructuralChunkProbe wrongSpace));
    Equal(default(WorldStructuralChunkProbe), wrongSpace);

    (WorldPosition Point, WorldBaseTerrainResolutionStatus Detailed, BaseTerrainSurfaceStatus Legacy)[] cells =
    [
        (absentPackPoint, WorldBaseTerrainResolutionStatus.NotAuthoredPack, BaseTerrainSurfaceStatus.Unavailable),
        (unpreparedPoint, WorldBaseTerrainResolutionStatus.KnownPackButNotPrepared, BaseTerrainSurfaceStatus.Unavailable),
        (missingPoint, WorldBaseTerrainResolutionStatus.AuthoredPackMissingChunk, BaseTerrainSurfaceStatus.Unavailable),
        (voidCell, WorldBaseTerrainResolutionStatus.IntentionalVoid, BaseTerrainSurfaceStatus.Absent),
        (present, WorldBaseTerrainResolutionStatus.Present, BaseTerrainSurfaceStatus.Present),
    ];
    Span<WorldTerrainColumnSurface> column = stackalloc WorldTerrainColumnSurface[8];
    for (int index = 0; index < cells.Length; index++)
    {
        var row = cells[index];
        True(composition.TryProbeBaseTerrainResolution(row.Point, out WorldBaseTerrainResolution resolution));
        Equal(row.Detailed, resolution.Status);
        Equal(row.Legacy, composition.ProbeBaseTerrainSurface(row.Point).Status);
        Equal(index < 3 ? WorldTerrainColumnQueryStatus.Unavailable : WorldTerrainColumnQueryStatus.Complete,
            composition.TryCopyTerrainColumnSurfaces(row.Point, column, out int count, out _));
        Equal(index == 4 ? 1 : 0, count);
        Equal(index == 4, composition.TrySampleBaseTerrainHeight(row.Point, out _));
        Equal(index == 4, composition.TryResolveBaseTerrainMaterial(row.Point, out _));
        if (index == 4)
        {
            True(resolution.MaterialId != 0);
            Equal(composition.ProbeBaseTerrainSurface(row.Point).HeightMillimeters, resolution.HeightMillimeters);
        }
        else
        {
            Equal(0, resolution.HeightMillimeters);
            Equal((ushort)0, resolution.MaterialId);
        }
    }
    True(composition.TryGetBaseTerrain(authored, out _)); // Absent cell does not remove the chunk.
    True(composition.TryProbeBaseTerrainResolution(
        new WorldPosition(0, 0, 1_750, 2_250, int.MaxValue), out WorldBaseTerrainResolution yChanged));
    Equal(WorldBaseTerrainResolutionStatus.IntentionalVoid, yChanged.Status);

    WorldStructuralManifest emptyManifest = StructuralManifest();
    PreparedWorldComposition empty = W4Composition(space, emptyManifest, new(1));
    True(empty.TryProbeStructuralChunk(new SpatialChunkKey(space, int.MinValue, int.MaxValue),
        out WorldStructuralChunkProbe extreme));
    Equal(WorldStructuralChunkStatus.NotAuthoredPack, extreme.Status);
    True(empty.TryProbeStructuralChunk(authored, out WorldStructuralChunkProbe emptyResult));
    Equal(WorldStructuralChunkStatus.NotAuthoredPack, emptyResult.Status);

    // The manifest identity is authored truth; a different immutable prepared subset changes only residency.
    WorldPreparedView eastView = W4Shard(east, 0, 128, space: space);
    WorldStructuralManifest transitionManifest = StructuralManifest(
        new(origin, pack.Fingerprint), new(east, eastView.BaseWorldFingerprint));
    PreparedWorldComposition before = W4Composition(space, transitionManifest, new(1),
        new PreparedWorldPack(origin, new WorldPreparedView(bound!)));
    PreparedWorldComposition after = W4Composition(space, transitionManifest, new(1),
        new PreparedWorldPack(origin, new WorldPreparedView(bound!)), new PreparedWorldPack(east, eastView));
    Equal(before.ManifestFingerprint, after.ManifestFingerprint);
    True(before.TryProbeStructuralChunk(unprepared, out WorldStructuralChunkProbe beforeProbe));
    True(after.TryProbeStructuralChunk(unprepared, out WorldStructuralChunkProbe afterProbe));
    Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, beforeProbe.Status);
    Equal(WorldStructuralChunkStatus.PreparedChunk, afterProbe.Status);
    Equal(beforeProbe.ExpectedPackFingerprint, afterProbe.ExpectedPackFingerprint);

    // A large sparse manifest must stay indexed by entries, not by coordinate span.
    var entries = new WorldStructuralManifestEntry[10_000];
    for (int index = 0; index < entries.Length; index++)
        entries[index] = new(new WorldPackCoord(index + 1_000, 0), eastFingerprint);
    entries[0] = new(origin, pack.Fingerprint);
    PreparedWorldComposition sparse = W4Composition(space, StructuralManifest(entries), new(1),
        new PreparedWorldPack(origin, new WorldPreparedView(bound!)));
    var sparseKnown = new SpatialChunkKey(space, 1_005 * 256, 0);
    var sparseAbsent = new SpatialChunkKey(space, 20_000 * 256, 0);
    _ = sparse.TryProbeStructuralChunk(sparseKnown, out _);
    _ = composition.TryProbeBaseTerrainResolution(present, out _);
    long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    int checksum = 0;
    for (int index = 0; index < 100_000; index++)
    {
        SpatialChunkKey key = index % 3 == 0 ? sparseKnown : index % 3 == 1 ? sparseAbsent : authored;
        if (!sparse.TryProbeStructuralChunk(key, out WorldStructuralChunkProbe probe)) throw new Exception();
        checksum += (int)probe.Status;
        if (!composition.TryProbeBaseTerrainResolution(cells[index % cells.Length].Point,
                out WorldBaseTerrainResolution baseResult)) throw new Exception();
        checksum += (int)baseResult.Status;
    }
    long ticks = Stopwatch.GetTimestamp() - started;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
    Equal(0L, allocated);
    True(checksum != 0);
    Console.WriteLine($"W6 MATRIX states=5 sparse_manifest=10000 probes=100000 ticks={ticks} allocated={allocated}");
}

static void W6ConceptDemoStructuralStatesAreCanonical()
{
    ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
    PreparedWorldComposition complete = source.Prepare(ConceptDemoStructuralSource.CreateBinding());
    Equal(162, complete.PreparedChunkCount);
    True(complete.TryValidateComplete(out _));
    Equal("A76FAE182AA566BE91D312E26FCE29EA6E2854E84402D50579A6C5C2009553CB",
        source.West.Fingerprint.ToString());
    Equal("CE9F0CCDB591F3C3E592B8105E43C1AEB8323A5CB686533FC4735A8AA15D7B1D",
        source.East.Fingerprint.ToString());
    Equal("41DB6F68C26AC85DF5ABEB60BEDA3F3F07DD1E76CF30A5164874446D902B6B53",
        source.Manifest.Fingerprint.ToString());
    Equal("323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795",
        ConceptDemoStructuralSource.LegacyCombinedFingerprint.ToString());
    SpatialSpaceId space = complete.Space;
    var notAuthored = new SpatialChunkKey(space, 384, 0); // Pack (2,0).
    var missingChunk = new SpatialChunkKey(space, 40, 40); // Inside prepared Pack (0,0), outside both clusters.
    WorldPosition sky = GamePlayableWorld.SkyTelepadDestination;
    WorldPosition ground = GamePlayableWorld.PlayerStart;
    True(complete.TryProbeStructuralChunk(notAuthored, out WorldStructuralChunkProbe outside));
    Equal(WorldStructuralChunkStatus.NotAuthoredPack, outside.Status);
    Equal(new WorldPackCoord(2, 0), outside.OwnerPack);
    False(outside.ExpectedPackFingerprint.IsValid);
    True(complete.TryProbeStructuralChunk(missingChunk, out WorldStructuralChunkProbe gap));
    Equal(WorldStructuralChunkStatus.AuthoredPackMissingChunk, gap.Status);
    Equal(ConceptDemoStructuralSource.WestCoord, gap.OwnerPack);
    Equal(source.West.Fingerprint, gap.ExpectedPackFingerprint);
    True(complete.TryProbeBaseTerrainResolution(sky, out WorldBaseTerrainResolution skyBase));
    Equal(WorldBaseTerrainResolutionStatus.IntentionalVoid, skyBase.Status);
    Equal(BaseTerrainSurfaceStatus.Absent, complete.ProbeBaseTerrainSurface(sky).Status);
    Span<WorldTerrainColumnSurface> surfaces = stackalloc WorldTerrainColumnSurface[8];
    Equal(WorldTerrainColumnQueryStatus.Complete,
        complete.TryCopyTerrainColumnSurfaces(sky, surfaces, out int skyCount, out _));
    True(skyCount > 0);
    bool skySurface = false;
    for (int index = 0; index < skyCount; index++)
        skySurface |= surfaces[index].SurfaceId == GamePlayableWorld.SkyIslandSurface;
    True(skySurface);
    True(complete.TryProbeBaseTerrainResolution(ground, out WorldBaseTerrainResolution groundBase));
    Equal(WorldBaseTerrainResolutionStatus.Present, groundBase.Status);
    True(groundBase.MaterialId != 0);
    Equal(BaseTerrainSurfaceStatus.Present, complete.ProbeBaseTerrainSurface(ground).Status);

    True(complete.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord, out PreparedWorldPack west));
    PreparedWorldComposition partial = W4Composition(space, source.Manifest, complete.Revision, west);
    var farEast = new SpatialChunkKey(space, 128, 0);
    True(partial.TryProbeStructuralChunk(farEast, out WorldStructuralChunkProbe pending));
    Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, pending.Status);
    Equal(source.East.Fingerprint, pending.ExpectedPackFingerprint);
    True(complete.TryProbeStructuralChunk(farEast, out WorldStructuralChunkProbe ready));
    Equal(WorldStructuralChunkStatus.PreparedChunk, ready.Status);
    Equal(partial.ManifestFingerprint, complete.ManifestFingerprint);
    Equal(pending.ExpectedPackFingerprint, ready.ExpectedPackFingerprint);

    using var ecs = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(ecs, preparedComposition: complete);
    True(state.PreparedWorld is null && state.PreparedComposition is not null);
    True(state.TryProbeStructuralChunk(missingChunk, out WorldStructuralChunkProbe gameGap));
    Equal(gap, gameGap);
    True(state.TryProbeBaseTerrainResolution(sky, out WorldBaseTerrainResolution gameSky));
    Equal(skyBase, gameSky);
    using var legacyEcs = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var legacy = new GameWorldState(legacyEcs, baseWorld: west.View.BaseWorld);
    False(legacy.TryProbeStructuralChunk(missingChunk, out _));
    False(legacy.TryProbeBaseTerrainResolution(sky, out _));
    Equal(BaseTerrainSurfaceStatus.Absent, legacy.ProbeBaseTerrainSurface(sky).Status);
    Console.WriteLine($"W6 CONCEPT not_authored={notAuthored} missing={missingChunk} " +
        $"sky={skyBase.Status}+T1.1({skyCount}) ground={groundBase.Status} far={pending.Status}->{ready.Status}");
}

static void PreparedCompositionValidatesCompleteCandidateAndSparseSubset()
{
    SpatialSpaceId space = WorldPackTestProfile.Space;
    WorldPackCoord origin = new(0, 0);
    WorldPackCoord east = new(1, 0);
    WorldPreparedView first = W4Shard(origin);
    WorldPreparedView second = W4Shard(east, 0, 128);
    WorldStructuralManifest manifest = StructuralManifest(
        new WorldStructuralManifestEntry(origin, first.BaseWorldFingerprint),
        new WorldStructuralManifestEntry(east, second.BaseWorldFingerprint));
    PreparedWorldComposition empty = W4Composition(space, manifest, new(1));
    Equal(0, empty.PreparedPackCount);
    Equal(2, empty.ManifestEntryCount);
    False(empty.TryGetBaseTerrain(new SpatialChunkKey(space, 0, 0), out _));
    WorldStructuralManifest emptyManifest = StructuralManifest();
    Equal(0, W4Composition(space, emptyManifest, new(1)).PreparedPackCount);
    PreparedWorldComposition subset = W4Composition(space, manifest, new(1), new PreparedWorldPack(origin, first));
    Equal(1, subset.PreparedPackCount);
    Equal(manifest.Fingerprint, subset.ManifestFingerprint);
    False(subset.TryGetBaseTerrain(new SpatialChunkKey(space, 128, 0), out _));
    False(subset.TryGetBaseTerrain(new SpatialChunkKey(space, 20, 20), out _));
    False(subset.TryGetBaseTerrain(new SpatialChunkKey(new SpatialSpaceId(991), 0, 0), out _));
    PreparedWorldPack[] input = [new(east, second), new(origin, first)];
    PreparedWorldComposition complete = W4Composition(space, manifest, new(2), input);
    input[0] = default;
    Equal(2, complete.PreparedPackCount);
    True(complete.TryGetPreparedPackAt(0, out PreparedWorldPack ordered));
    Equal(origin, ordered.Coord);
    True(complete.TryGetPreparedPack(east, out PreparedWorldPack found));
    True(ReferenceEquals(second, found.View));
    Equal(manifest.Fingerprint, complete.ManifestFingerprint);
    Equal(manifest.Fingerprint, subset.ManifestFingerprint);
    Equal(2UL, complete.Revision.Value);
    Equal(1UL, subset.Revision.Value);

    void Reject(WorldStructuralManifest source, SpatialSpaceId targetSpace,
        WorldCompositionRevision revision, PreparedWorldCompositionError expected,
        params PreparedWorldPack[] candidates)
    {
        False(PreparedWorldComposition.TryCreate(targetSpace, revision, source, candidates,
            out PreparedWorldComposition? rejected, out PreparedWorldCompositionError error));
        Equal(null, rejected);
        Equal(expected, error);
    }
    Reject(manifest, space, default, PreparedWorldCompositionError.InvalidRevision);
    Reject(manifest, default, new(1), PreparedWorldCompositionError.InvalidSpace);
    Reject(manifest, space, new(1), PreparedWorldCompositionError.DuplicatePreparedPack,
        new PreparedWorldPack(origin, first), new PreparedWorldPack(origin, first));
    Reject(StructuralManifest(new WorldStructuralManifestEntry(east, second.BaseWorldFingerprint)), space, new(1),
        PreparedWorldCompositionError.PackNotInManifest, new PreparedWorldPack(origin, first));
    Reject(StructuralManifest(new WorldStructuralManifestEntry(origin, StructuralTestFingerprint(7))), space, new(1),
        PreparedWorldCompositionError.PackFingerprintMismatch, new PreparedWorldPack(origin, first));
    Reject(StructuralManifest(new WorldStructuralManifestEntry(origin, second.BaseWorldFingerprint)), space, new(1),
        PreparedWorldCompositionError.PackWindowMismatch, new PreparedWorldPack(origin, second));
    WorldPreparedView foreignSpace = W4Shard(origin, space: new SpatialSpaceId(992));
    Reject(StructuralManifest(new WorldStructuralManifestEntry(origin, foreignSpace.BaseWorldFingerprint)), space, new(1),
        PreparedWorldCompositionError.PackSpaceMismatch, new PreparedWorldPack(origin, foreignSpace));
    WorldPreparedView foreignContent = W4Shard(east, 0, 128,
        content: new WorldContentFingerprint(123, 456));
    Reject(StructuralManifest(new WorldStructuralManifestEntry(origin, first.BaseWorldFingerprint),
            new WorldStructuralManifestEntry(east, foreignContent.BaseWorldFingerprint)), space, new(1),
        PreparedWorldCompositionError.ContentBindingMismatch,
        new PreparedWorldPack(origin, first), new PreparedWorldPack(east, foreignContent));
}

static void PreparedCompositionMatchesOnePackQueriesAndOverlay()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(false, GameContent.WorldFingerprint,
        GameWorldState.DefaultSpace).Open();
    True(pack.TryValidateSpatialWindow(new WorldPackCoord(0, 0), out _, out _));
    True(pack.TryBind(CreateGameWorldBinding(), out BoundWorldPack? bound, out _));
    SpatialSpaceId space = bound!.Space;
    var coord = new WorldPackCoord(0, 0);
    var key = new SpatialChunkKey(space, 0, 0);
    var point = new WorldPosition(0, 0, 1_000, 1_000, 0);
    WorldStructuralManifest manifest = StructuralManifest(new WorldStructuralManifestEntry(coord, bound.Fingerprint));
    var direct = new WorldPreparedView(bound);
    PreparedWorldComposition composition = W4Composition(space, manifest, new(1), new PreparedWorldPack(coord, direct));
    Equal(direct.ProbeBaseTerrainSurface(point), composition.ProbeBaseTerrainSurface(point));
    Equal(direct.TrySampleBaseTerrainHeight(point, out int directHeight),
        composition.TrySampleBaseTerrainHeight(point, out int composedHeight));
    Equal(directHeight, composedHeight);
    Equal(direct.TryResolveBaseTerrainMaterial(point, out ushort directMaterial),
        composition.TryResolveBaseTerrainMaterial(point, out ushort composedMaterial));
    Equal(directMaterial, composedMaterial);
    True(direct.TryGetBaseTerrain(key, out WorldTerrainChunkView directTerrain));
    True(composition.TryGetBaseTerrain(key, out WorldTerrainChunkView composedTerrain));
    Equal(directTerrain.GetMaterial(0), composedTerrain.GetMaterial(0));
    True(direct.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView directStatics));
    True(composition.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView composedStatics));
    Equal(directStatics.Count, composedStatics.Count);
    for (int index = 0; index < directStatics.Count; index++)
        Equal(directStatics.GetRecord(index), composedStatics.GetRecord(index));
    True(direct.TryGetMultiFragments(key, out WorldMultiFragmentChunkView directMulti));
    True(composition.TryGetMultiFragments(key, out WorldMultiFragmentChunkView composedMulti));
    Equal(directMulti.Count, composedMulti.Count);
    for (int index = 0; index < directMulti.Count; index++)
        Equal(directMulti.GetRecord(index), composedMulti.GetRecord(index));
    Equal(direct.TryGetAdditionalTerrainSurfaceBands(key, out WorldTerrainSurfaceBandChunkView directBands),
        composition.TryGetAdditionalTerrainSurfaceBands(key, out WorldTerrainSurfaceBandChunkView composedBands));
    Equal(directBands.Count, composedBands.Count);
    Span<WorldTerrainColumnSurface> directColumn = stackalloc WorldTerrainColumnSurface[8];
    Span<WorldTerrainColumnSurface> composedColumn = stackalloc WorldTerrainColumnSurface[8];
    Equal(direct.TryCopyTerrainColumnSurfaces(point, directColumn, out int dc, out int dr),
        composition.TryCopyTerrainColumnSurfaces(point, composedColumn, out int cc, out int cr));
    Equal(dc, cc);
    Equal(dr, cr);
    True(directColumn[..dc].SequenceEqual(composedColumn[..cc]));
    Equal(direct.TryFindHighestSupportAtOrBelow(point, int.MaxValue, out WorldTerrainColumnSurface ds),
        composition.TryFindHighestSupportAtOrBelow(point, int.MaxValue, out WorldTerrainColumnSurface cs));
    Equal(ds, cs);

    var verticesA = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    var verticesB = new TerrainBaseMeshVertex[verticesA.Length];
    var indicesA = new ushort[TerrainChunkFormat.TriangleIndexCount];
    var indicesB = new ushort[indicesA.Length];
    var materialsA = new ushort[TerrainChunkFormat.TriangleCount];
    var materialsB = new ushort[materialsA.Length];
    True(direct.TryBuildBaseTerrainMesh(key, verticesA, indicesA, materialsA, out var summaryA, out _));
    True(composition.TryBuildBaseTerrainMesh(key, verticesB, indicesB, materialsB, out var summaryB, out _));
    Equal(summaryA, summaryB);
    True(verticesA.AsSpan().SequenceEqual(verticesB));
    True(indicesA.AsSpan().SequenceEqual(indicesB));
    True(materialsA.AsSpan().SequenceEqual(materialsB));
    var bounds = new WorldStaticBounds(-16_000, -2_000, -16_000, 16_000, 4_000, 16_000);
    var hitsA = new WorldStaticQueryHit[16];
    var hitsB = new WorldStaticQueryHit[16];
    True(direct.TryQueryStaticBounds(bounds, 32, hitsA, out int countA, out int requiredA, out _));
    True(composition.TryQueryStaticBounds(bounds, 32, hitsB, out int countB, out int requiredB, out _));
    Equal(countA, countB);
    Equal(requiredA, requiredB);
    True(hitsA.AsSpan(0, countA).SequenceEqual(hitsB.AsSpan(0, countB)));

    WorldMutableOverlay overlay = CreateReplacementOverlay(bound, key, GameTerrainMaterialIds.Snow,
        new WorldStaticId(9_555));
    direct = new WorldPreparedView(bound, overlay);
    composition = W4Composition(space, manifest, new(2), new PreparedWorldPack(coord, direct));
    Equal(direct.ProbeBaseTerrainSurface(point), composition.ProbeBaseTerrainSurface(point));
    Equal(direct.TryGetReplacementIdentity(key, out WorldChunkContentRevision directRev,
            out WorldChunkContentHash directHash),
        composition.TryGetReplacementIdentity(key, out WorldChunkContentRevision composedRev,
            out WorldChunkContentHash composedHash));
    Equal(directRev, composedRev);
    Equal(directHash, composedHash);
    True(composition.TryQueryStaticBounds(bounds, 32, hitsB, out countB, out requiredB, out _));
    True(direct.TryQueryStaticBounds(bounds, 32, hitsA, out countA, out requiredA, out _));
    True(hitsA.AsSpan(0, countA).SequenceEqual(hitsB.AsSpan(0, countB)));

    WorldPack voidPack = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false).Open();
    True(voidPack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? voidBound, out _));
    var voidView = new WorldPreparedView(voidBound!);
    var voidComposition = W4Composition(voidBound!.Space,
        StructuralManifest(new WorldStructuralManifestEntry(coord, voidBound.Fingerprint)), new(1),
        new PreparedWorldPack(coord, voidView));
    for (int x = 0; x < 16_000; x += 500)
    for (int z = 0; z < 16_000; z += 500)
    {
        var location = new WorldPosition(0, 0, checked((ushort)x), checked((ushort)z), 0);
        Equal(voidView.ProbeBaseTerrainSurface(location), voidComposition.ProbeBaseTerrainSurface(location));
    }

    var shellBuilder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(512 * 1024, 1, 8), coord);
    var shellMaterials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(shellMaterials, (ushort)1);
    var shellHeights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase zero));
    var shellKey = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    True(shellBuilder.TryAddBaseTerrain(shellKey, shellMaterials, zero, shellHeights, out _));
    True(shellBuilder.TryAddAdditionalTerrainSurfaceBand(shellKey, new WorldSurfaceId(11),
        new TerrainSurfacePatchBounds(0, 0, 1, 1), WorldTerrainSurfaceFaces.Top,
        [1UL], [TerrainSurfaceEdgeConnection.None], [2], zero,
        [new TerrainHeightOffset(0), new TerrainHeightOffset(0),
            new TerrainHeightOffset(0), new TerrainHeightOffset(0)],
        [], default, [], 0, out _));
    True(shellBuilder.TryBuild(out WorldPackArtifact? shellArtifact, out _));
    True(shellArtifact!.Open().TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2]),
        out BoundWorldPack? shellBound, out _));
    var shellView = new WorldPreparedView(shellBound!);
    var shellComposition = W4Composition(WorldPackTestProfile.Space,
        StructuralManifest(new WorldStructuralManifestEntry(coord, shellBound!.Fingerprint)), new(1),
        new PreparedWorldPack(coord, shellView));
    True(shellComposition.TryGetAdditionalTerrainSurfaceBands(shellKey, out WorldTerrainSurfaceBandChunkView shellBands));
    Equal(1, shellBands.Count);
    var shellPoint = new WorldPosition(0, 0, 100, 100, 0);
    Equal(shellView.TryCopyTerrainColumnSurfaces(shellPoint, directColumn, out dc, out dr),
        shellComposition.TryCopyTerrainColumnSurfaces(shellPoint, composedColumn, out cc, out cr));
    Equal(2, dc);
    True(directColumn[..dc].SequenceEqual(composedColumn[..cc]));
    Equal(shellView.TryFindNearestSupport(shellPoint, 1, out ds),
        shellComposition.TryFindNearestSupport(shellPoint, 1, out cs));
    Equal(ds, cs);
}

static void PreparedCompositionRoutesDistantAndCrossPackStatics()
{
    SpatialSpaceId space = WorldPackTestProfile.Space;
    var origin = new WorldPackCoord(0, 0);
    var east = new WorldPackCoord(1, 0);
    var distant = new WorldPackCoord(2000, -500);
    WorldPreparedView left = W4Shard(origin, 255, 128, staticId: 101);
    WorldPreparedView right = W4Shard(east, 0, 128, staticId: 102);
    WorldPreparedView far = W4Shard(distant);
    var manifest = StructuralManifest(new WorldStructuralManifestEntry(origin, left.BaseWorldFingerprint),
        new WorldStructuralManifestEntry(east, right.BaseWorldFingerprint),
        new WorldStructuralManifestEntry(distant, far.BaseWorldFingerprint));
    PreparedWorldComposition composition = W4Composition(space, manifest, new(1),
        new PreparedWorldPack(distant, far), new PreparedWorldPack(east, right),
        new PreparedWorldPack(origin, left));
    Equal(3, composition.PreparedPackCount);
    Equal(3L * (sizeof(int) * 2 + IntPtr.Size), composition.PreparedIndexReservedBytes);
    True(composition.TryGetBaseTerrain(new SpatialChunkKey(space, 127, 0), out _));
    True(composition.TryGetBaseTerrain(new SpatialChunkKey(space, 128, 0), out _));
    True(composition.TryGetBaseTerrain(new SpatialChunkKey(space, 512_000, -128_000), out _));
    False(composition.TryGetBaseTerrain(new SpatialChunkKey(space, 256_000, 0), out _));
    long seam = 128L * GameWorldSpatialProfile.WorldChunkEdgeUnits;
    var bounds = new WorldStaticBounds(seam - 500, -400, 0, seam + 500, 400, 2_000);
    var hits = new WorldStaticQueryHit[4];
    True(composition.TryQueryStaticBounds(bounds, 16, hits, out int count, out int required, out _));
    Equal(2, count);
    Equal(2, required);
    True(hits[0].StaticId != hits[1].StaticId);
    var leftKey = new SpatialChunkKey(space, 127, 0);
    var rightKey = new SpatialChunkKey(space, 128, 0);
    WorldMutableOverlay? leftOverlay = null;
    WorldMutableOverlay? rightOverlay = null;
    for (int index = 0; index < 2; index++)
        leftOverlay = CreateReplacementOverlay(left.BaseWorld, leftKey, 2,
            new WorldStaticId((ulong)(201 + index)), leftOverlay);
    for (int index = 0; index < 7; index++)
        rightOverlay = CreateReplacementOverlay(right.BaseWorld, rightKey, 2,
            new WorldStaticId((ulong)(301 + index)), rightOverlay);
    PreparedWorldComposition changed = W4Composition(space, manifest, new(3),
        new(origin, new WorldPreparedView(left.BaseWorld, leftOverlay)),
        new(east, new WorldPreparedView(right.BaseWorld, rightOverlay)),
        new(distant, far));
    Equal(manifest.Fingerprint, changed.ManifestFingerprint);
    True(changed.TryGetReplacementIdentity(leftKey, out WorldChunkContentRevision leftRevision, out _));
    True(changed.TryGetReplacementIdentity(rightKey, out WorldChunkContentRevision rightRevision, out _));
    Equal(2UL, leftOverlay!.Revision.Value);
    Equal(7UL, rightOverlay!.Revision.Value);
    Equal(2UL, leftRevision.Value);
    Equal(7UL, rightRevision.Value);
    Console.WriteLine($"W4 SPARSE prepared={composition.PreparedPackCount} index_bytes={composition.PreparedIndexReservedBytes} seam_statics={count}");
}

static void PreparedCompositionTerrainMeshHaloReadsNeighborPack()
{
    BoundWorldPack combined = CreateBoundTerrainWorld(127, 128, 0, 0, SmoothHeight, QueryMaterial);
    BoundWorldPack left = CreateBoundTerrainWorld(127, 127, 0, 0, SmoothHeight, QueryMaterial);
    BoundWorldPack right = CreateBoundTerrainWorld(128, 128, 0, 0, SmoothHeight, QueryMaterial);
    var leftCoord = new WorldPackCoord(0, 0);
    var rightCoord = new WorldPackCoord(1, 0);
    True(left.Pack.TryValidateSpatialWindow(leftCoord, out _, out _));
    True(right.Pack.TryValidateSpatialWindow(rightCoord, out _, out _));
    var composition = W4Composition(left.Space,
        StructuralManifest(new WorldStructuralManifestEntry(leftCoord, left.Fingerprint),
            new WorldStructuralManifestEntry(rightCoord, right.Fingerprint)), new(1),
        new PreparedWorldPack(leftCoord, new WorldPreparedView(left)),
        new PreparedWorldPack(rightCoord, new WorldPreparedView(right)));
    var legacy = new WorldPreparedView(combined);
    var firstVertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    var secondVertices = new TerrainBaseMeshVertex[firstVertices.Length];
    var firstIndices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    var secondIndices = new ushort[firstIndices.Length];
    var firstMaterials = new ushort[TerrainChunkFormat.TriangleCount];
    var secondMaterials = new ushort[firstMaterials.Length];
    foreach (int x in new[] { 127, 128 })
    {
        var key = new SpatialChunkKey(left.Space, x, 0);
        True(legacy.TryBuildBaseTerrainMesh(key, firstVertices, firstIndices, firstMaterials,
            out TerrainBaseMeshSummary firstSummary, out _));
        True(composition.TryBuildBaseTerrainMesh(key, secondVertices, secondIndices, secondMaterials,
            out TerrainBaseMeshSummary secondSummary, out _));
        Equal(firstSummary, secondSummary);
        True(firstVertices.AsSpan().SequenceEqual(secondVertices));
        True(firstIndices.AsSpan().SequenceEqual(secondIndices));
        True(firstMaterials.AsSpan().SequenceEqual(secondMaterials));
    }
}

static void GameWorldStateCanonicalCompositionIsExclusiveAndNotWorldviewV1()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(false, GameContent.WorldFingerprint,
        GameWorldState.DefaultSpace).Open();
    True(pack.TryBind(CreateGameWorldBinding(), out BoundWorldPack? bound, out _));
    var coord = new WorldPackCoord(0, 0);
    PreparedWorldComposition composition = W4Composition(GameWorldState.DefaultSpace,
        StructuralManifest(new WorldStructuralManifestEntry(coord, bound!.Fingerprint)), new(1),
        new PreparedWorldPack(coord, new WorldPreparedView(bound)));
    using var legacyWorld = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var canonicalWorld = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var legacy = new GameWorldState(legacyWorld, bound);
    using var canonical = new GameWorldState(canonicalWorld, preparedComposition: composition);
    Equal(null, canonical.BaseWorld);
    Equal(null, canonical.PreparedWorld);
    True(ReferenceEquals(composition, canonical.PreparedComposition));
    False(canonical.TryInstallPreparedWorld(bound, null));
    False(legacy.TryInstallPreparedComposition(composition));
    var point = new WorldPosition(0, 0, 1_000, 1_000, 0);
    Equal(legacy.ProbeBaseTerrainSurface(point), canonical.ProbeBaseTerrainSurface(point));
    Equal(legacy.TryResolveBaseTerrainMaterial(point, out ushort a),
        canonical.TryResolveBaseTerrainMaterial(point, out ushort b));
    Equal(a, b);
    var bounds = new WorldStaticBounds(-16_000, -2_000, -16_000, 16_000, 4_000, 16_000);
    var hitsA = new WorldStaticQueryHit[16];
    var hitsB = new WorldStaticQueryHit[16];
    True(legacy.TryQueryStaticBounds(bounds, 32, hitsA, out int countA, out _, out _));
    True(canonical.TryQueryStaticBounds(bounds, 32, hitsB, out int countB, out _, out _));
    True(hitsA.AsSpan(0, countA).SequenceEqual(hitsB.AsSpan(0, countB)));
    var body = new SimplePhysicsBody(200, 200, 100, 100, 1_700, 300, 300);
    var occupancy = new SimpleOccupancyRequest(new WorldPosition(0, 0, 8_000, 8_000, 0), body);
    Equal(new UltraSimSimplePhysics(legacy).ProbeOccupancy(occupancy, [], true),
        new UltraSimSimplePhysics(canonical).ProbeOccupancy(occupancy, [], true));
    using var stagingWorld = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var staging = new GameWorldState(stagingWorld);
    True(staging.TryInstallPreparedComposition(composition));
    False(staging.TryInstallPreparedComposition(composition));
    False(staging.TryPublishOverlay(CreateReplacementOverlay(bound, new SpatialChunkKey(bound.Space, 0, 0),
        GameTerrainMaterialIds.Snow, new WorldStaticId(9_777))));
    var participant = new WorldSaveSetParticipant(canonical, new PersistentWorldId(0x7444),
        new WorldPersistenceOptions(1_000_000));
    using (ICapturedSaveSetParticipant captured = participant.Capture(new SaveSetContext(1, 0)))
    using (var encoded = new MemoryStream())
    {
        captured.WriteArtifact(encoded);
        Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(encoded.GetBuffer().AsSpan(4)));
    }
    PreparedWorldComposition empty = W4Composition(GameWorldState.DefaultSpace,
        StructuralManifest(), new(1));
    using var emptyWorld = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var emptyState = new GameWorldState(emptyWorld, preparedComposition: empty);
    Equal(BaseTerrainSurfaceStatus.Unavailable, emptyState.ProbeBaseTerrainSurface(point).Status);
    False(emptyState.TrySampleBaseTerrainHeight(point, out _));
    True(emptyState.TryQueryStaticBounds(bounds, 32, hitsB, out countB, out _, out _));
    Equal(0, countB);
    Throws<ArgumentException>(() => _ = new GameWorldState(canonicalWorld, bound,
        preparedComposition: composition));
}

static void W5CompleteTwoPackValidationAndIdentityCollisions()
{
    var (valid, west, east) = W5TwoPackFixture.Create();
    True(valid.TryValidateComplete(out PreparedWorldCompositionError validError));
    Equal(PreparedWorldCompositionError.None, validError);
    Equal(2, valid.PreparedPackCount);
    Equal(30, valid.PreparedChunkCount);
    Equal(W5TwoPackFixture.Space, valid.Space);
    True(west.Fingerprint != east.Fingerprint);
    var westKey = new SpatialChunkKey(valid.Space, 127, 0);
    var eastKey = new SpatialChunkKey(valid.Space, 128, 0);
    True(valid.TryGetBaseTerrain(westKey, out WorldTerrainChunkView westTerrain));
    True(valid.TryGetBaseTerrain(eastKey, out WorldTerrainChunkView eastTerrain));
    for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
        Equal(westTerrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
                new TerrainVertexCoord(32, vertex))),
            eastTerrain.GetAbsoluteHeightMillimeters(TerrainChunkFormat.GetHeightIndex(
                new TerrainVertexCoord(0, vertex))));
    var bounds = new WorldStaticBounds(127L * 16_000, -1_000, 0,
        129L * 16_000, 2_000, 8_000);
    Span<WorldStaticQueryHit> hits = stackalloc WorldStaticQueryHit[8];
    True(valid.TryQueryStaticBounds(bounds, 16, hits, out int count, out _, out _));
    Equal(4, count); // one frozen static and one Multi fragment per Pack.
    int frozen = 0;
    int multi = 0;
    for (int index = 0; index < count; index++)
    {
        if (hits[index].Kind == WorldStaticQueryKind.FrozenStatic) frozen++;
        if (hits[index].Kind == WorldStaticQueryKind.MultiFragment) multi++;
    }
    Equal(2, frozen);
    Equal(2, multi);
    True(valid.TryGetMultiFragments(westKey, out WorldMultiFragmentChunkView westPieces));
    True(valid.TryGetMultiFragments(eastKey, out WorldMultiFragmentChunkView eastPieces));
    Equal(1, westPieces.Count);
    Equal(1, eastPieces.Count);
    WorldMultiInstanceRecord westRoot = default;
    WorldMultiInstanceRecord eastRoot = default;
    True(valid.TryGetPreparedPack(W5TwoPackFixture.WestCoord, out PreparedWorldPack westPack) &&
        westPack.View.TryGetMultiInstance(new MultiInstanceId(1001), out westRoot));
    True(valid.TryGetPreparedPack(W5TwoPackFixture.EastCoord, out PreparedWorldPack eastPack) &&
        eastPack.View.TryGetMultiInstance(new MultiInstanceId(1002), out eastRoot));
    Equal(1, westRoot.PieceCount);
    Equal(1, eastRoot.PieceCount);
    var westVertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    var eastVertices = new TerrainBaseMeshVertex[westVertices.Length];
    var westIndices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    var eastIndices = new ushort[westIndices.Length];
    var westMaterials = new ushort[TerrainChunkFormat.TriangleCount];
    var eastMaterials = new ushort[westMaterials.Length];
    True(valid.TryBuildBaseTerrainMesh(westKey, westVertices, westIndices, westMaterials, out _, out _));
    True(valid.TryBuildBaseTerrainMesh(eastKey, eastVertices, eastIndices, eastMaterials, out _, out _));
    for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
    {
        int wi = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, vertex));
        int ei = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, vertex));
        Equal(westVertices[wi].AbsoluteYMillimeters, eastVertices[ei].AbsoluteYMillimeters);
        Equal(westVertices[wi].LocalZMillimeters, eastVertices[ei].LocalZMillimeters);
    }
    // Cell 31 of chunk 127 is globally odd; cell 0 of chunk 128 is globally even.
    Equal((ushort)32, westIndices[31 * 6 + 2]);
    Equal((ushort)34, eastIndices[2]);

    var (broken, _, _) = W5TwoPackFixture.Create(brokenSeam: true);
    False(broken.TryValidatePreparedSubset(out PreparedWorldCompositionError subsetSeamError));
    Equal(PreparedWorldCompositionError.TerrainSeamMismatch, subsetSeamError);
    False(broken.TryValidateComplete(out PreparedWorldCompositionError seamError));
    Equal(PreparedWorldCompositionError.TerrainSeamMismatch, seamError);
    var (duplicateStatic, _, _) = W5TwoPackFixture.Create(duplicateStaticId: true);
    False(duplicateStatic.TryValidatePreparedSubset(out PreparedWorldCompositionError subsetStaticError));
    Equal(PreparedWorldCompositionError.DuplicateWorldStaticId, subsetStaticError);
    False(duplicateStatic.TryValidateComplete(out PreparedWorldCompositionError staticError));
    Equal(PreparedWorldCompositionError.DuplicateWorldStaticId, staticError);
    var (duplicateMulti, _, _) = W5TwoPackFixture.Create(duplicateMultiId: true);
    False(duplicateMulti.TryValidatePreparedSubset(out PreparedWorldCompositionError subsetMultiError));
    Equal(PreparedWorldCompositionError.DuplicateMultiInstanceId, subsetMultiError);
    False(duplicateMulti.TryValidateComplete(out PreparedWorldCompositionError multiError));
    Equal(PreparedWorldCompositionError.DuplicateMultiInstanceId, multiError);
    var subset = W4Composition(valid.Space, valid.Manifest, valid.Revision,
        new PreparedWorldPack(W5TwoPackFixture.WestCoord, new WorldPreparedView(west)));
    True(subset.TryValidatePreparedSubset(out PreparedWorldCompositionError subsetError));
    Equal(PreparedWorldCompositionError.None, subsetError);
    False(subset.TryValidateComplete(out PreparedWorldCompositionError incomplete));
    Equal(PreparedWorldCompositionError.IncompleteComposition, incomplete);
    using var world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    Throws<ArgumentException>(() => _ = new GameWorldState(world, preparedComposition: broken));
    Throws<ArgumentException>(() => _ = new GameWorldState(world, preparedComposition: duplicateStatic));
    Throws<ArgumentException>(() => _ = new GameWorldState(world, preparedComposition: duplicateMulti));
    using var shell = new GameWorldState(world);
    False(shell.TryInstallPreparedComposition(broken));
    False(shell.TryInstallPreparedComposition(duplicateStatic));
    False(shell.TryInstallPreparedComposition(duplicateMulti));
    True(shell.TryInstallPreparedComposition(subset));
    var seamStart = new WorldPosition(127, 0, 15_500, 1_000, 0);
    var seamCandidate = new WorldPosition(128, 0, 500, 1_000, 0);
    using var completeState = new GameWorldState(world, preparedComposition: valid);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        completeState.TryFindNearestSupport(seamCandidate, seamStart.YMillimeters,
            out WorldTerrainColumnSurface crossPackSupport));
    True(crossPackSupport.IsSupport);
    Equal(0, crossPackSupport.HeightMillimeters);
    Console.WriteLine($"W5 TWO PACK west={west.Fingerprint} east={east.Fingerprint} " +
        $"manifest={valid.ManifestFingerprint} chunks={valid.PreparedChunkCount} static_hits={count}");
}

static void W5WorldviewV2RowsAndResolverFailures()
{
    var (source, west, east) = W5TwoPackFixture.Create();
    var westKey = new SpatialChunkKey(source.Space, 125, 0);
    var eastKey = new SpatialChunkKey(source.Space, 130, 0);
    True(west.TryGetBaseTerrain(westKey, out WorldTerrainChunkView westTerrain));
    var originalMaterials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var originalHeights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    westTerrain.CopyMaterials(originalMaterials);
    for (int index = 0; index < originalHeights.Length; index++)
        originalHeights[index] = westTerrain.GetHeightOffset(index);
    var changedMaterials = (ushort[])originalMaterials.Clone();
    Array.Fill(changedMaterials, (ushort)2);
    var first = new WorldMutableOverlayBuilder(west);
    True(first.TryReplaceTerrain(westKey, changedMaterials, westTerrain.HeightBase, originalHeights));
    True(first.TryBuild(out WorldMutableOverlay? westChanged));
    var revert = new WorldMutableOverlayBuilder(west, prior: westChanged);
    True(revert.TryReplaceTerrain(westKey, originalMaterials, westTerrain.HeightBase, originalHeights));
    True(revert.TryBuild(out WorldMutableOverlay? westRevisedEmpty));
    Equal(2UL, westRevisedEmpty!.Revision.Value);
    Equal(0, westRevisedEmpty.ChangedChunkCount);

    True(east.TryGetBaseTerrain(eastKey, out WorldTerrainChunkView eastTerrain));
    var eastMaterials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var eastHeights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    eastTerrain.CopyMaterials(eastMaterials);
    for (int index = 0; index < eastHeights.Length; index++)
        eastHeights[index] = eastTerrain.GetHeightOffset(index);
    Array.Fill(eastMaterials, (ushort)3);
    var eastBuilder = new WorldMutableOverlayBuilder(east);
    True(eastBuilder.TryReplaceTerrain(eastKey, eastMaterials, eastTerrain.HeightBase, eastHeights));
    True(eastBuilder.TryBuild(out WorldMutableOverlay? eastChanged));

    True(PreparedWorldComposition.TryCreate(source.Space, new WorldCompositionRevision(7),
        source.Manifest,
        [new PreparedWorldPack(W5TwoPackFixture.WestCoord, new WorldPreparedView(west, westRevisedEmpty)),
         new PreparedWorldPack(W5TwoPackFixture.EastCoord, new WorldPreparedView(east, eastChanged))],
        out PreparedWorldComposition? composed, out _));
    True(composed!.TryValidateComplete(out _));
    using var ecs = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(ecs, preparedComposition: composed);
    var capture = new WorldSaveSetParticipant(state, new PersistentWorldId(0x5755_0002));
    byte[] artifact = EncodeWorldView(capture, 17);
    Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(4)));
    Equal(2, BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(72)));
    True(artifact.Length < west.Pack.Length + east.Pack.Length);
    var restore = new WorldSaveSetParticipant(
        fingerprint => fingerprint == source.ManifestFingerprint ? source.Manifest : null,
        fingerprint => fingerprint == west.Fingerprint ? west :
            fingerprint == east.Fingerprint ? east : null);
    using (IPreparedLoadParticipant pending = restore.Prepare(artifact, new SaveSetContext(17, 0)))
    {
        pending.Resolve(EmptyPersistentResolver.Instance);
        pending.Validate();
        PreparedWorldComposition restored = ((PreparedWorldParticipant)pending).PreparedComposition!;
        Equal(composed.ManifestFingerprint, restored.ManifestFingerprint);
        Equal(composed.Revision, restored.Revision);
        True(restored.TryGetPreparedPack(W5TwoPackFixture.WestCoord, out PreparedWorldPack westRow));
        True(restored.TryGetPreparedPack(W5TwoPackFixture.EastCoord, out PreparedWorldPack eastRow));
        Equal(2UL, westRow.View.Overlay.Revision.Value);
        Equal(0, westRow.View.Overlay.ChangedChunkCount);
        Equal(1UL, eastRow.View.Overlay.Revision.Value);
        True(eastRow.View.Overlay.TryGetChunkRevision(eastKey, out _, out _));
    }
    var missingManifest = new WorldSaveSetParticipant(
        _ => null, _ => west);
    Throws<PersistenceCompatibilityException>(() =>
    {
        using IPreparedLoadParticipant pending = missingManifest.Prepare(artifact, new SaveSetContext(17, 0));
        pending.Resolve(EmptyPersistentResolver.Instance);
    });
    var missingEast = new WorldSaveSetParticipant(
        _ => source.Manifest, fingerprint => fingerprint == west.Fingerprint ? west : null);
    Throws<PersistenceCompatibilityException>(() =>
    {
        using IPreparedLoadParticipant pending = missingEast.Prepare(artifact, new SaveSetContext(17, 0));
        pending.Resolve(EmptyPersistentResolver.Instance);
    });
    byte[] duplicateRow = (byte[])artifact.Clone();
    int firstRowLength = BinaryPrimitives.ReadInt32LittleEndian(duplicateRow.AsSpan(84));
    BinaryPrimitives.WriteInt32LittleEndian(duplicateRow.AsSpan(88 + firstRowLength), 0);
    Throws<PersistenceValidationException>(() =>
        restore.Prepare(duplicateRow, new SaveSetContext(17, 0)));
    var subset = W4Composition(source.Space, source.Manifest, source.Revision,
        new PreparedWorldPack(W5TwoPackFixture.WestCoord, new WorldPreparedView(west)));
    using var partialEcs = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var partialState = new GameWorldState(partialEcs, preparedComposition: subset);
    Throws<PersistenceValidationException>(() =>
        new WorldSaveSetParticipant(partialState, new PersistentWorldId(0x5755_0002))
            .Capture(new SaveSetContext(17, 0)));
    Console.WriteLine($"W5 WORLDVIEW v2_bytes={artifact.Length} zero_row_revision=2 " +
        "duplicate/missing resolver/partial capture rejected");
}

static void PreparedCompositionWarmedQueriesAllocateZero()
{
    SpatialSpaceId space = WorldPackTestProfile.Space;
    var entries = new WorldStructuralManifestEntry[64];
    var prepared = new PreparedWorldPack[64];
    for (int index = 0; index < 64; index++)
    {
        var coord = new WorldPackCoord(index, -index);
        WorldPreparedView view = W4Shard(coord);
        entries[index] = new WorldStructuralManifestEntry(coord, view.BaseWorldFingerprint);
        prepared[index] = new PreparedWorldPack(coord, view);
    }
    PreparedWorldComposition composition = W4Composition(space, StructuralManifest(entries), new(1), prepared);
    var key = new SpatialChunkKey(space, 0, 0);
    var point = new WorldPosition(0, 0, 1_000, 1_000, 0);
    True(composition.TryGetPreparedPack(new WorldPackCoord(0, 0), out _));
    True(composition.TryGetBaseTerrain(key, out _));
    True(composition.TrySampleBaseTerrainHeight(point, out _));
    long before = GC.GetAllocatedBytesForCurrentThread();
    int checksum = 0;
    long lookupStarted = Stopwatch.GetTimestamp();
    for (int iteration = 0; iteration < 100_000; iteration++)
    {
        WorldPackCoord coord = new(iteration % 64, -(iteration % 64));
        if (!composition.TryGetPreparedPack(coord, out PreparedWorldPack pack))
            throw new Exception("Missing prepared Pack.");
        checksum += pack.Coord.X;
    }
    long lookupTicks = Stopwatch.GetTimestamp() - lookupStarted;
    long queryStarted = Stopwatch.GetTimestamp();
    for (int iteration = 0; iteration < 100_000; iteration++)
    {
        if (!composition.TryGetBaseTerrain(key, out WorldTerrainChunkView terrain))
            throw new Exception("Missing prepared chunk.");
        checksum += terrain.GetMaterial(iteration % TerrainChunkFormat.MaterialCellCount);
    }
    long queryTicks = Stopwatch.GetTimestamp() - queryStarted;
    for (int iteration = 0; iteration < 100_000; iteration++)
    {
        if (!composition.TrySampleBaseTerrainHeight(point, out int height))
            throw new Exception("Missing prepared height.");
        checksum += height;
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
    Console.WriteLine($"W4 LOOKUP64 operations_each=100000 lookup_ticks={lookupTicks} " +
        $"lookup_ns_each={lookupTicks * 1_000_000_000.0 / Stopwatch.Frequency / 100_000:F1} " +
        $"chunk_query_ticks={queryTicks} chunk_query_ns_each={queryTicks * 1_000_000_000.0 / Stopwatch.Frequency / 100_000:F1} " +
        $"allocated_bytes={allocated}");
}

static void FullDeclaredWorldPackAcceptance()
{
    const int oldLimitBytes = 256 * 1024 * 1024;
    var space = WorldPackTestProfile.Space;
    var content = WorldPackTestProfile.Content;
    var address = new WorldPackCoord(0, 0);
    var builder = new WorldPackBuilder(space, content, WorldPackLimits.Default, address);
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    Array.Fill(materials, (ushort)1);
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    var stopwatch = Stopwatch.StartNew();
    for (int z = -WorldPackAddressing.CenteringBiasChunks;
         z < WorldPackAddressing.CenteringBiasChunks; z++)
    for (int x = -WorldPackAddressing.CenteringBiasChunks;
         x < WorldPackAddressing.CenteringBiasChunks; x++)
    {
        if (!builder.TryAddBaseTerrain(new SpatialChunkKey(space, x, z), materials,
                heightBase, heights, out WorldPackBuildError error))
            throw new Exception($"Full Pack chunk ({x},{z}) rejected: {error}.");
    }
    Equal(WorldPackAddressing.MaximumChunksPerWorldPack, builder.Count);
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError));
    Equal(WorldPackBuildError.None, buildError);
    long buildMilliseconds = stopwatch.ElapsedMilliseconds;
    int length = artifact!.Length;
    True(length > oldLimitBytes);
    True(length <= WorldPackLimits.Default.MaximumPackBytes);
    var oldLimits = new WorldPackLimits(oldLimitBytes,
        WorldPackAddressing.MaximumChunksPerWorldPack, WorldPackLimits.Default.MaximumSections);
    False(artifact.TryOpen(oldLimits, out WorldPack? oldOpened, out WorldPackOpenError oldError));
    Equal(null, oldOpened);
    Equal(WorldPackOpenError.CapacityExceeded, oldError);
    stopwatch.Restart();
    True(artifact.TryOpen(WorldPackLimits.Default, out WorldPack? opened, out WorldPackOpenError openError));
    Equal(WorldPackOpenError.None, openError);
    Equal(WorldPackAddressing.MaximumChunksPerWorldPack, opened!.ChunkCount);
    True(opened.TryValidateSpatialWindow(address, out _, out WorldPackSpatialValidationError spatialError));
    Equal(WorldPackSpatialValidationError.None, spatialError);
    long openMilliseconds = stopwatch.ElapsedMilliseconds;
    True(opened.TryGetChunkKey(0, out SpatialChunkKey first));
    True(opened.TryGetChunkKey(opened.ChunkCount - 1, out SpatialChunkKey last));
    Equal(new SpatialChunkCoord(-128, -128), first.Coord);
    Equal(new SpatialChunkCoord(127, 127), last.Coord);
    Console.WriteLine($"W2 FULL PACK PASS chunks={opened.ChunkCount} bytes={length} " +
        $"mib={length / (1024.0 * 1024.0):F6} directory_bytes=" +
        $"{opened.ChunkCount * WorldPackFormat.ChunkDirectoryRowBytes} " +
        $"terrain_bytes={opened.ChunkCount * TerrainChunkFormat.FoundationBytes} " +
        $"old_256mib={oldError} default_512mib={openError} " +
        $"build_ms={buildMilliseconds} open_validate_ms={openMilliseconds}");
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
    Equal(16, TerrainChunkFormat.SurfacePresenceWordCount);
    Equal(128, TerrainChunkFormat.SurfacePresencePlaneBytes);
    Equal(4_230, TerrainChunkFormat.FoundationBytes);
    Equal(4_358, TerrainChunkFormat.FoundationBytesWithSurfacePresence);
    Equal(12, Marshal.SizeOf<BaseTerrainSurfaceSample>());
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
    Equal(1U, WorldPackFormat.TerrainPresenceSchemaVersion);
    Equal(1U, WorldPackFormat.AdditionalTerrainSurfacesSchemaVersion);
    Equal(16, Marshal.SizeOf<WorldContentFingerprint>());
    Equal(32, Marshal.SizeOf<WorldPackFingerprint>());
    var fingerprint = new WorldPackFingerprint(
        0xABCD_EF01_2345_6789, 2, 3, 4);
    True(WorldPackFingerprint.TryParse(fingerprint.ToString(), out WorldPackFingerprint parsed));
    Equal(fingerprint, parsed);
    False(WorldPackFingerprint.TryParse(fingerprint.ToString().ToLowerInvariant(), out _));
    False(WorldPackFingerprint.TryParse(fingerprint.ToString()[1..], out _));
    Equal(1U, (uint)WorldPackSectionKind.TerrainMaterials);
    Equal(2U, (uint)WorldPackSectionKind.TerrainHeights);
    Equal(3U, (uint)WorldPackSectionKind.FrozenStatics);
    Equal(4U, (uint)WorldPackSectionKind.MultiFragments);
    Equal(5U, (uint)WorldPackSectionKind.TerrainPresence);
    Equal(6U, (uint)WorldPackSectionKind.AdditionalTerrainSurfaces);
    Equal(8, Marshal.SizeOf<WorldSurfaceId>());
    Equal(4, Marshal.SizeOf<TerrainSurfacePatchBounds>());
    Equal(16, Marshal.SizeOf<WorldTerrainColumnSurface>());
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

static void BaseSurfacePresencePackingIsCanonicalAndLegacyCompatible()
{
    WorldPackArtifact legacy = CreatePresenceWorldPackArtifact(withVoid: false, explicitAllPresent: false);
    WorldPackArtifact explicitPresent = CreatePresenceWorldPackArtifact(withVoid: false, explicitAllPresent: true);
    Equal(legacy.Length, explicitPresent.Length);
    Equal(legacy.Fingerprint, explicitPresent.Fingerprint);
    var legacyBytes = new byte[legacy.Length];
    var explicitBytes = new byte[explicitPresent.Length];
    legacy.CopyTo(legacyBytes);
    explicitPresent.CopyTo(explicitBytes);
    True(legacyBytes.AsSpan().SequenceEqual(explicitBytes));
    Equal(2, legacy.Open().SectionCount);
    False(legacy.Open().HasExplicitTerrainPresence);

    WorldPackArtifact forward = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false);
    WorldPackArtifact reverse = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false, reverse: true);
    Equal(forward.Length, reverse.Length);
    Equal(forward.Fingerprint, reverse.Fingerprint);
    var first = new byte[forward.Length];
    var second = new byte[reverse.Length];
    forward.CopyTo(first);
    reverse.CopyTo(second);
    True(first.AsSpan().SequenceEqual(second));
    Equal(checked(legacy.Length + WorldPackFormat.SectionDirectoryRowBytes +
        2 * TerrainChunkFormat.SurfacePresencePlaneBytes), forward.Length);

    WorldPack pack = forward.Open();
    Equal(3, pack.SectionCount);
    True(pack.HasExplicitTerrainPresence);
    True(pack.TryGetBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        out WorldTerrainChunkView terrain));
    True(terrain.HasExplicitSurfacePresence);
    Equal(TerrainChunkFormat.MaterialCellCount - 2, terrain.PresentSurfaceCount);
    var copied = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
    terrain.CopySurfacePresence(copied);
    False(TerrainSurfacePresence.IsPresent(copied, new TerrainCellCoord(3, 4)));
    False(TerrainSurfacePresence.IsPresent(copied, new TerrainCellCoord(31, 15)));
    True(TerrainSurfacePresence.IsPresent(copied, new TerrainCellCoord(2, 4)));

    string path = Path.Combine(Path.GetTempPath(), $"ultrasim-worldpack-presence-{Guid.NewGuid():N}.uwp");
    try
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            forward.WriteTo(stream);
        True(WorldPack.TryOpenFile(path, new WorldPackLimits(512 * 1024, 2, 8),
            WorldPackTestProfile.Content, out WorldPack? reopened, out WorldPackOpenError openError));
        Equal(WorldPackOpenError.None, openError);
        Equal(forward.Fingerprint, reopened!.Fingerprint);
        True(reopened.TryGetBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
            out WorldTerrainChunkView reopenedTerrain));
        False(reopenedTerrain.IsSurfacePresent(new TerrainCellCoord(3, 4)));
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }

    var malformed = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(512 * 1024, 1, 8));
    CreatePackPlanes(0, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights);
    False(malformed.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, new ulong[TerrainChunkFormat.SurfacePresenceWordCount - 1],
        out WorldPackBuildError malformedError));
    Equal(WorldPackBuildError.MalformedTerrainPresence, malformedError);

    var sectionLimited = new WorldPackBuilder(WorldPackTestProfile.Space,
        WorldPackTestProfile.Content, new WorldPackLimits(512 * 1024, 1, 2));
    var oneHole = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
    TerrainSurfacePresence.FillPresent(oneHole);
    TerrainSurfacePresence.SetPresent(oneHole, new TerrainCellCoord(0, 0), false);
    True(sectionLimited.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, oneHole, out _));
    False(sectionLimited.TryBuild(out _, out WorldPackBuildError sectionError));
    Equal(WorldPackBuildError.CapacityExceeded, sectionError);

    byte[] redundant = (byte[])first.Clone();
    int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(redundant.AsSpan(64));
    int sectionDirectory = WorldPackFormat.HeaderBytes +
        chunkCount * WorldPackFormat.ChunkDirectoryRowBytes;
    int presenceRow = sectionDirectory + 2 * WorldPackFormat.SectionDirectoryRowBytes;
    int presenceOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
        redundant.AsSpan(presenceRow + 8)));
    redundant.AsSpan(presenceOffset,
        chunkCount * TerrainChunkFormat.SurfacePresencePlaneBytes).Fill(byte.MaxValue);
    RewriteSectionDigest(redundant, presenceRow);
    RewritePackFingerprint(redundant);
    AssertPackOpenFails(redundant, new WorldPackLimits(512 * 1024, 2, 8),
        WorldPackOpenError.InvalidTerrainPayload);
}

static void BaseSurfaceProbeDistinguishesVoidFromUnavailable()
{
    WorldPack pack = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false).Open();
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? world, out _));
    BoundWorldPack bound = world!;
    var present = new WorldPosition(0, 0, 1_000, 1_000, int.MaxValue);
    BaseTerrainSurfaceSample presentSample = bound.ProbeBaseTerrainSurface(present);
    Equal(BaseTerrainSurfaceStatus.Present, presentSample.Status);
    True(presentSample.IsPresent);
    True(presentSample.MaterialId != 0);

    var absent = new WorldPosition(0, 0, 1_750, 2_250, int.MinValue);
    BaseTerrainSurfaceSample absentSample = bound.ProbeBaseTerrainSurface(absent);
    Equal(BaseTerrainSurfaceStatus.Absent, absentSample.Status);
    False(absentSample.IsPresent);
    Equal(0, absentSample.HeightMillimeters);
    Equal((ushort)0, absentSample.MaterialId);
    False(bound.TrySampleBaseTerrainHeight(absent, out int absentHeight));
    Equal(0, absentHeight);
    False(bound.TryResolveBaseTerrainMaterial(absent, out ushort absentMaterial));
    Equal((ushort)0, absentMaterial);

    var unavailable = new WorldPosition(8, 8, 1_750, 2_250, 0);
    BaseTerrainSurfaceSample unavailableSample = bound.ProbeBaseTerrainSurface(unavailable);
    Equal(BaseTerrainSurfaceStatus.Unavailable, unavailableSample.Status);
    False(unavailableSample.IsPresent);
    False(default(BaseTerrainSurfaceSample).IsPresent);

    ulong checksum = 0;
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 100_000; index++)
    {
        BaseTerrainSurfaceSample sample = bound.ProbeBaseTerrainSurface(
            index % 2 == 0 ? present : absent);
        checksum += (uint)sample.Status + sample.MaterialId;
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
}

static void BaseSurfaceVoidMeshOmitsGeometryDeterministically()
{
    WorldPack pack = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false).Open();
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? world, out _));
    BoundWorldPack bound = world!;
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    var vertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    var materials = new ushort[TerrainChunkFormat.TriangleCount];
    Array.Fill(indices, ushort.MaxValue);
    Array.Fill(materials, ushort.MaxValue);
    True(bound.TryBuildBaseTerrainMesh(key, vertices, indices, materials,
        out TerrainBaseMeshSummary summary, out TerrainBaseMeshBuildError error));
    Equal(TerrainBaseMeshBuildError.None, error);
    Equal(TerrainChunkFormat.TriangleCount - 4, summary.TriangleCount);
    Equal(TerrainChunkFormat.TriangleIndexCount - 12, summary.IndexCount);
    True(indices.AsSpan(summary.IndexCount).ToArray().All(value => value == ushort.MaxValue));
    True(materials.AsSpan(summary.TriangleCount).ToArray().All(value => value == ushort.MaxValue));
    True(vertices.All(vertex => vertex.NormalY > 0));

    var secondVertices = new TerrainBaseMeshVertex[vertices.Length];
    var secondIndices = new ushort[summary.IndexCount];
    var secondMaterials = new ushort[summary.TriangleCount];
    True(bound.TryBuildBaseTerrainMesh(key, secondVertices, secondIndices, secondMaterials,
        out TerrainBaseMeshSummary secondSummary, out _));
    Equal(summary, secondSummary);
    True(vertices.AsSpan().SequenceEqual(secondVertices));
    True(indices.AsSpan(0, summary.IndexCount).SequenceEqual(secondIndices));
    True(materials.AsSpan(0, summary.TriangleCount).SequenceEqual(secondMaterials));
    False(bound.TryBuildBaseTerrainMesh(key, secondVertices, secondIndices.AsSpan(1), secondMaterials,
        out TerrainBaseMeshSummary rejected, out error));
    Equal(default, rejected);
    Equal(TerrainBaseMeshBuildError.DestinationTooSmall, error);

    WorldPack allVoidPack = CreateAllVoidWorldPackArtifact().Open();
    True(allVoidPack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? allVoid, out _));
    True(allVoid!.TryBuildBaseTerrainMesh(key, vertices, Span<ushort>.Empty, Span<ushort>.Empty,
        out TerrainBaseMeshSummary emptySummary, out _));
    Equal(0, emptySummary.TriangleCount);
    Equal(0, emptySummary.IndexCount);
    Equal(0, emptySummary.MinimumHeightMillimeters);
    Equal(0, emptySummary.MaximumHeightMillimeters);
    True(vertices.All(vertex => vertex.NormalX == 0 && vertex.NormalY == 1 && vertex.NormalZ == 0));
}

static void TerrainReplacementPreservesAuthoredBaseTopology()
{
    WorldPack pack = CreatePresenceWorldPackArtifact(withVoid: true, explicitAllPresent: false).Open();
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? world, out _));
    BoundWorldPack bound = world!;
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    CreatePackPlanes(0, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights);
    Array.Fill(materials, (ushort)5);
    var builder = new WorldMutableOverlayBuilder(bound);
    True(builder.TryReplaceTerrain(key, materials, heightBase, heights));
    True(builder.TryBuild(out WorldMutableOverlay? overlay));
    var prepared = new WorldPreparedView(bound, overlay);
    var absent = new WorldPosition(0, 0, 1_750, 2_250, 0);
    Equal(BaseTerrainSurfaceStatus.Absent, prepared.ProbeBaseTerrainSurface(absent).Status);
    var present = new WorldPosition(0, 0, 1_000, 1_000, 0);
    BaseTerrainSurfaceSample sample = prepared.ProbeBaseTerrainSurface(present);
    Equal(BaseTerrainSurfaceStatus.Present, sample.Status);
    Equal((ushort)5, sample.MaterialId);
}

static void AdditionalTerrainShellsCompileCanonicallyAndSpanChunks()
{
    WorldPackArtifact forward = CreateAdditionalTerrainWorldPack(reverse: false);
    WorldPackArtifact reverse = CreateAdditionalTerrainWorldPack(reverse: true);
    Equal(forward.Fingerprint, reverse.Fingerprint);
    Equal(forward.Length, reverse.Length);
    var forwardBytes = new byte[forward.Length];
    var reverseBytes = new byte[reverse.Length];
    forward.CopyTo(forwardBytes);
    reverse.CopyTo(reverseBytes);
    True(forwardBytes.AsSpan().SequenceEqual(reverseBytes));

    WorldPack pack = forward.Open();
    Equal(4, pack.AdditionalTerrainSurfaceFragmentCount);
    Equal(3, pack.SectionCount);
    var left = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    var right = new SpatialChunkKey(WorldPackTestProfile.Space, 1, 0);
    True(pack.TryGetAdditionalTerrainSurfaceBands(left, out WorldTerrainSurfaceBandChunkView leftBands));
    True(pack.TryGetAdditionalTerrainSurfaceBands(right, out WorldTerrainSurfaceBandChunkView rightBands));
    Equal(2, leftBands.Count);
    Equal(2, rightBands.Count);
    Equal(new WorldSurfaceId(100), leftBands.GetBand(0).Id);
    Equal(new WorldSurfaceId(100), rightBands.GetBand(0).Id);
    Equal(new WorldSurfaceId(200), leftBands.GetBand(1).Id);
    Equal(new WorldSurfaceId(200), rightBands.GetBand(1).Id);
    Equal(TerrainSurfaceEdgeConnection.East,
        leftBands.GetBand(0).GetEdgeConnections(1, 0));
    Equal(TerrainSurfaceEdgeConnection.West,
        rightBands.GetBand(0).GetEdgeConnections(0, 0));
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? bound, out WorldPackBindingError bindingError));
    Equal(WorldPackBindingError.None, bindingError);
    Equal(4, bound!.AdditionalTerrainSurfaceFragmentCount);
}

static void AdditionalTerrainColumnsUseGeometryNotLayerRank()
{
    BoundWorldPack world = OpenAdditionalTerrainWorld();
    var point = new WorldPosition(0, 0, 15_250, 250, 1_250_000);
    Span<WorldTerrainColumnSurface> surfaces = stackalloc WorldTerrainColumnSurface[5];
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryCopyTerrainColumnSurfaces(point, surfaces, out int count, out int required));
    Equal(5, count);
    Equal(5, required);
    Equal(new WorldSurfaceId(100), surfaces[0].SurfaceId);
    Equal(WorldTerrainSurfaceFace.Top, surfaces[0].Face);
    Equal(1_300_000, surfaces[0].HeightMillimeters);
    Equal(new WorldSurfaceId(100), surfaces[1].SurfaceId);
    Equal(WorldTerrainSurfaceFace.Bottom, surfaces[1].Face);
    Equal(1_290_000, surfaces[1].HeightMillimeters);
    Equal(new WorldSurfaceId(200), surfaces[2].SurfaceId);
    Equal(1_100_000, surfaces[2].HeightMillimeters);
    Equal(new WorldSurfaceId(200), surfaces[3].SurfaceId);
    Equal(1_090_000, surfaces[3].HeightMillimeters);
    True(surfaces[4].IsBase);
    Equal(0, surfaces[4].HeightMillimeters);

    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindHighestSupportAtOrBelow(point, 1_295_000, out WorldTerrainColumnSurface support));
    Equal(new WorldSurfaceId(200), support.SurfaceId);
    Equal(1_100_000, support.HeightMillimeters);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindNearestSupport(point, 1_200_000,
            out WorldTerrainColumnSurface nearestTie));
    Equal(new WorldSurfaceId(200), nearestTie.SurfaceId);
    Equal(1_100_000, nearestTie.HeightMillimeters);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindNearestSupport(point, 1_260_000,
            out WorldTerrainColumnSurface nearestHigh));
    Equal(new WorldSurfaceId(100), nearestHigh.SurfaceId);
    Equal(1_300_000, nearestHigh.HeightMillimeters);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindLowestCeilingAtOrAbove(point, 1_105_000, out WorldTerrainColumnSurface ceiling));
    Equal(new WorldSurfaceId(100), ceiling.SurfaceId);
    Equal(1_290_000, ceiling.HeightMillimeters);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindHighestSupportAtOrBelow(point, int.MinValue,
            out WorldTerrainColumnSurface noSupport));
    False(noSupport.IsValid);
    Equal(WorldTerrainColumnQueryStatus.Complete,
        world.TryFindLowestCeilingAtOrAbove(point, int.MaxValue,
            out WorldTerrainColumnSurface noCeiling));
    False(noCeiling.IsValid);
    False(default(WorldTerrainColumnSurface).IsValid);
    Equal(WorldTerrainColumnQueryStatus.DestinationTooSmall,
        world.TryCopyTerrainColumnSurfaces(point, surfaces[..4], out count, out required));
    Equal(0, count);
    Equal(5, required);
    Equal(WorldTerrainColumnQueryStatus.Unavailable,
        world.TryCopyTerrainColumnSurfaces(new WorldPosition(8, 8, 0, 0, 0), surfaces,
            out count, out required));

    _ = world.TryCopyTerrainColumnSurfaces(point, surfaces, out _, out _);
    long before = GC.GetAllocatedBytesForCurrentThread();
    ulong checksum = 0;
    for (int index = 0; index < 100_000; index++)
    {
        Equal(WorldTerrainColumnQueryStatus.Complete,
            world.TryCopyTerrainColumnSurfaces(point, surfaces, out count, out _));
        checksum += unchecked((uint)surfaces[index % count].HeightMillimeters);
    }
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    True(checksum != 0);
}

static void AdditionalTerrainQueriesAndMeshesShareBaseParity()
{
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(512 * 1024, 1, 8));
    ushort[] baseMaterials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
    var baseHeights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase zero));
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    True(builder.TryAddBaseTerrain(key, baseMaterials, zero, baseHeights, out _));
    TerrainSurfacePatchBounds bounds = new(0, 0, 1, 1);
    TerrainHeightOffset[] sloped =
    [
        new(0), new(0),
        new(0), new(200),
    ];
    True(builder.TryAddAdditionalTerrainSurfaceBand(
        key, new WorldSurfaceId(11), bounds, WorldTerrainSurfaceFaces.Top,
        [1UL], [TerrainSurfaceEdgeConnection.None], [2], zero, sloped,
        [], default, [], 0, out _));
    True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
    True(artifact!.Open().TryBind(
        new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? world, out _));
    BoundWorldPack bound = world!;

    var point = new WorldPosition(0, 0, 400, 100, 0);
    Span<WorldTerrainColumnSurface> column = stackalloc WorldTerrainColumnSurface[2];
    Equal(WorldTerrainColumnQueryStatus.Complete,
        bound.TryCopyTerrainColumnSurfaces(point, column, out int count, out _));
    Equal(2, count);
    Equal(new WorldSurfaceId(11), column[0].SurfaceId);
    Equal(200, column[0].HeightMillimeters);

    True(bound.TryGetAdditionalTerrainSurfaceBands(key, out WorldTerrainSurfaceBandChunkView bands));
    WorldTerrainSurfaceBandView band = bands.GetBand(0);
    var vertices = new TerrainBaseMeshVertex[TerrainSurfaceBandMesh.MaximumVertexCount];
    var indices = new ushort[TerrainSurfaceBandMesh.MaximumIndexCount];
    var materials = new ushort[TerrainSurfaceBandMesh.MaximumTriangleCount];
    True(TerrainSurfaceBandMesh.TryBuild(band, vertices, indices, materials,
        out TerrainSurfaceBandMeshSummary summary, out _));
    Equal(2, summary.TriangleCount);
    Equal(0, vertices[0].LocalXMillimeters);
    Equal(0, vertices[0].LocalZMillimeters);
    Equal(0, vertices[1].LocalXMillimeters);
    Equal(GameWorldSpatialProfile.TerrainCellEdgeUnits, vertices[1].LocalZMillimeters);
    Equal(GameWorldSpatialProfile.TerrainCellEdgeUnits, vertices[2].LocalXMillimeters);
    Equal(GameWorldSpatialProfile.TerrainCellEdgeUnits, vertices[2].LocalZMillimeters);
    Equal(1_000, vertices[2].AbsoluteYMillimeters);
}

static void AdditionalTerrainMeshIsBoundedClosedAndDeterministic()
{
    BoundWorldPack world = OpenAdditionalTerrainWorld();
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    True(world.TryGetAdditionalTerrainSurfaceBands(key, out WorldTerrainSurfaceBandChunkView bands));
    WorldTerrainSurfaceBandView band = bands.GetBand(0);
    var vertices = new TerrainBaseMeshVertex[TerrainSurfaceBandMesh.MaximumVertexCount];
    var indices = new ushort[TerrainSurfaceBandMesh.MaximumIndexCount];
    var materials = new ushort[TerrainSurfaceBandMesh.MaximumTriangleCount];
    True(TerrainSurfaceBandMesh.TryBuild(band, vertices, indices, materials,
        out TerrainSurfaceBandMeshSummary summary, out TerrainSurfaceBandMeshBuildError error));
    Equal(TerrainSurfaceBandMeshBuildError.None, error);
    Equal(4, summary.TopTriangleCount);
    Equal(4, summary.BottomTriangleCount);
    Equal(10, summary.BoundaryTriangleCount);
    Equal(18, summary.TriangleCount);
    Equal(54, summary.VertexCount);
    Equal(1_290_000, summary.MinimumHeightMillimeters);
    Equal(1_300_000, summary.MaximumHeightMillimeters);
    Equal(4, materials.AsSpan(0, summary.TriangleCount).ToArray().Count(value => value == 2));
    Equal(4, materials.AsSpan(0, summary.TriangleCount).ToArray().Count(value => value == 3));
    Equal(10, materials.AsSpan(0, summary.TriangleCount).ToArray().Count(value => value == 4));
    Equal(12, vertices.AsSpan(0, summary.VertexCount).ToArray().Count(value => value.NormalY > 0));
    Equal(12, vertices.AsSpan(0, summary.VertexCount).ToArray().Count(value => value.NormalY < 0));

    var secondVertices = new TerrainBaseMeshVertex[summary.VertexCount];
    var secondIndices = new ushort[summary.IndexCount];
    var secondMaterials = new ushort[summary.TriangleCount];
    True(TerrainSurfaceBandMesh.TryBuild(band, secondVertices, secondIndices, secondMaterials,
        out TerrainSurfaceBandMeshSummary second, out _));
    Equal(summary, second);
    True(vertices.AsSpan(0, summary.VertexCount).SequenceEqual(secondVertices));
    True(indices.AsSpan(0, summary.IndexCount).SequenceEqual(secondIndices));
    True(materials.AsSpan(0, summary.TriangleCount).SequenceEqual(secondMaterials));
    False(TerrainSurfaceBandMesh.TryBuild(band, secondVertices.AsSpan(1), secondIndices,
        secondMaterials, out _, out error));
    Equal(TerrainSurfaceBandMeshBuildError.DestinationTooSmall, error);
}

static void AdditionalTerrainValidationAndCompatibilityAreFailClosed()
{
    WorldPackArtifact legacy = CreateFlatTwoChunkWorld();
    var recreated = CreateFlatTwoChunkWorld();
    Equal(legacy.Fingerprint, recreated.Fingerprint);
    Equal(legacy.Length, recreated.Length);
    Equal(2, legacy.Open().SectionCount);
    Equal(0, legacy.Open().AdditionalTerrainSurfaceFragmentCount);

    var builder = CreateFlatTwoChunkBuilder();
    var key = new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0);
    TerrainSurfacePatchBounds bounds = new(0, 0, 2, 1);
    ulong[] presence = [0b11UL];
    ushort[] materials = [2, 2];
    TerrainHeightOffset[] heights = new TerrainHeightOffset[bounds.VertexCount];
    True(TerrainHeightPlaneBase.TryCreate(10_000, out TerrainHeightPlaneBase top));
    True(TerrainHeightPlaneBase.TryCreate(9_000, out TerrainHeightPlaneBase bottom));
    False(builder.TryAddAdditionalTerrainSurfaceBand(key, default, bounds,
        WorldTerrainSurfaceFaces.Top, presence, new TerrainSurfaceEdgeConnection[bounds.CellCount],
        materials, top, heights,
        [], default, [], 0, out WorldPackBuildError invalid));
    Equal(WorldPackBuildError.InvalidAdditionalTerrainSurface, invalid);
    True(builder.TryAddAdditionalTerrainSurfaceBand(key, new WorldSurfaceId(7), bounds,
        WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom, presence,
        new TerrainSurfaceEdgeConnection[bounds.CellCount],
        materials, top, heights, materials, bottom, heights, 4, out _));
    False(builder.TryAddAdditionalTerrainSurfaceBand(key, new WorldSurfaceId(7), bounds,
        WorldTerrainSurfaceFaces.Top, presence, new TerrainSurfaceEdgeConnection[bounds.CellCount],
        materials, top, heights,
        [], default, [], 0, out invalid));
    Equal(WorldPackBuildError.InvalidAdditionalTerrainSurface, invalid);

    var brokenSeam = CreateFlatTwoChunkBuilder();
    TerrainSurfacePatchBounds edgeBounds = new(30, 0, 2, 1);
    var edgeConnections = new TerrainSurfaceEdgeConnection[edgeBounds.CellCount];
    edgeConnections[1] = TerrainSurfaceEdgeConnection.East;
    True(brokenSeam.TryAddAdditionalTerrainSurfaceBand(
        key, new WorldSurfaceId(8), edgeBounds,
        WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom,
        [0b11UL], edgeConnections, materials, top, heights,
        materials, bottom, heights, 4, out _));
    False(brokenSeam.TryBuild(out _, out WorldPackBuildError brokenSeamError));
    Equal(WorldPackBuildError.InvalidAdditionalTerrainSurface, brokenSeamError);

    // Both fragments are individually valid, but the east fragment points at a neighboring
    // patch offset one cell from the seam. Checked builds must reject the derived -1 local
    // coordinate through the builder's error protocol rather than throwing.
    var offsetSeam = CreateFlatTwoChunkBuilder();
    TerrainSurfacePatchBounds eastBounds = new(31, 4, 1, 1);
    TerrainSurfacePatchBounds westOffsetBounds = new(1, 4, 1, 1);
    True(offsetSeam.TryAddAdditionalTerrainSurfaceBand(
        new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0), new WorldSurfaceId(9), eastBounds,
        WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom,
        [1UL], [TerrainSurfaceEdgeConnection.East], [(ushort)2], top,
        new TerrainHeightOffset[eastBounds.VertexCount], [(ushort)2], bottom,
        new TerrainHeightOffset[eastBounds.VertexCount], 4, out _));
    True(offsetSeam.TryAddAdditionalTerrainSurfaceBand(
        new SpatialChunkKey(WorldPackTestProfile.Space, 1, 0), new WorldSurfaceId(9), westOffsetBounds,
        WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom,
        [1UL], [TerrainSurfaceEdgeConnection.None], [(ushort)2], top,
        new TerrainHeightOffset[westOffsetBounds.VertexCount], [(ushort)2], bottom,
        new TerrainHeightOffset[westOffsetBounds.VertexCount], 4, out _));
    False(offsetSeam.TryBuild(out _, out WorldPackBuildError offsetSeamError));
    Equal(WorldPackBuildError.InvalidAdditionalTerrainSurface, offsetSeamError);
    False(offsetSeam.TryBuild(out _, out offsetSeamError));
    Equal(WorldPackBuildError.InvalidAdditionalTerrainSurface, offsetSeamError);

    WorldPackArtifact artifact = CreateAdditionalTerrainWorldPack(false);
    var bytes = new byte[artifact.Length];
    artifact.CopyTo(bytes);
    int sectionDirectory = WorldPackFormat.HeaderBytes +
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(64)) * WorldPackFormat.ChunkDirectoryRowBytes;
    int row = sectionDirectory + 2 * WorldPackFormat.SectionDirectoryRowBytes;
    int payload = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(row + 8)));
    int descriptors = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(payload + 20));
    bytes[payload + descriptors + 13] = 1;
    RewriteSectionDigest(bytes, row);
    RewritePackFingerprint(bytes);
    AssertPackOpenFails(bytes, new WorldPackLimits(2 * 1024 * 1024, 2, 8),
        WorldPackOpenError.InvalidAdditionalTerrainSurfacePayload);

    artifact.CopyTo(bytes);
    int firstConnections = BinaryPrimitives.ReadInt32LittleEndian(
        bytes.AsSpan(payload + descriptors + 36));
    bytes[payload + firstConnections + 1] = (byte)TerrainSurfaceEdgeConnection.None;
    RewriteSectionDigest(bytes, row);
    RewritePackFingerprint(bytes);
    AssertPackOpenFails(bytes, new WorldPackLimits(2 * 1024 * 1024, 2, 8),
        WorldPackOpenError.InvalidAdditionalTerrainSurfacePayload);
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

static void WorldPackStreamOpenIsExactBoundedAndSingleCopy()
{
    WorldPackArtifact artifact = CreateWorldPackArtifact(reverse: true);
    var bytes = new byte[artifact.Length];
    artifact.CopyTo(bytes);
    var limits = new WorldPackLimits(2 * 1024 * 1024, 64, 8);
    using (var source = new ChunkedReadStream(bytes, maximumRead: 37))
    {
        True(WorldPack.TryOpen(source, limits, WorldPackTestProfile.Content,
            out WorldPack? streamed, out WorldPackOpenError error));
        Equal(WorldPackOpenError.None, error);
        Equal(artifact.Length, streamed!.Length);
        Equal(artifact.Fingerprint, streamed.Fingerprint);
        var copied = new byte[streamed.Length];
        streamed.CopyTo(copied);
        True(bytes.AsSpan().SequenceEqual(copied));
    }

    using (var truncated = new ChunkedReadStream(bytes[..^1], maximumRead: 13))
    {
        False(WorldPack.TryOpen(truncated, limits, WorldPackTestProfile.Content,
            out WorldPack? pack, out WorldPackOpenError error));
        Equal(null, pack);
        Equal(WorldPackOpenError.Truncated, error);
    }

    var trailingBytes = new byte[bytes.Length + 1];
    bytes.CopyTo(trailingBytes, 0);
    using (var trailing = new ChunkedReadStream(trailingBytes, maximumRead: 19))
    {
        False(WorldPack.TryOpen(trailing, limits, WorldPackTestProfile.Content,
            out WorldPack? pack, out WorldPackOpenError error));
        Equal(null, pack);
        Equal(WorldPackOpenError.LengthMismatch, error);
    }

    var oversizedHeader = new byte[WorldPackFormat.HeaderBytes];
    BinaryPrimitives.WriteUInt64LittleEndian(oversizedHeader, WorldPackFormat.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(
        oversizedHeader.AsSpan(8), WorldPackFormat.ContainerVersion);
    BinaryPrimitives.WriteUInt64LittleEndian(oversizedHeader.AsSpan(16), 16 * 1024 * 1024);
    using var oversized = new ChunkedReadStream(oversizedHeader, maximumRead: 23);
    False(WorldPack.TryOpen(oversized, limits, WorldPackTestProfile.Content,
        out _, out WorldPackOpenError warmError));
    Equal(WorldPackOpenError.CapacityExceeded, warmError);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 10_000; index++)
    {
        oversized.Reset();
        False(WorldPack.TryOpen(oversized, limits, WorldPackTestProfile.Content,
            out _, out WorldPackOpenError error));
        Equal(WorldPackOpenError.CapacityExceeded, error);
    }
    Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
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
    True(WorldStaticPhysicalSemantics.BlocksBody(WorldStaticFlags.Collidable));
    True(WorldStaticPhysicalSemantics.SupportsTop(WorldStaticFlags.Collidable));
    WorldStaticFlags noTopSupport = WorldStaticFlags.Collidable | WorldStaticFlags.NoTopSupport;
    True(WorldStaticPhysicalSemantics.BlocksBody(noTopSupport));
    False(WorldStaticPhysicalSemantics.SupportsTop(noTopSupport));

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
    False(duplicate.TryAddFrozenStatic(source with
    {
        Id = new WorldStaticId(4),
        Flags = WorldStaticFlags.Renderable | WorldStaticFlags.NoTopSupport,
    }, out error));
    Equal(WorldPackBuildError.InvalidStaticPlacement, error);

    WorldPackBuilder noTopRoundTrip = CreateTerrainBuilder();
    True(noTopRoundTrip.TryAddFrozenStatic(source with { Flags = noTopSupport }, out _));
    True(noTopRoundTrip.TryBuild(out WorldPackArtifact? noTopArtifact, out _));
    WorldPack noTopPack = noTopArtifact!.Open();
    True(noTopPack.TryGetFrozenStatics(new SpatialChunkKey(noTopPack.Space, 0, 0),
        out WorldFrozenStaticChunkView noTopStatics));
    Equal(1, noTopStatics.Count);
    Equal(noTopSupport, noTopStatics.GetRecord(0).Flags);

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
    for (int index = 0; index < indices.Length; index += 3)
    {
        TerrainBaseMeshVertex first = vertices[indices[index]];
        TerrainBaseMeshVertex second = vertices[indices[index + 1]];
        TerrainBaseMeshVertex third = vertices[indices[index + 2]];
        long edgeOneX = (long)second.LocalXMillimeters - first.LocalXMillimeters;
        long edgeOneZ = (long)second.LocalZMillimeters - first.LocalZMillimeters;
        long edgeTwoX = (long)third.LocalXMillimeters - first.LocalXMillimeters;
        long edgeTwoZ = (long)third.LocalZMillimeters - first.LocalZMillimeters;
        long projectedCrossY = checked(edgeOneZ * edgeTwoX - edgeOneX * edgeTwoZ);
        True(projectedCrossY > 0);
    }
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

static void MutableWorldOverlayPublishesCompleteTerrainAndStaticReplacements()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    WorldContentBinding binding = CreateGameWorldBinding();
    True(pack.TryBind(binding, out BoundWorldPack? bound, out WorldPackBindingError bindError));
    Equal(WorldPackBindingError.None, bindError);
    using var world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(world, bound);
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
    var location = new WorldPosition(0, 0, 100, 100, 0);
    True(state.TryResolveBaseTerrainMaterial(location, out ushort immutableMaterial));

    ushort replacementMaterial = immutableMaterial == GameTerrainMaterialIds.Snow
        ? GameTerrainMaterialIds.Rock
        : GameTerrainMaterialIds.Snow;
    WorldMutableOverlay first = CreateReplacementOverlay(
        bound!, key, replacementMaterial, new WorldStaticId(9_001));
    Equal(1UL, first.Revision.Value);
    Equal(1, first.ChangedChunkCount);
    True(state.TryPublishOverlay(first));
    True(state.TryResolveBaseTerrainMaterial(location, out ushort currentMaterial));
    Equal(replacementMaterial, currentMaterial);

    var query = new WorldStaticBounds(900, -100, 900, 1_100, 100, 1_100);
    var hits = new WorldStaticQueryHit[8];
    True(state.TryQueryStaticBounds(query, 16, hits, out int count, out int required,
        out WorldStaticQueryError queryError));
    Equal(WorldStaticQueryError.None, queryError);
    Equal(1, count);
    Equal(1, required);
    Equal(new WorldStaticId(9_001), hits[0].StaticId);

    WorldMutableOverlay second = CreateReplacementOverlay(
        bound!, key, GameTerrainMaterialIds.Grass, new WorldStaticId(9_002), first);
    Equal(2UL, second.Revision.Value);
    True(second.TryGetChunkRevision(key, out WorldChunkContentRevision chunkRevision, out _));
    Equal(2UL, chunkRevision.Value);
    True(state.TryPublishOverlay(second));
    False(state.TryPublishOverlay(first));
    Equal(2UL, state.GetSnapshot().OverlayRevision.Value);

    True(bound!.TryGetBaseTerrain(key, out WorldTerrainChunkView baseTerrain));
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    baseTerrain.CopyMaterials(materials);
    for (int index = 0; index < offsets.Length; index++) offsets[index] = baseTerrain.GetHeightOffset(index);
    offsets[0] = new TerrainHeightOffset(checked((ushort)(offsets[0].Units + 1)));
    var brokenSeam = new WorldMutableOverlayBuilder(bound);
    True(brokenSeam.TryReplaceTerrain(key, materials, baseTerrain.HeightBase, offsets));
    False(brokenSeam.TryBuild(out WorldMutableOverlay? broken));
    Equal(null, broken);

    var oversizedBounds = new WorldStaticBounds(
        0, -1, 0,
        GameWorldSpatialProfile.WorldChunkEdgeUnits * 3L, 1,
        GameWorldSpatialProfile.WorldChunkEdgeUnits);
    var invalidStatic = new WorldFrozenStaticRecord(
        new WorldStaticId(99_999),
        new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Tree),
        new WorldPosition(0, 0, 1, 1, 0), WorldYaw.Zero, 0,
        WorldStaticFlags.Renderable, oversizedBounds);
    var invalidPlacement = new WorldMutableOverlayBuilder(bound);
    False(invalidPlacement.TryReplaceFrozenStatics(key, [invalidStatic]));
}

static void EmptyAndIdenticalOverlayTransactionsRemainCanonicalAndRestorable()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    True(pack.TryBind(CreateGameWorldBinding(), out BoundWorldPack? maybeBound, out _));
    BoundWorldPack bound = maybeBound!;
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);

    var emptyBuilder = new WorldMutableOverlayBuilder(bound);
    True(emptyBuilder.TryBuild(out WorldMutableOverlay? empty));
    Equal(default(WorldOverlayRevision), empty!.Revision);
    Equal(0, empty.ChangedChunkCount);
    False(empty.ReplacesMultiInstances);

    using var world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(world, bound);
    False(state.TryPublishOverlay(empty));

    WorldMutableOverlay first = CreateReplacementOverlay(
        bound, key, GameTerrainMaterialIds.Snow, new WorldStaticId(9_201));
    True(state.TryPublishOverlay(first));
    True(first.TryGetChunkRevision(key, out WorldChunkContentRevision firstChunkRevision,
        out WorldChunkContentHash firstHash));

    WorldMutableOverlay identical = CreateReplacementOverlay(
        bound, key, GameTerrainMaterialIds.Snow, new WorldStaticId(9_201), first);
    Equal(first.Revision, identical.Revision);
    True(identical.TryGetChunkRevision(key, out WorldChunkContentRevision identicalChunkRevision,
        out WorldChunkContentHash identicalHash));
    Equal(firstChunkRevision, identicalChunkRevision);
    Equal(firstHash, identicalHash);
    False(state.TryPublishOverlay(identical));

    Throws<ArgumentException>(() => _ = new WorldMutableOverlayBuilder(
        bound,
        new WorldMutableOverlayLimits(4_096, 0, 2_048, 262_144, 256L * 1024 * 1024),
        first));

    True(bound.TryGetBaseTerrain(key, out WorldTerrainChunkView terrain));
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    terrain.CopyMaterials(materials);
    for (int index = 0; index < offsets.Length; index++) offsets[index] = terrain.GetHeightOffset(index);
    True(bound.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView frozen));
    var baseStatics = new WorldFrozenStaticRecord[frozen.Count];
    for (int index = 0; index < baseStatics.Length; index++) baseStatics[index] = frozen.GetRecord(index);

    var resetBuilder = new WorldMutableOverlayBuilder(bound, prior: first);
    True(resetBuilder.TryReplaceTerrain(key, materials, terrain.HeightBase, offsets));
    True(resetBuilder.TryReplaceFrozenStatics(key, baseStatics));
    True(resetBuilder.TryBuild(out WorldMutableOverlay? reset));
    Equal(2UL, reset!.Revision.Value);
    Equal(0, reset.ChangedChunkCount);
    True(state.TryPublishOverlay(reset));

    var participant = new WorldSaveSetParticipant(
        state, new PersistentWorldId(0x70_0901), new WorldPersistenceOptions(1_000_000));
    byte[] artifact = EncodeWorldView(participant, 9);
    var restore = new WorldSaveSetParticipant(
        fingerprint => fingerprint == bound.Fingerprint ? bound : null,
        new WorldPersistenceOptions(1_000_000));
    using IPreparedLoadParticipant prepared = restore.Prepare(artifact, new SaveSetContext(9, 0));
    prepared.Resolve(EmptyPersistentResolver.Instance);
    prepared.Validate();
    var restored = (PreparedWorldParticipant)prepared;
    Equal(2UL, restored.Overlay!.Revision.Value);
    Equal(0, restored.Overlay.ChangedChunkCount);
}

static void MultiOverlayRootsFragmentsAndPersistenceRoundTrip()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    True(pack.TryBind(CreateGameWorldBinding(), out BoundWorldPack? maybeBound, out _));
    BoundWorldPack bound = maybeBound!;
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
    True(bound.TryGetMultiFragments(key, out WorldMultiFragmentChunkView baseFragments));
    var fragments = new WorldMultiFragmentRecord[baseFragments.Count + 1];
    for (int index = 0; index < baseFragments.Count; index++)
    {
        WorldMultiFragmentRecord record = baseFragments.GetRecord(index);
        if (record.MultiId == new MultiInstanceId(700) && record.PieceId == new MultiPieceId(3))
        {
            var moved = new WorldPosition(0, 0, 750, 900, 2_000);
            record = record with { Position = moved, Bounds = BoundsAround(moved, 400) };
        }
        fragments[index] = record;
    }
    var addedPosition = new WorldPosition(0, 0, 3_000, 3_000, 500);
    var added = new WorldMultiFragmentRecord(
        new MultiInstanceId(800), new MultiPieceId(1),
        new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Book),
        addedPosition, WorldYaw.QuarterTurn, 2,
        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
        BoundsAround(addedPosition, 150));
    fragments[^1] = added;

    var roots = new WorldMultiInstanceRecord[bound.MultiInstanceCount + 1];
    for (int index = 0; index < bound.MultiInstanceCount; index++)
        True(bound.TryGetMultiInstanceAt(index, out roots[index]));
    roots[^1] = new WorldMultiInstanceRecord(
        new MultiInstanceId(800), addedPosition, WorldYaw.QuarterTurn, 1);

    var builder = new WorldMutableOverlayBuilder(bound);
    True(builder.TryReplaceMultiFragments(key, fragments));
    True(builder.TryReplaceMultiInstances(roots));
    True(builder.TryBuild(out WorldMutableOverlay? maybeOverlay));
    WorldMutableOverlay overlay = maybeOverlay!;
    Equal(1UL, overlay.Revision.Value);
    True(overlay.ReplacesMultiInstances);
    var preparedView = new WorldPreparedView(bound, overlay);
    True(preparedView.TryGetMultiFragments(key, out WorldMultiFragmentChunkView effective));
    Equal(fragments.Length, effective.Count);
    True(preparedView.TryGetMultiInstance(new MultiInstanceId(800), out WorldMultiInstanceRecord addedRoot));
    Equal(1, addedRoot.PieceCount);

    var duplicateBuilder = new WorldMutableOverlayBuilder(bound);
    False(duplicateBuilder.TryReplaceMultiFragments(key, [added, added]));

    WorldMultiInstanceRecord[] wrongRoots = (WorldMultiInstanceRecord[])roots.Clone();
    wrongRoots[^1] = wrongRoots[^1] with { PieceCount = 2 };
    var wrongCountBuilder = new WorldMutableOverlayBuilder(bound);
    True(wrongCountBuilder.TryReplaceMultiFragments(key, fragments));
    True(wrongCountBuilder.TryReplaceMultiInstances(wrongRoots));
    False(wrongCountBuilder.TryBuild(out _));

    using var world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(world, bound);
    True(state.TryPublishOverlay(overlay));
    var participant = new WorldSaveSetParticipant(
        state, new PersistentWorldId(0x70_0902), new WorldPersistenceOptions(1_000_000));
    byte[] artifact = EncodeWorldView(participant, 10);
    var restore = new WorldSaveSetParticipant(
        fingerprint => fingerprint == bound.Fingerprint ? bound : null,
        new WorldPersistenceOptions(1_000_000));
    using IPreparedLoadParticipant restoredParticipant = restore.Prepare(artifact, new SaveSetContext(10, 0));
    restoredParticipant.Resolve(EmptyPersistentResolver.Instance);
    restoredParticipant.Validate();
    var restored = (PreparedWorldParticipant)restoredParticipant;
    var restoredView = new WorldPreparedView(bound, restored.Overlay);
    True(restoredView.TryGetMultiFragments(key, out WorldMultiFragmentChunkView restoredFragments));
    Equal(fragments.Length, restoredFragments.Count);
    True(restoredView.TryGetMultiInstance(new MultiInstanceId(800), out WorldMultiInstanceRecord restoredRoot));
    Equal(addedRoot, restoredRoot);
}

static void WorldViewArtifactIsCanonicalBoundedAndFailClosed()
{
    WorldPack pack = CreateStructuredWorldPackArtifact(
        reverse: false, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    True(pack.TryBind(CreateGameWorldBinding(), out BoundWorldPack? bound, out _));
    BoundWorldPack resolved = bound!;
    using var world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(64));
    using var state = new GameWorldState(world, resolved);
    var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
    True(state.TryPublishOverlay(CreateReplacementOverlay(
        resolved, key, GameTerrainMaterialIds.Snow, new WorldStaticId(9_050))));
    var participant = new WorldSaveSetParticipant(
        state, new PersistentWorldId(0x7007), new WorldPersistenceOptions(1_000_000));
    byte[] first = EncodeWorldView(participant, sequence: 7);
    byte[] second = EncodeWorldView(participant, sequence: 7);
    True(first.AsSpan().SequenceEqual(second));
    True(first.Length < pack.Length);

    var restore = new WorldSaveSetParticipant(
        fingerprint => fingerprint == resolved.Fingerprint ? resolved : null,
        new WorldPersistenceOptions(1_000_000));
    using (IPreparedLoadParticipant prepared = restore.Prepare(first, new SaveSetContext(7, 0)))
    {
        prepared.Resolve(EmptyPersistentResolver.Instance);
        prepared.Validate();
        var preparedWorld = (PreparedWorldParticipant)prepared;
        Equal(resolved.Fingerprint, preparedWorld.BaseWorld!.Fingerprint);
        Equal(1UL, preparedWorld.Overlay!.Revision.Value);
    }

    byte[] corrupt = (byte[])first.Clone();
    corrupt[^1] ^= 0x40;
    Throws<PersistenceException>(() =>
    {
        using IPreparedLoadParticipant prepared = restore.Prepare(corrupt, new SaveSetContext(7, 0));
        prepared.Resolve(EmptyPersistentResolver.Instance);
        prepared.Validate();
    });
    Throws<PersistenceValidationException>(() =>
        restore.Prepare(first.AsMemory(0, first.Length - 1), new SaveSetContext(7, 0)));
}

static byte[] EncodeWorldView(WorldSaveSetParticipant participant, ulong sequence)
{
    using ICapturedSaveSetParticipant captured = participant.Capture(new SaveSetContext(sequence, 0));
    using var stream = new MemoryStream();
    captured.WriteArtifact(stream);
    return stream.ToArray();
}

static void PreparedWorldSaveRestoresExactPackAndOverlayAtomically()
{
    string root = Path.Combine(Path.GetTempPath(), $"ultrasim-t09-{Guid.NewGuid():N}");
    WorldPack gamePack = CreateStructuredWorldPackArtifact(
        reverse: true, GameContent.WorldFingerprint, GameWorldState.DefaultSpace).Open();
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x90_0001));
    var composition = new UltraSimGameComposition(baseWorldPack: gamePack);
    using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(
        UltraSimGameComposition.CreateWorldOptions(64, identity), WorldClockOptions.Default));
    try
    {
        runtime.Initialize();
        runtime.Start();
        var key = new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0);
        True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((_, _, game) =>
        {
            BoundWorldPack bound = game.WorldState.BaseWorld!;
            WorldMutableOverlay overlay = CreateReplacementOverlay(
                bound, key, GameTerrainMaterialIds.Snow, new WorldStaticId(9_101));
            True(game.WorldState.TryPublishOverlay(overlay));
            return true;
        }, out bool installed));
        True(installed);

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            TerrainRestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult saved = persistence.TrySave(new SaveSetContext(1, 1_800_000_000_000),
            static capture => capture());
        if (!saved.Succeeded)
            throw new InvalidOperationException("T0.9 current-world save failed.", saved.Failure);
        Equal(4, saved.Manifest!.Artifacts.Length);
        True(saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "worldview"));

        True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((_, _, game) =>
        {
            WorldMutableOverlay divergent = CreateReplacementOverlay(
                game.WorldState.BaseWorld!, key, GameTerrainMaterialIds.Rock,
                new WorldStaticId(9_102), game.WorldState.Overlay);
            True(game.WorldState.TryPublishOverlay(divergent));
            return true;
        }, out bool diverged));
        True(diverged);

        GameLoadResult loaded = persistence.TryLoad();
        if (!loaded.Succeeded)
            throw new InvalidOperationException("T0.9 prepared-world load failed.", loaded.Failure);
        True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>((world, _, game) =>
        {
            Equal(identity.WorldId, world.PersistentWorldId);
            Equal(gamePack.Fingerprint, game.WorldState.BaseWorld!.Fingerprint);
            Equal(1UL, game.WorldState.Overlay!.Revision.Value);
            var location = new WorldPosition(0, 0, 100, 100, 0);
            True(game.WorldState.TryResolveBaseTerrainMaterial(location, out ushort material));
            Equal(GameTerrainMaterialIds.Snow, material);
            var bounds = new WorldStaticBounds(900, -100, 900, 1_100, 100, 1_100);
            var hits = new WorldStaticQueryHit[4];
            True(game.WorldState.TryQueryStaticBounds(bounds, 16, hits,
                out int count, out int required, out WorldStaticQueryError error));
            Equal(WorldStaticQueryError.None, error);
            Equal(1, count);
            Equal(1, required);
            Equal(new WorldStaticId(9_101), hits[0].StaticId);
            return true;
        }, out bool restored));
        True(restored);

        var missingComposition = new UltraSimGameComposition();
        using var missingRuntime = new ServerRuntime(missingComposition, new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(64, identity), WorldClockOptions.Default));
        missingRuntime.Initialize();
        missingRuntime.Start();
        ulong missingGeneration = missingRuntime.ActiveGenerationId;
        var missingPersistence = new GameRuntimePersistence(
            missingRuntime.Game,
            missingComposition,
            TerrainRestoreOptions(),
            new SaveSetCoordinator(root));
        GameLoadResult missing = missingPersistence.TryLoad();
        False(missing.Succeeded);
        True(missing.Failure is PersistenceCompatibilityException or IOException);
        Equal(missingGeneration, missingRuntime.ActiveGenerationId);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static WorldMutableOverlay CreateReplacementOverlay(
    BoundWorldPack baseWorld,
    SpatialChunkKey key,
    ushort material,
    WorldStaticId staticId,
    WorldMutableOverlay? prior = null)
{
    True(baseWorld.TryGetBaseTerrain(key, out WorldTerrainChunkView terrain));
    var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
    var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    terrain.CopyMaterials(materials);
    for (int index = 0; index < offsets.Length; index++) offsets[index] = terrain.GetHeightOffset(index);
    materials[0] = material;
    var position = new WorldPosition(key.Coord.X, key.Coord.Z, 1_000, 1_000, 0);
    WorldFrozenStaticRecord[] statics =
    [
        new WorldFrozenStaticRecord(
            staticId,
            new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Tree),
            position,
            WorldYaw.Zero,
            0,
            WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
            BoundsAround(position, 200)),
    ];
    var builder = new WorldMutableOverlayBuilder(baseWorld, prior: prior);
    True(builder.TryReplaceTerrain(key, materials, terrain.HeightBase, offsets));
    True(builder.TryReplaceFrozenStatics(key, statics));
    True(builder.TryBuild(out WorldMutableOverlay? overlay));
    True(overlay is not null);
    return overlay!;
}

static WorldContentBinding CreateGameWorldBinding() => new(
    GameContent.WorldFingerprint,
    [
        GameTerrainMaterialIds.Water,
        GameTerrainMaterialIds.Sand,
        GameTerrainMaterialIds.Grass,
        GameTerrainMaterialIds.Rock,
        GameTerrainMaterialIds.Snow,
    ],
    [
        GameWorldStaticDefinitionIds.Tree,
        GameWorldStaticDefinitionIds.Rock,
        GameWorldStaticDefinitionIds.CabinWall,
        GameWorldStaticDefinitionIds.CabinRoof,
        GameWorldStaticDefinitionIds.Shelf,
        GameWorldStaticDefinitionIds.Book,
    ]);

static PersistenceRestoreOptions TerrainRestoreOptions() => new(
    new PersistenceLimits(2_000_000, 256, 64, 64, 256, 2_000_000, 2_000_000),
    ChunkLayoutOptions.KiB16);

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

static WorldPackArtifact CreateAdditionalTerrainWorldPack(bool reverse)
{
    WorldPackBuilder builder = CreateFlatTwoChunkBuilder();
    var fragments = new List<(SpatialChunkKey Key, WorldSurfaceId Id, int Top, int Bottom)>
    {
        (new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0), new WorldSurfaceId(100), 1_300_000, 1_290_000),
        (new SpatialChunkKey(WorldPackTestProfile.Space, 1, 0), new WorldSurfaceId(100), 1_300_000, 1_290_000),
        (new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0), new WorldSurfaceId(200), 1_100_000, 1_090_000),
        (new SpatialChunkKey(WorldPackTestProfile.Space, 1, 0), new WorldSurfaceId(200), 1_100_000, 1_090_000),
    };
    if (reverse) fragments.Reverse();
    foreach ((SpatialChunkKey key, WorldSurfaceId id, int top, int bottom) in fragments)
        AddFlatShellFragment(builder, key, id, top, bottom);
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error));
    Equal(WorldPackBuildError.None, error);
    return artifact!;
}

static BoundWorldPack OpenAdditionalTerrainWorld()
{
    WorldPack pack = CreateAdditionalTerrainWorldPack(false).Open();
    True(pack.TryBind(new WorldContentBinding(WorldPackTestProfile.Content, [1, 2, 3, 4, 5]),
        out BoundWorldPack? world, out WorldPackBindingError error));
    Equal(WorldPackBindingError.None, error);
    return world!;
}

static WorldPackArtifact CreateFlatTwoChunkWorld()
{
    WorldPackBuilder builder = CreateFlatTwoChunkBuilder();
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error));
    Equal(WorldPackBuildError.None, error);
    return artifact!;
}

static WorldPackBuilder CreateFlatTwoChunkBuilder()
{
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(2 * 1024 * 1024, 2, 8));
    ushort[] materials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
    True(builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, out _));
    True(builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 1, 0),
        materials, heightBase, heights, out _));
    return builder;
}

static void AddFlatShellFragment(
    WorldPackBuilder builder,
    SpatialChunkKey key,
    WorldSurfaceId id,
    int topMillimeters,
    int bottomMillimeters)
{
    bool left = key.Coord.X == 0;
    TerrainSurfacePatchBounds bounds = new(left ? (byte)30 : (byte)0, 0, 2, 1);
    ulong[] presence = [0b11UL];
    var connections = new TerrainSurfaceEdgeConnection[bounds.CellCount];
    connections[left ? 1 : 0] = left
        ? TerrainSurfaceEdgeConnection.East
        : TerrainSurfaceEdgeConnection.West;
    ushort[] topMaterials = [2, 2];
    ushort[] bottomMaterials = [3, 3];
    var topHeights = new TerrainHeightOffset[bounds.VertexCount];
    var bottomHeights = new TerrainHeightOffset[bounds.VertexCount];
    True(TerrainHeightPlaneBase.TryCreate(topMillimeters, out TerrainHeightPlaneBase topBase));
    True(TerrainHeightPlaneBase.TryCreate(bottomMillimeters, out TerrainHeightPlaneBase bottomBase));
    True(builder.TryAddAdditionalTerrainSurfaceBand(
        key, id, bounds, WorldTerrainSurfaceFaces.Top | WorldTerrainSurfaceFaces.Bottom,
        presence, connections, topMaterials, topBase, topHeights,
        bottomMaterials, bottomBase, bottomHeights,
        4, out WorldPackBuildError error));
    Equal(WorldPackBuildError.None, error);
}

static WorldPackArtifact CreatePresenceWorldPackArtifact(
    bool withVoid,
    bool explicitAllPresent,
    bool reverse = false)
{
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(512 * 1024, 2, 8));
    int[] chunkXs = reverse ? [1, 0] : [0, 1];
    foreach (int chunkX in chunkXs)
    {
        CreatePackPlanes(chunkX, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
            out TerrainHeightOffset[] heights);
        var presence = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
        TerrainSurfacePresence.FillPresent(presence);
        if (withVoid && chunkX == 0)
        {
            TerrainSurfacePresence.SetPresent(presence, new TerrainCellCoord(3, 4), false);
            TerrainSurfacePresence.SetPresent(presence, new TerrainCellCoord(31, 15), false);
        }
        bool added = explicitAllPresent || withVoid
            ? builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, chunkX, 0),
                materials, heightBase, heights, presence, out WorldPackBuildError error)
            : builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, chunkX, 0),
                materials, heightBase, heights, out error);
        True(added);
        Equal(WorldPackBuildError.None, error);
    }
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError));
    Equal(WorldPackBuildError.None, buildError);
    return artifact!;
}

static WorldPackArtifact CreateAllVoidWorldPackArtifact()
{
    var builder = new WorldPackBuilder(WorldPackTestProfile.Space, WorldPackTestProfile.Content,
        new WorldPackLimits(512 * 1024, 1, 8));
    CreatePackPlanes(0, 0, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights);
    var presence = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
    True(builder.TryAddBaseTerrain(new SpatialChunkKey(WorldPackTestProfile.Space, 0, 0),
        materials, heightBase, heights, presence, out _));
    True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
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
    Equal(15, schema.ComponentCount);
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

static void SpatialIndexTurnoverDoesNotRetainHistoricalProbeChains()
{
    using RawSpatialFixture fixture = new(lifecycleCapacity: 32, entityCapacity: 64);
    True(fixture.World.TryCreate(fixture.SpatialArchetype, out Entity entity));
    for (int chunk = 0; chunk < 2_048; chunk++)
    {
        True(fixture.State.TrySetPosition(
            fixture.World, entity, new WorldPosition(chunk, -chunk, 0, 0, 0)));
        fixture.State.CommitAfterEcsStep(fixture.World);
    }
    True(fixture.World.TryDestroy(entity));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(0, fixture.State.Index.GetSnapshot().ChunkCount);

    WorldSpatialIndexSnapshot before = fixture.State.Index.GetSnapshot();
    Equal(0, fixture.State.Index.GetMemberCount(
        new SpatialChunkKey(GameWorldState.DefaultSpace, int.MinValue, int.MaxValue)));
    WorldSpatialIndexSnapshot after = fixture.State.Index.GetSnapshot();
    Equal(1L, after.MapLookups - before.MapLookups);
    Equal(1L, after.MapProbes - before.MapProbes);
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
            staging.Items,
            staging.Content,
            staging.WorldState,
            staging.SimulationLookup,
            staging.Replication,
            staging.WorldActions);
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

    True(fixture.State.TrySetPublic(fixture.World, entity,
        new WorldPublicDescriptor(77, new WorldYaw(1_009), WorldPublicStateFlags.None)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    var hidden = new WorldPublicDeltaRecord[1];
    Equal(ChunkPublicationReadStatus.Delta,
        fixture.State.Publications.TryCopyDelta(key, batch.Cursor, hidden, out _));
    Equal(WorldPublicOperation.Remove, hidden[0].Operation);
    Equal(publicId, hidden[0].Id);
    Equal(0, fixture.State.Publications.GetSnapshot().PublicEntityCount);

    True(fixture.State.TrySetPublic(fixture.World, entity,
        new WorldPublicDescriptor(77, new WorldYaw(1_009), WorldPublicStateFlags.Visible)));
    fixture.State.CommitAfterEcsStep(fixture.World);
    Equal(ChunkPublicationReadStatus.Snapshot,
        fixture.State.Publications.TryCopySnapshot(key, records, out batch));
    Equal(1, batch.Count);
    True(records[0].Id.Index == publicId.Index && records[0].Id.Generation > publicId.Generation);
    publicId = records[0].Id;

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

static void CheckedTryGuardsRejectSignedOutOfRangeInputs()
{
    SpatialChunkGeometry geometry = GameWorldSpatialProfile.ChunkGeometry;
    foreach (int local in new[] { int.MinValue, -1, geometry.EdgeUnits, int.MaxValue })
    {
        False(geometry.TryReconstructAxis(0, local, out long rejected));
        Equal(0L, rejected);
    }
    True(geometry.TryReconstructAxis(int.MinValue, 0, out long minimum));
    Equal((long)int.MinValue * geometry.EdgeUnits, minimum);
    True(geometry.TryReconstructAxis(int.MaxValue, geometry.EdgeUnits - 1, out long maximum));
    Equal((long)int.MaxValue * geometry.EdgeUnits + geometry.EdgeUnits - 1L, maximum);

    WorldPack pack = CreateStructuredWorldPackArtifact(reverse: false).Open();
    foreach (int index in new[] { int.MinValue, -1, pack.ChunkCount, int.MaxValue })
    {
        False(pack.TryGetChunkKey(index, out SpatialChunkKey key));
        Equal(default, key);
    }
    True(pack.TryGetChunkKey(0, out _));
    True(pack.TryGetChunkKey(pack.ChunkCount - 1, out _));

    foreach (int index in new[] { int.MinValue, -1, pack.MultiInstanceCount, int.MaxValue })
    {
        False(pack.TryGetMultiInstanceAt(index, out WorldMultiInstanceRecord instance));
        Equal(default, instance);
    }
    True(pack.TryGetMultiInstanceAt(0, out _));
    True(pack.TryGetMultiInstanceAt(pack.MultiInstanceCount - 1, out _));
}

static void SpatialMapCapacityRejectsUnrepresentableConfiguration()
{
    Equal(2, SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(1));
    Equal(4, SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(2));
    Equal(8, SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(3));
    Equal(SpatialStorageCapacity.MaximumPowerOfTwoCapacity,
        SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(1 << 29));
    Throws<ArgumentOutOfRangeException>(() =>
        SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(0));
    Throws<ArgumentOutOfRangeException>(() =>
        SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo((1 << 29) + 1));
    Throws<ArgumentOutOfRangeException>(() =>
        SpatialStorageCapacity.DoubleAndRoundUpPowerOfTwo(int.MaxValue));

    const int unsupported = (1 << 29) + 1;
    Throws<ArgumentOutOfRangeException>(() => _ = new WorldSpatialIndex(1, unsupported));
    Throws<ArgumentOutOfRangeException>(() => _ = new ChunkPublicationStore(
        new PublicationEpoch(1), new ChunkPublicationLimits(1, unsupported, 1, 1)));
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
        if (!actor.Alive || !actor.Public ||
            (actor.Descriptor.Flags & WorldPublicStateFlags.Visible) == 0)
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
        if (!actor.Alive || !actor.Public ||
            (actor.Descriptor.Flags & WorldPublicStateFlags.Visible) == 0) continue;
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

static void ChunkInterestClipsAtWorldAddressExtremes()
{
    SpatialSpaceId space = GameWorldState.DefaultSpace;
    var destination = new ChunkInterestEntry[1_024];
    WorldPosition[] anchors =
    [
        new(int.MinValue, int.MinValue, 0, 0, 0),
        new(int.MaxValue, int.MaxValue,
            checked((ushort)(GameWorldSpatialProfile.WorldChunkEdgeUnits - 1)),
            checked((ushort)(GameWorldSpatialProfile.WorldChunkEdgeUnits - 1)), 0),
    ];
    foreach (WorldPosition anchor in anchors)
    {
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(space, anchor, GameChunkInterestProfile.Default, [], destination,
                out int count, out int required));
        Equal(count, required);
        True(count > 0);
        AssertCanonicalInterest(destination.AsSpan(0, count));
        SpatialChunkKey home = anchor.ToChunkKey(space);
        True(FindInterest(destination.AsSpan(0, count), home, out _) >= 0);
        for (int index = 0; index < count; index++)
            Equal(space, destination[index].Key.Space);
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

static void LocalChunkSemanticSlotsFenceReuse()
{
    var source = new ChunkPublicationStore(PublicationEpoch.Create(), new ChunkPublicationLimits(1, 2, 1, 1));
    var oracle = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(1, 1),
        new ChunkInterestProfile(0, 0, 0, 0, 0, 0),
        new LocalChunkStreamingCadence(1, 1, 1));

    WorldPosition firstAnchor = new(0, 0, 8_000, 8_000, 0);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(source, GameWorldState.DefaultSpace, firstAnchor, 0, out int required));
    Equal(1, required);
    Equal(1, oracle.Subscriptions.Length);
    ClientSemanticChunkSlot first = oracle.Subscriptions[0].ResidentSlot;
    Equal(new SpatialChunkKey(GameWorldState.DefaultSpace, 0, 0), oracle.Subscriptions[0].Interest.Key);

    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(source, GameWorldState.DefaultSpace, firstAnchor, 1, out _));
    Equal(first, oracle.Subscriptions[0].ResidentSlot);

    WorldPosition replacementAnchor = new(10, -7, 8_000, 8_000, 0);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(source, GameWorldState.DefaultSpace, replacementAnchor, 2, out required));
    Equal(1, required);
    ClientSemanticChunkSlot replacement = oracle.Subscriptions[0].ResidentSlot;
    Equal(first.Index, replacement.Index);
    True(replacement.Generation > first.Generation);
    False(first == replacement);
    Equal(new SpatialChunkKey(GameWorldState.DefaultSpace, 10, -7), oracle.Subscriptions[0].Interest.Key);
}

static void LocalChunkSemanticApplicationRejectsContradictoryVersions()
{
    var oracle = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(1, 4),
        new ChunkInterestProfile(0, 0, 0, 0, 0, 0),
        new LocalChunkStreamingCadence(1, 1, 1));
    SpatialChunkKey key = new(GameWorldState.DefaultSpace, 0, 0);
    var id = new WorldPublicEntityId(2, 1);
    var record = new WorldPublicRecord(
        id,
        7,
        41,
        new WorldPosition(0, 0, 1_000, 2_000, 3_000),
        WorldYaw.QuarterTurn,
        WorldPublicStateFlags.Visible,
        new ChunkPublicationRevision(9));
    var present = new WorldPublicDeltaRecord(
        WorldPublicOperation.Present,
        WorldPublicFieldMask.Complete,
        id,
        record.EntityPublicVersion,
        record);
    oracle.ApplySemanticDeltaForTesting(key, present);
    oracle.ApplySemanticDeltaForTesting(key, present);

    WorldPublicRecord conflictingRecord = record with { PrefabId = 42 };
    Throws<InvalidOperationException>(() => oracle.ApplySemanticDeltaForTesting(key,
        present with { Current = conflictingRecord }));
    Throws<InvalidOperationException>(() => oracle.ApplySemanticDeltaForTesting(key,
        present with { Operation = WorldPublicOperation.Remove }));
    Throws<InvalidOperationException>(() => oracle.ApplySemanticDeltaForTesting(key,
        present with { Current = record with { EntityPublicVersion = 8 } }));
    SpatialChunkKey wrongKey = new(GameWorldState.DefaultSpace, 1, 0);
    Throws<InvalidOperationException>(() => oracle.ApplySemanticDeltaForTesting(wrongKey, present));

    var rows = new LocalChunkSemanticRecord[4];
    True(oracle.TryCopyResidentRecords(rows, out int count, out int required));
    Equal(1, count);
    Equal(1, required);
    Equal(new LocalChunkSemanticRecord(key, record), rows[0]);
}

static void LocalChunkStreamingOracleMatchesReferenceModelUnderChurn()
{
    const int operations = 5_000;
    const int capacity = 32;
    var limits = new ChunkPublicationLimits(capacity, capacity * 2, 8, 3);
    using var fixture = new RawSpatialFixture(128, capacity, publicationLimits: limits);
    var actors = new PublicationChurnActor[capacity];
    var knownKeys = new HashSet<SpatialChunkKey>();
    var random = new Random(0x70_07_570);
    uint nextPrefab = 1;
    for (int index = 0; index < 8; index++)
        True(CreateChurnActor(fixture, actors, knownKeys, random, ref nextPrefab, makePublic: index % 2 == 0));
    fixture.State.CommitAfterEcsStep(fixture.World);

    var oracle = new LocalChunkStreamingOracle(
        new LocalChunkStreamingLimits(1_024, capacity),
        GameChunkInterestProfile.Default,
        new LocalChunkStreamingCadence(1, 1, 1));
    WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
    Equal(LocalChunkStreamingStatus.Synchronized,
        oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor, 0, out _));
    AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);

    for (int operation = 1; operation <= operations; operation++)
    {
        MutateChurnActor(fixture, actors, knownKeys, random, ref nextPrefab);
        fixture.State.CommitAfterEcsStep(fixture.World);

        if (operation % 73 == 0)
        {
            int publicActor = FindPublicChurnActor(actors, random);
            if (publicActor < 0)
            {
                int existing = FindChurnActor(actors, random, requireAlive: true);
                if (existing >= 0)
                {
                    ref PublicationChurnActor actor = ref actors[existing];
                    True(fixture.World.TryTransition(actor.Entity, fixture.AddPublic));
                    actor.Public = true;
                    actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
                    True(fixture.State.TrySetPublic(fixture.World, actor.Entity, actor.Descriptor));
                }
                else
                {
                    True(CreateChurnActor(
                        fixture, actors, knownKeys, random, ref nextPrefab, makePublic: true));
                }
                fixture.State.CommitAfterEcsStep(fixture.World);
                publicActor = FindPublicChurnActor(actors, random);
            }
            True(publicActor >= 0);
            for (int dirty = 0; dirty < 5; dirty++)
            {
                ref PublicationChurnActor actor = ref actors[publicActor];
                actor.Descriptor = NextChurnDescriptor(random, ref nextPrefab);
                True(fixture.State.TrySetPublic(fixture.World, actor.Entity, actor.Descriptor));
                fixture.State.CommitAfterEcsStep(fixture.World);
            }
        }

        if (operation % 401 == 0)
            fixture.State.Rebuild(fixture.World);

        if (operation % 97 == 0)
        {
            anchor = new WorldPosition(random.Next(-2, 3), random.Next(-2, 3),
                checked((ushort)random.Next(GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                checked((ushort)random.Next(GameWorldSpatialProfile.WorldChunkEdgeUnits)), 0);
        }
        else
        {
            True(WorldPosition.TryFromGlobalMillimeters(
                anchor.GlobalXMillimeters + random.Next(-900, 901),
                0,
                anchor.GlobalZMillimeters + random.Next(-900, 901),
                out anchor));
        }

        Equal(LocalChunkStreamingStatus.Synchronized,
            oracle.Synchronize(fixture.State.Publications, GameWorldState.DefaultSpace, anchor,
                checked((ulong)operation), out _));
        AssertOracleMatchesInterestedPublication(fixture.State.Publications, oracle);
    }

    LocalChunkStreamingSnapshot snapshot = oracle.GetSnapshot();
    True(snapshot.SnapshotFallbacks > 0);
    True(snapshot.ChunkAbsentApplications > 0);
    Equal(fixture.State.Publications.Epoch, snapshot.SourceEpoch);
}

static int FindPublicChurnActor(PublicationChurnActor[] actors, Random random)
{
    int start = random.Next(actors.Length);
    for (int offset = 0; offset < actors.Length; offset++)
    {
        int index = (start + offset) % actors.Length;
        if (actors[index].Alive && actors[index].Public) return index;
    }
    return -1;
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

sealed class EmptyPersistentResolver : IExternalPersistentEntityResolver
{
    internal static readonly EmptyPersistentResolver Instance = new();
    public bool TryResolve(PersistentEntityKey key, out object? value)
    {
        value = null;
        return false;
    }
}

sealed class ChunkedReadStream : Stream
{
    private readonly byte[] _bytes;
    private readonly int _maximumRead;
    private int _position;

    internal ChunkedReadStream(byte[] bytes, int maximumRead)
    {
        _bytes = bytes;
        _maximumRead = maximumRead;
    }

    internal void Reset() => _position = 0;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int count = Math.Min(Math.Min(buffer.Length, _maximumRead), _bytes.Length - _position);
        if (count == 0) return 0;
        _bytes.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }
    public override int ReadByte() =>
        _position == _bytes.Length ? -1 : _bytes[_position++];
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
