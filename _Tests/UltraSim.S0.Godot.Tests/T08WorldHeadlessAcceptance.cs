using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.World;
using UltraSim.Spatial;

public partial class T08WorldHeadlessAcceptance : Node3D
{
    private static readonly SpatialSpaceId Space = new(808);
    private static readonly WorldContentFingerprint Content = new(0x8080, 0x20260821);

    public override void _Ready()
    {
        try
        {
            RunAcceptance();
            GD.Print("T0.8 GODOT WORLD ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"T0.8 GODOT WORLD ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunAcceptance()
    {
        BoundWorldPack world = CreateWorld();
        var cache = new ClientWorldSemanticCache(world, new(4, 16, 8, 4, 64));
        PublicationEpoch epoch = PublicationEpoch.Create();
        double nativeResourceBaseline = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double renderBufferBaseline = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        using var renderer = new GodotWorldRenderer(
            this, cache, new GodotWorldRendererLimits(4, 16));
        var resident = new Dictionary<int, ClientSemanticChunkHandle>();
        var publicVersions = new ulong[8];
        Array.Fill(publicVersions, 1UL);
        var frameSamples = new List<double>();
        var meshSamples = new List<double>();
        var publicRefreshSamples = new List<double>();
        var commitSamples = new List<double>();
        double peakNativeResources = nativeResourceBaseline;
        double peakRenderBufferBytes = renderBufferBaseline;

        PopulateWindow(0);
        ClientSemanticHash initialHash = cache.GetSnapshot().WorldSemanticHash;
        Expect(cache.GetSnapshot().ResidentChunks == 3, "Initial semantic window was not resident.");
        Process(new GodotWorldFrameBudget(1, 1));
        Expect(renderer.GetSnapshot().ActiveRenderChunks == 1,
            "Render budget did not remain independent from semantic residency.");
        DrainRenderer();
        ValidateBoundedRepresentation();

        ClientSemanticChunkHandle updatedHandle = resident[0];
        publicVersions[0] = 2;
        WorldPublicRecord updated = Public(0, 0, Key(0, 0), publicVersions[0]);
        var update = new WorldPublicDeltaRecord(
            WorldPublicOperation.Present, WorldPublicFieldMask.Transform,
            updated.Id, updated.EntityPublicVersion, updated);
        var updateBatch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Delta, Key(0, 0),
            new ChunkPublicationCursor(epoch, Key(0, 0), new ChunkPublicationRevision(2)),
            new ChunkPublicationRevision(1), 1, 1);
        ClientSemanticHash beforeUpdate = cache.GetSnapshot().WorldSemanticHash;
        Expect(cache.TryApplyDelta(updatedHandle, updateBatch, [update], out _),
            "Dynamic public semantic delta did not apply.");
        Expect(cache.GetSnapshot().WorldSemanticHash != beforeUpdate,
            "Dynamic public delta did not change semantic identity.");
        GodotWorldRendererSnapshot beforeRefresh = renderer.GetSnapshot();
        Expect(renderer.TryGetChunkDiagnostics(updatedHandle, out GodotWorldRenderChunkDiagnostics beforeIdentity),
            "Updated render chunk diagnostics were unavailable.");
        Expect(renderer.TryQueueResident(updatedHandle, visible: true, out _),
            "Updated render chunk did not queue.");
        Process(new GodotWorldFrameBudget(1, 0));
        GodotWorldRendererSnapshot afterRefresh = renderer.GetSnapshot();
        Expect(afterRefresh.PublicRefreshes == beforeRefresh.PublicRefreshes + 1,
            "Changed public semantics did not refresh the public layer.");
        Expect(afterRefresh.CompletedBuilds == beforeRefresh.CompletedBuilds &&
            afterRefresh.Rebuilds == beforeRefresh.Rebuilds,
            "Changed public semantics rebuilt immutable chunk presentation.");
        Expect(renderer.TryGetChunkDiagnostics(updatedHandle, out GodotWorldRenderChunkDiagnostics afterIdentity),
            "Refreshed render chunk diagnostics were unavailable.");
        Expect(afterIdentity.StructuralObjectId == beforeIdentity.StructuralObjectId &&
            afterIdentity.TerrainMeshObjectId == beforeIdentity.TerrainMeshObjectId &&
            afterIdentity.PublicObjectId != beforeIdentity.PublicObjectId,
            "Public refresh did not preserve terrain/static resource identity.");
        for (ulong version = 3; version <= 33; version++)
        {
            publicVersions[0] = version;
            WorldPublicRecord next = Public(0, 0, Key(0, 0), version);
            var delta = new WorldPublicDeltaRecord(
                WorldPublicOperation.Present, WorldPublicFieldMask.Transform,
                next.Id, next.EntityPublicVersion, next);
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta, Key(0, 0),
                new ChunkPublicationCursor(epoch, Key(0, 0), new ChunkPublicationRevision(version)),
                new ChunkPublicationRevision(1), 1, 1);
            Expect(cache.TryApplyDelta(updatedHandle, batch, [delta], out _),
                "Repeated public refresh delta did not apply.");
            Expect(renderer.TryQueueResident(updatedHandle, true, out _),
                "Repeated public refresh did not queue.");
            Process(new GodotWorldFrameBudget(1, 0));
        }
        ValidateBoundedRepresentation();
        initialHash = cache.GetSnapshot().WorldSemanticHash;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            for (int step = 1; step <= 5; step++) MoveWindow(step);
            for (int step = 4; step >= 0; step--) MoveWindow(step);
        }
        Expect(cache.GetSnapshot().WorldSemanticHash == initialHash,
            "Returning to the initial semantic window changed its canonical hash.");

        ClientSemanticHash beforeRebase = cache.GetSnapshot().WorldSemanticHash;
        renderer.SetRenderOrigin(new WorldRenderOrigin(new SpatialChunkCoord(100_000, -100_000), 1_250_000));
        Expect(cache.GetSnapshot().WorldSemanticHash == beforeRebase,
            "Presentation-origin rebase changed semantic authority.");
        ValidateBoundedRepresentation();

        foreach (ClientSemanticChunkHandle handle in resident.Values)
        {
            Expect(cache.BeginRetire(handle), "Semantic retirement did not begin.");
            Expect(renderer.TryQueueRetire(handle, out _), "Render retirement did not queue.");
        }
        DrainRenderer();
        foreach (ClientSemanticChunkHandle handle in resident.Values)
            Expect(cache.CompleteRetire(handle), "Semantic retirement did not complete.");
        resident.Clear();

        GodotWorldRendererSnapshot final = renderer.GetSnapshot();
        Expect(final.ActiveRenderChunks == 0 && final.VisibleRenderChunks == 0,
            "Render chunks leaked after the walk.");
        Expect(final.ActiveNodes == 1 && final.ActiveResources == 0,
            "Godot nodes/resources leaked after the walk.");
        Expect(final.CompletedBuilds >= 13 && final.CompletedRetirements >= 13,
            "The repeated enter/leave path was not exercised.");
        RendererCapacityAndIdentityRegressions(world);
        HighElevationPrecisionRegression();
        renderer.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        double nativeResourcesAfter = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double renderBufferAfter = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        Expect(nativeResourcesAfter <= peakNativeResources,
            "Native resource count continued growing after renderer disposal.");
        Expect(renderBufferAfter <= peakRenderBufferBytes,
            "Rendering buffer memory continued growing after renderer disposal.");
        frameSamples.Sort();
        meshSamples.Sort();
        publicRefreshSamples.Sort();
        commitSamples.Sort();
        double p95 = frameSamples[(int)Math.Ceiling(frameSamples.Count * 0.95) - 1];
        double meshP95 = meshSamples[(int)Math.Ceiling(meshSamples.Count * 0.95) - 1];
        double publicRefreshP95 = publicRefreshSamples[
            (int)Math.Ceiling(publicRefreshSamples.Count * 0.95) - 1];
        double commitP95 = commitSamples[(int)Math.Ceiling(commitSamples.Count * 0.95) - 1];
        GD.Print(
            $"T0.8 METRICS builds={final.CompletedBuilds} public_refreshes={final.PublicRefreshes} " +
            $"retires={final.CompletedRetirements} " +
            $"rebases={final.Rebases} mesh_build_p95_us={meshP95:F3} " +
            $"mesh_build_max_us={final.MaximumMeshBuildMicroseconds:F3} " +
            $"public_refresh_p95_us={publicRefreshP95:F3} " +
            $"resource_commit_p95_us={commitP95:F3} " +
            $"resource_commit_max_us={final.MaximumResourceCommitMicroseconds:F3} " +
            $"frame_p95_us={p95:F3} frame_max_us={final.MaximumFrameMicroseconds:F3} " +
            $"semantic_reserved_bytes={cache.GetSnapshot().ReservedBytes} " +
            $"native_resources_baseline={nativeResourceBaseline:F0} " +
            $"native_resources_peak={peakNativeResources:F0} " +
            $"native_resources_after={nativeResourcesAfter:F0} " +
            $"render_buffer_baseline_bytes={renderBufferBaseline:F0} " +
            $"render_buffer_peak_bytes={peakRenderBufferBytes:F0} " +
            $"render_buffer_after_bytes={renderBufferAfter:F0}");

        void PopulateWindow(int start)
        {
            for (int x = start; x < start + 3; x++)
            {
                SpatialChunkKey key = Key(x, 0);
                Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
                    "Semantic request failed.");
                WorldPublicRecord[] records = PublicRecords(x, key, publicVersions[x]);
                var cursor = new ChunkPublicationCursor(
                    epoch, key, new ChunkPublicationRevision(publicVersions[x]));
                Expect(cache.TryStageFromBaseWorld(handle, records, cursor, out _),
                    "Semantic staging failed.");
                Expect(cache.TryPublish(handle, out _), "Semantic publication failed.");
                resident.Add(x, handle);
                Expect(renderer.TryQueueResident(handle, visible: x != start + 2, out _),
                    "Render build did not queue.");
            }
        }

