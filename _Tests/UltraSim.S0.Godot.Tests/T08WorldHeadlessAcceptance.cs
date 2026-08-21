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
        var cache = new ClientWorldSemanticCache(world, new(4, 16, 8, 4, 32));
        PublicationEpoch epoch = PublicationEpoch.Create();
        using var renderer = new GodotWorldRenderer(
            this, cache, new GodotWorldRendererLimits(4, 8));
        var resident = new Dictionary<int, ClientSemanticChunkHandle>();
        var publicVersions = new ulong[8];
        Array.Fill(publicVersions, 1UL);
        var frameSamples = new List<double>();
        var meshSamples = new List<double>();
        var commitSamples = new List<double>();

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
        WorldPublicRecord updated = Public(0, Key(0, 0), publicVersions[0]);
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
        ulong rebuilds = renderer.GetSnapshot().Rebuilds;
        Expect(renderer.TryQueueResident(updatedHandle, visible: true, out _),
            "Updated render chunk did not queue.");
        Process(new GodotWorldFrameBudget(1, 0));
        Expect(renderer.GetSnapshot().Rebuilds == rebuilds + 1,
            "Changed semantic state did not replace its render chunk.");
        ValidateBoundedRepresentation();
        initialHash = cache.GetSnapshot().WorldSemanticHash;

        for (int cycle = 0; cycle < 3; cycle++)
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
        frameSamples.Sort();
        meshSamples.Sort();
        commitSamples.Sort();
        double p95 = frameSamples[(int)Math.Ceiling(frameSamples.Count * 0.95) - 1];
        double meshP95 = meshSamples[(int)Math.Ceiling(meshSamples.Count * 0.95) - 1];
        double commitP95 = commitSamples[(int)Math.Ceiling(commitSamples.Count * 0.95) - 1];
        GD.Print(
            $"T0.8 METRICS builds={final.CompletedBuilds} retires={final.CompletedRetirements} " +
            $"rebases={final.Rebases} mesh_build_p95_us={meshP95:F3} " +
            $"mesh_build_max_us={final.MaximumMeshBuildMicroseconds:F3} " +
            $"resource_commit_p95_us={commitP95:F3} " +
            $"resource_commit_max_us={final.MaximumResourceCommitMicroseconds:F3} " +
            $"frame_p95_us={p95:F3} frame_max_us={final.MaximumFrameMicroseconds:F3} " +
            $"semantic_reserved_bytes={cache.GetSnapshot().ReservedBytes}");

        void PopulateWindow(int start)
        {
            for (int x = start; x < start + 3; x++)
            {
                SpatialChunkKey key = Key(x, 0);
                Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
                    "Semantic request failed.");
                WorldPublicRecord record = Public(x, key, publicVersions[x]);
                var cursor = new ChunkPublicationCursor(
                    epoch, key, new ChunkPublicationRevision(publicVersions[x]));
                Expect(cache.TryStageFromBaseWorld(handle, [record], cursor, out _),
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
            WorldPublicRecord record = Public(entering, key, publicVersions[entering]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(publicVersions[entering]));
            Expect(cache.TryStageFromBaseWorld(handle, [record], cursor, out _),
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
            renderer.ProcessFrame(budget);
            GodotWorldRendererSnapshot snapshot = renderer.GetSnapshot();
            frameSamples.Add(snapshot.LastFrameMicroseconds);
            if (snapshot.CompletedBuilds != builds)
            {
                meshSamples.Add(snapshot.LastMeshBuildMicroseconds);
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
            Expect(snapshot.ActiveNodes <= 1 + resident.Count * 4,
                "The renderer created a node-shaped representation per cell/static.");
            Expect(snapshot.ActiveNodes < resident.Count * TerrainChunkFormat.MaterialCellCount,
                "Terrain cells leaked into scene-tree identity.");
        }
    }

    private static WorldPublicRecord Public(int x, SpatialChunkKey key, ulong version) => new(
        new WorldPublicEntityId(checked((uint)x + 1), 1),
        version,
        100,
        new WorldPosition(key.Coord.X, key.Coord.Z,
            checked((ushort)(8_000 + (version - 1) * 100)), 8_000, 1_000),
        WorldYaw.Zero,
        WorldPublicStateFlags.Visible,
        new ChunkPublicationRevision(version));

    private static BoundWorldPack CreateWorld()
    {
        const int chunkCount = 8;
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(4 * 1024 * 1024, chunkCount, 8, 128, 4, 8));
        for (int x = 0; x < chunkCount; x++)
        {
            ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
            Array.Fill(materials, checked((ushort)(x % 2 + 1)));
            var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
            Expect(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase),
                "Terrain base was invalid.");
            Expect(builder.TryAddBaseTerrain(Key(x, 0), materials, heightBase, offsets, out _),
                "Terrain chunk could not be added.");
            for (int row = 0; row < 8; row++)
            {
                var position = new WorldPosition(x, 0,
                    checked((ushort)(500 + row * 1_500)), 2_000, 500);
                Expect(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                    new WorldStaticId(checked((ulong)x * 100 + (ulong)row + 1)),
                    new WorldStaticDefinitionId(1),
                    position,
                    new WorldYaw(checked((ushort)(row * 4_096))),
                    0,
                    WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                    Bounds(position, 200)), out _), "Frozen static could not be added.");
            }
        }
        Expect(builder.TryBuild(out WorldPackArtifact? artifact, out _), "WorldPack build failed.");
        WorldPack pack = artifact!.Open();
        var binding = new WorldContentBinding(Content, [1, 2], [1]);
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
