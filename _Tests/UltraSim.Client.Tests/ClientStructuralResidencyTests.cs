using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class ClientStructuralResidencyTests
{
    private static readonly SpatialSpaceId Space = new(72);
    private static readonly WorldContentFingerprint Content = new(0x7201, 0xD001);

    internal static void Run()
    {
        BoundWorldPack world = CreateWorld();
        var cache = new ClientWorldSemanticCache(world, new(41, 0, 0, 1, 1));
        var residency = new ClientStructuralResidency(cache, renderRadiusChunks: 1);
        SpatialChunkKey center = Key(0, 0);
        True(residency.TryPrepare(center, false, out ClientStructuralResidencyPlan first,
            out ClientStructuralResidencyTransition initial, out _));
        Equal(25, initial.DesiredChunks);
        Equal(25, initial.EnteredChunks);
        residency.Commit(first);
        ClientStructuralResidencySnapshot initialSnapshot = residency.GetSnapshot();
        Equal(81, initialSnapshot.AuthoredChunks);
        Equal(25, initialSnapshot.DesiredResidentChunks);
        Equal(25, initialSnapshot.SemanticResidentChunks);
        True(initialSnapshot.AuthoredChunks > initialSnapshot.SemanticResidentChunks);

        var presentation = new ClientPresentationWindow(cache, 1);
        var changes = new ClientPresentationChange[18];
        WorldPosition player = new(0, 0, 1_000, 1_000, 0);
        True(presentation.TryPlanTrack(player, center, changes, out ClientPresentationWindowPlan renderPlan,
            out ClientPresentationWindowTransition rendered, out _, out _));
        Equal(9, rendered.ActiveChunks);
        presentation.Commit(renderPlan);
        True(initialSnapshot.SemanticResidentChunks > rendered.ActiveChunks);

        SpatialChunkKey movedCenter = Key(1, 0);
        True(residency.TryPrepare(movedCenter, false, out ClientStructuralResidencyPlan moved,
            out ClientStructuralResidencyTransition transition, out _));
        Equal(5, transition.EnteredChunks);
        Equal(20, transition.RetainedChunks);
        Equal(5, transition.ExitedChunks);
        residency.Commit(moved);
        ClientStructuralResidencySnapshot movedSnapshot = residency.GetSnapshot();
        Equal(25, movedSnapshot.SemanticResidentChunks);
        Equal((ulong)30, movedSnapshot.SemanticEnters);
        Equal((ulong)5, movedSnapshot.SemanticExits);

        SpatialChunkKey publicOnlyKey = Key(-4, 4);
        PublicationEpoch epoch = PublicationEpoch.Create();
        var publicRow = new WorldPublicRecord(
            new WorldPublicEntityId(1, 1),
            1,
            10,
            new WorldPosition(-4, 4, 1_000, 0, 1_000),
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(1));
        True(cache.TryRequest(publicOnlyKey, out ClientSemanticChunkHandle publicHandle, out _));
        True(cache.TryStageFromBaseWorld(
            publicHandle,
            [publicRow],
            new ChunkPublicationCursor(epoch, publicOnlyKey, new ChunkPublicationRevision(1)),
            out _));
        True(cache.TryPublish(publicHandle, out _));
        True(residency.TryPrepare(movedCenter, false, out ClientStructuralResidencyPlan preservePublic,
            out _, out _));
        residency.Commit(preservePublic);
        True(cache.TryGetResidentHandle(publicOnlyKey, out publicHandle));
        True(cache.TryRetirePublicInterest(publicHandle, epoch, out _));
        True(residency.TryPrepare(movedCenter, false, out ClientStructuralResidencyPlan reapPublic,
            out _, out _));
        residency.Commit(reapPublic);
        False(cache.TryGetResidentHandle(publicOnlyKey, out _));

        var exactCache = new ClientWorldSemanticCache(world, new(25, 0, 0, 1, 1));
        var exact = new ClientStructuralResidency(exactCache, 1);
        True(exact.TryPrepare(center, false, out ClientStructuralResidencyPlan exactInitial,
            out _, out _));
        exact.Commit(exactInitial);
        False(exact.TryPrepare(movedCenter, false, out _, out _,
            out ClientStructuralResidencyError capacity));
        Equal(ClientStructuralResidencyError.SemanticCacheRejected, capacity);
        ClientStructuralResidencySnapshot afterFailure = exact.GetSnapshot();
        Equal(25, afterFailure.DesiredResidentChunks);
        Equal(25, afterFailure.SemanticResidentChunks);
        True(exactCache.TryGetResidentHandle(Key(-2, 0), out _));
        False(exactCache.TryGetResidentHandle(Key(3, 0), out _));

        SpatialChunkKey edge = residency.GetPresentationCenter(
            new WorldPosition(4, 4, 15_000, 15_000, 0));
        Equal(Key(3, 3), edge);

        True(residency.TryPrepare(movedCenter, false, out ClientStructuralResidencyPlan warm,
            out _, out _));
        residency.Commit(warm);
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 64; index++)
        {
            True(residency.TryPrepare(movedCenter, false, out ClientStructuralResidencyPlan steady,
                out _, out _));
            residency.Commit(steady);
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - beforeAllocation);

        // The middle supported radius has the largest non-overlapping transition on this finite
        // 9x9 source. Its capacity must be chosen explicitly by composition; the controller must
        // reject atomically rather than silently dropping part of the structural neighborhood.
        var radiusTwoCache = new ClientWorldSemanticCache(world, new(49, 0, 0, 1, 1));
        var radiusTwo = new ClientStructuralResidency(radiusTwoCache, 2);
        True(radiusTwo.TryPrepare(Key(-2, -2), false, out ClientStructuralResidencyPlan lower,
            out _, out _));
        radiusTwo.Commit(lower);
        False(radiusTwo.TryPrepare(Key(2, 2), true, out _, out _,
            out ClientStructuralResidencyError resetCapacity));
        Equal(ClientStructuralResidencyError.SemanticCacheRejected, resetCapacity);
        ClientStructuralResidencySnapshot radiusTwoFailure = radiusTwo.GetSnapshot();
        Equal(36, radiusTwoFailure.DesiredResidentChunks);
        Equal(36, radiusTwoFailure.SemanticResidentChunks);

        // Two complete authored clusters may share one WorldPack without filling the distance
        // between them with fake chunks. A distant transition explicitly retires the old bounded
        // working set before admitting the new one, so capacity need not hold both clusters.
        BoundWorldPack sparseWorld = CreateSparseWorld();
        var sparseCache = new ClientWorldSemanticCache(sparseWorld, new(41, 0, 0, 1, 1));
        var sparse = new ClientStructuralResidency(sparseCache, 1);
        True(sparse.TryPrepare(Key(0, 0), false, out ClientStructuralResidencyPlan central,
            out ClientStructuralResidencyTransition centralTransition, out _));
        Equal(25, centralTransition.DesiredChunks);
        sparse.Commit(central);
        Equal(162, sparse.GetSnapshot().AuthoredChunks);
        False(sparse.TryPrepare(Key(50, 0), false, out _, out _,
            out ClientStructuralResidencyError gap));
        Equal(ClientStructuralResidencyError.InvalidCenter, gap);

        True(sparse.TryPrepareReset(out ClientStructuralResidencyPlan clear,
            out ClientStructuralResidencyTransition clearing, out _));
        Equal(25, clearing.ExitedChunks);
        Equal(0, clearing.DesiredChunks);
        sparse.Commit(clear);
        Equal(0, sparse.GetSnapshot().SemanticResidentChunks);

        SpatialChunkKey farCenter = Key(100, 0);
        True(sparse.TryPrepare(farCenter, false, out ClientStructuralResidencyPlan far,
            out ClientStructuralResidencyTransition farTransition, out _));
        Equal(25, farTransition.EnteredChunks);
        Equal(0, farTransition.RetainedChunks);
        sparse.Commit(far);
        ClientStructuralResidencySnapshot farSnapshot = sparse.GetSnapshot();
        Equal(25, farSnapshot.SemanticResidentChunks);
        Equal((ulong)1, farSnapshot.ResetTransitions);
        False(sparseCache.TryGetResidentHandle(Key(0, 0), out _));
        True(sparseCache.TryGetResidentHandle(farCenter, out _));
        Equal(Key(103, 3), sparse.GetPresentationCenter(
            new WorldPosition(104, 4, 15_000, 0, 15_000)));
    }

    private static BoundWorldPack CreateWorld()
    {
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(2 * 1024 * 1024, 81, 2, 0, 0, 0));
        var materials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
        for (int x = -4; x <= 4; x++)
        for (int z = -4; z <= 4; z++)
            True(builder.TryAddBaseTerrain(Key(x, z), materials, heightBase, offsets, out _));
        True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
        True(artifact!.Open().TryBind(
            new WorldContentBinding(Content, [1]), out BoundWorldPack? bound, out _));
        return bound!;
    }

    private static BoundWorldPack CreateSparseWorld()
    {
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(4 * 1024 * 1024, 162, 2, 0, 0, 0));
        var materials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
        AddCluster(-4, 4);
        AddCluster(96, 104);
        True(builder.TryBuild(out WorldPackArtifact? artifact, out _));
        True(artifact!.Open().TryBind(
            new WorldContentBinding(Content, [1]), out BoundWorldPack? bound, out _));
        return bound!;

        void AddCluster(int minimumX, int maximumX)
        {
            for (int x = minimumX; x <= maximumX; x++)
            for (int z = -4; z <= 4; z++)
                True(builder.TryAddBaseTerrain(
                    Key(x, z), materials, heightBase, offsets, out _));
        }
    }

    private static SpatialChunkKey Key(int x, int z) => new(Space, x, z);

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected false.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