        void MoveWindow(int start)
        {
            int leaving = resident.Keys.Single(x => x < start || x >= start + 3);
            ClientSemanticChunkHandle leavingHandle = resident[leaving];
            Expect(cache.BeginRetire(leavingHandle), "Walk retirement did not begin.");
            Expect(renderer.TryQueueRetire(leavingHandle, out _), "Walk retirement did not queue.");
            Process(new GodotWorldFrameBudget(0, 1));
            Expect(!renderer.IsRendered(leavingHandle), "Retired render chunk remained active.");
            Expect(cache.CompleteRetire(leavingHandle), "Walk retirement did not complete.");
            resident.Remove(leaving);

            int entering = Enumerable.Range(start, 3).Single(x => !resident.ContainsKey(x));
            SpatialChunkKey key = Key(entering, 0);
            Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
                "Walk semantic request failed.");
            WorldPublicRecord[] records = PublicRecords(entering, key, publicVersions[entering]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(publicVersions[entering]));
            Expect(cache.TryStageFromBaseWorld(handle, records, cursor, out _),
                "Walk semantic staging failed.");
            Expect(cache.TryPublish(handle, out _), "Walk semantic publication failed.");
            resident.Add(entering, handle);
            Expect(renderer.TryQueueResident(handle, visible: entering != start + 2, out _),
                "Walk render build did not queue.");
            Process(new GodotWorldFrameBudget(1, 0));
            DrainRenderer();
            foreach ((int x, ClientSemanticChunkHandle current) in resident)
                Expect(renderer.TrySetVisible(current, x != start + 2),
                    "Walk visibility update did not reach the render chunk.");
            ValidateBoundedRepresentation();
        }

        void DrainRenderer()
        {
            while (renderer.GetSnapshot().PendingBuilds != 0 ||
                renderer.GetSnapshot().PendingRetirements != 0)
            {
                Process(new GodotWorldFrameBudget(1, 1));
            }
        }

        void Process(GodotWorldFrameBudget budget)
        {
            ulong builds = renderer.GetSnapshot().CompletedBuilds;
            ulong refreshes = renderer.GetSnapshot().PublicRefreshes;
            renderer.ProcessFrame(budget);
            GodotWorldRendererSnapshot snapshot = renderer.GetSnapshot();
            peakNativeResources = Math.Max(peakNativeResources,
                Performance.GetMonitor(Performance.Monitor.ObjectResourceCount));
            peakRenderBufferBytes = Math.Max(peakRenderBufferBytes,
                Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed));
            frameSamples.Add(snapshot.LastFrameMicroseconds);
            if (snapshot.CompletedBuilds != builds)
            {
                meshSamples.Add(snapshot.LastMeshBuildMicroseconds);
                commitSamples.Add(snapshot.LastResourceCommitMicroseconds);
            }
            else if (snapshot.PublicRefreshes != refreshes)
            {
                publicRefreshSamples.Add(snapshot.LastMeshBuildMicroseconds);
                commitSamples.Add(snapshot.LastResourceCommitMicroseconds);
            }
        }

        void ValidateBoundedRepresentation()
        {
            GodotWorldRendererSnapshot snapshot = renderer.GetSnapshot();
            Expect(snapshot.ActiveRenderChunks == resident.Count,
                "Render residency did not converge to the requested window.");
            Expect(snapshot.VisibleRenderChunks < snapshot.ActiveRenderChunks,
                "Semantic residency and render visibility collapsed into one state.");
            Expect(snapshot.ActiveNodes <= 1 + resident.Count * 16,
                "The renderer exceeded the bounded heterogeneous group shape.");
            Expect(snapshot.ActiveNodes < resident.Count * TerrainChunkFormat.MaterialCellCount,
                "Terrain cells leaked into scene-tree identity.");
        }
    }

    private void RendererCapacityAndIdentityRegressions(BoundWorldPack world)
    {
        var cache = new ClientWorldSemanticCache(world, new(4, 16, 8, 4, 32));
        var handles = new ClientSemanticChunkHandle[4];
        for (int index = 0; index < handles.Length; index++)
        {
            Expect(cache.TryRequest(Key(index, 0), out handles[index], out _),
                "Capacity fixture request failed.");
            Expect(cache.TryStageFromBaseWorld(handles[index], [], default, out _),
                "Capacity fixture stage failed.");
            Expect(cache.TryPublish(handles[index], out _), "Capacity fixture publish failed.");
        }
        Expect(handles[3].Index >= 3, "Capacity fixture did not create a high semantic slot.");

        using (var renderer = new GodotWorldRenderer(this, cache, new GodotWorldRendererLimits(1, 16)))
        {
            Expect(renderer.TryQueueResident(handles[0], true, out _), "First private render slot did not queue.");
            renderer.ProcessFrame(new GodotWorldFrameBudget(1, 0));
            Expect(!renderer.TryQueueResident(handles[3], true, out GodotWorldRenderQueueError capacity) &&
                capacity == GodotWorldRenderQueueError.RenderCapacityExceeded,
                "Concurrent render capacity was not enforced independently.");
            Expect(renderer.TryQueueRetire(handles[0], out _), "Private render slot did not retire.");
            renderer.ProcessFrame(new GodotWorldFrameBudget(0, 1));
            Expect(renderer.TryQueueResident(handles[3], true, out _),
                "A high-index semantic handle could not use a free private render slot.");
            renderer.ProcessFrame(new GodotWorldFrameBudget(1, 0));
            Expect(renderer.IsRendered(handles[3]), "High-index semantic handle was not rendered.");
            Expect(renderer.TryGetChunkDiagnostics(handles[3], out GodotWorldRenderChunkDiagnostics diagnostic) &&
                diagnostic.PrivateRenderSlot == 0,
                "Semantic and private render slot indices remained numerically coupled.");
        }

        using var constrained = new GodotWorldRenderer(this, cache, new GodotWorldRendererLimits(1, 1));
        Expect(!constrained.TryQueueResident(handles[3], true, out GodotWorldRenderQueueError groups) &&
            groups == GodotWorldRenderQueueError.GroupCapacityExceeded,
            "Expected group-capacity pressure was not rejected before construction.");
        Expect(constrained.GetSnapshot().ActiveResources == 0 &&
            constrained.GetSnapshot().GroupCapacityRejections == 1,
            "Group-capacity rejection created Godot resources or was not accounted.");
    }

    private void HighElevationPrecisionRegression()
    {
        const int baseHeight = 2_000_000_000;
        BoundWorldPack world = CreateHighWorld(baseHeight);
        var cache = new ClientWorldSemanticCache(world, new(1, 2, 0, 2, 4));
        SpatialChunkKey key = new(Space, -2, 3);
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPublicRecord record = new(
            new WorldPublicEntityId(1, 1), 1, 501,
            new WorldPosition(key.Coord.X, key.Coord.Z, 2_000, 2_000, baseHeight + 10),
            WorldYaw.Zero, WorldPublicStateFlags.Visible, new ChunkPublicationRevision(1));
        Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
            "High-elevation request failed.");
        Expect(cache.TryStageFromBaseWorld(handle, [record],
            new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(1)), out _),
            "High-elevation staging failed.");
        Expect(cache.TryPublish(handle, out _), "High-elevation publication failed.");
        using var renderer = new GodotWorldRenderer(this, cache, new GodotWorldRendererLimits(1, 4));
        renderer.SetRenderOrigin(new WorldRenderOrigin(key.Coord, baseHeight));
        Expect(renderer.TryQueueResident(handle, true, out _), "High-elevation render did not queue.");
        renderer.ProcessFrame(new GodotWorldFrameBudget(1, 0));
        Expect(renderer.TryGetChunkDiagnostics(handle, out GodotWorldRenderChunkDiagnostics diagnostic),
            "High-elevation diagnostics were unavailable.");
        Expect(diagnostic.YAnchorMillimeters == baseHeight &&
            MathF.Abs(diagnostic.RootLocalY) < 0.000001f &&
            MathF.Abs(diagnostic.FirstTerrainLocalY) < 0.000001f &&
            MathF.Abs(diagnostic.FirstStaticLocalY - 0.005f) < 0.000001f &&
            MathF.Abs(diagnostic.FirstPublicLocalY - 0.010f) < 0.000001f,
            "Integer-relative Y conversion lost millimetre-scale differences at high elevation.");
    }

    private static BoundWorldPack CreateHighWorld(int baseHeight)
    {
        SpatialChunkKey key = new(Space, -2, 3);
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(512 * 1024, 1, 8, 2, 0, 0));
        ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, (ushort)1);
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        Expect(TerrainHeightPlaneBase.TryCreate(baseHeight, out TerrainHeightPlaneBase heightBase),
            "High terrain base was invalid.");
        Expect(builder.TryAddBaseTerrain(key, materials, heightBase, offsets, out _),
            "High terrain chunk could not be added.");
        WorldPosition position = new(key.Coord.X, key.Coord.Z, 1_000, 1_000, baseHeight + 5);
        Expect(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
            new WorldStaticId(1), new WorldStaticDefinitionId(1), position, WorldYaw.Zero, 0,
            WorldStaticFlags.Renderable, Bounds(position, 2)), out _),
            "High static could not be added.");
        Expect(builder.TryBuild(out WorldPackArtifact? artifact, out _), "High WorldPack build failed.");
        WorldPack pack = artifact!.Open();
        Expect(pack.TryBind(new WorldContentBinding(Content, [1], [1]),
            out BoundWorldPack? bound, out _), "High WorldPack binding failed.");
        return bound!;
    }

    private static WorldPublicRecord Public(int x, int row, SpatialChunkKey key, ulong version) => new(
        new WorldPublicEntityId(checked((uint)(x * 4 + row) + 1), 1),
        version,
        checked((uint)(100 + row)),
        new WorldPosition(key.Coord.X, key.Coord.Z,
            checked((ushort)(5_000UL + (ulong)row * 1_500UL + (version - 1) * 100UL)),
            checked((ushort)(7_000 + row * 1_000)), 1_000 + row * 5),
        WorldYaw.Zero,
        WorldPublicStateFlags.Visible,
        new ChunkPublicationRevision(version));

    private static WorldPublicRecord[] PublicRecords(int x, SpatialChunkKey key, ulong firstVersion) =>
    [
        Public(x, 0, key, firstVersion),
        Public(x, 1, key, 1),
        Public(x, 2, key, 1),
    ];

    private static BoundWorldPack CreateWorld()
    {
        const int chunkCount = 8;
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(4 * 1024 * 1024, chunkCount, 8, 128, 16, 32));
        for (int x = 0; x < chunkCount; x++)
        {
            ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
            for (int cell = 0; cell < materials.Length; cell++)
                materials[cell] = checked((ushort)((cell + x) % 4 + 1));
            var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
            Expect(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase),
                "Terrain base was invalid.");
            Expect(builder.TryAddBaseTerrain(Key(x, 0), materials, heightBase, offsets, out _),
                "Terrain chunk could not be added.");
            for (int row = 0; row < 12; row++)
            {
                var position = new WorldPosition(x, 0,
                    checked((ushort)(500 + row * 1_200)), 2_000, 500);
                Expect(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                    new WorldStaticId(checked((ulong)x * 100 + (ulong)row + 1)),
                    new WorldStaticDefinitionId(checked((uint)(row % 6 + 1))),
                    position,
                    new WorldYaw(checked((ushort)(row * 4_096))),
                    checked((ushort)(row & 1)),
                    WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                    Bounds(position, 200)), out _), "Frozen static could not be added.");
            }
            WorldPosition root = new(x, 0, 10_000, 10_000, 700);
            WorldMultiPieceSource[] pieces =
            [
                new(new MultiPieceId(1), new WorldStaticDefinitionId(7), root,
                    WorldYaw.Zero, 0, WorldStaticFlags.Renderable, Bounds(root, 150)),
                new(new MultiPieceId(2), new WorldStaticDefinitionId(8),
                    new WorldPosition(x, 0, 11_000, 10_000, 700),
                    WorldYaw.QuarterTurn, 0, WorldStaticFlags.Renderable,
                    Bounds(new WorldPosition(x, 0, 11_000, 10_000, 700), 150)),
            ];
            Expect(builder.TryAddStaticMulti(new MultiInstanceId(checked((ulong)x + 1)), root,
                WorldYaw.Zero, pieces, out _), "Static Multi could not be added.");
        }
        Expect(builder.TryBuild(out WorldPackArtifact? artifact, out _), "WorldPack build failed.");
        WorldPack pack = artifact!.Open();
        var binding = new WorldContentBinding(Content, [1, 2, 3, 4], [1, 2, 3, 4, 5, 6, 7, 8]);
        Expect(pack.TryBind(binding, out BoundWorldPack? bound, out _), "WorldPack binding failed.");
        return bound!;
    }

    private static WorldStaticBounds Bounds(WorldPosition position, int half) => new(
        position.GlobalXMillimeters - half,
        position.YMillimeters - half,
        position.GlobalZMillimeters - half,
        position.GlobalXMillimeters + half,
        position.YMillimeters + half,
        position.GlobalZMillimeters + half);

    private static SpatialChunkKey Key(int x, int z) => new(Space, x, z);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
