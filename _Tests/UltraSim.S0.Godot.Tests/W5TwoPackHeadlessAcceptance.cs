using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.World.Testing;

[GlobalClass]
public partial class W5TwoPackHeadlessAcceptance : Node3D
{
    public override void _Ready()
    {
        try
        {
            Run();
            GD.Print("W5 TWO-PACK GODOT ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"W5 TWO-PACK GODOT ACCEPTANCE FAIL: {error}");
            GetTree().Quit(1);
        }
    }

    private void Run()
    {
        var (composition, _, _) = W5TwoPackFixture.Create();
        Expect(composition.TryValidateComplete(out _), "Canonical two-Pack source was invalid.");
        var cache = new ClientWorldSemanticCache(composition,
            ClientWorldSemanticCacheLimits.ForStructuralViewRadius(1, 21,
                maximumFrozenStaticsPerChunk: 2,
                maximumMultiFragmentsPerChunk: 2,
                maximumPublicRecordsPerChunk: 1,
                maximumKnownPublicIdentities: 1));
        var residency = new ClientStructuralResidency(cache, 1);
        using var renderer = new GodotWorldRenderer(this, cache,
            GodotWorldRendererLimits.ForStructuralWindow(1, 5, 8));
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        Node3D root = renderer.Root;

        Track(new WorldPosition(127, 0, 15_000, 8_000, 0));
        Expect(cache.GetSnapshot().HasCanonicalComposition &&
            cache.GetSnapshot().StructuralManifestFingerprint == composition.ManifestFingerprint &&
            cache.GetSnapshot().BaseWorldFingerprint == default &&
            residency.GetSnapshot().SemanticResidentChunks == 25 &&
            renderer.GetSnapshot().ActiveRenderChunks == 9,
            "Initial multi-Pack semantic/render window was incomplete or misidentified.");
        var westKey = new SpatialChunkKey(composition.Space, 127, 0);
        var eastKey = new SpatialChunkKey(composition.Space, 128, 0);
        bool hasWest = cache.TryGetResidentHandle(westKey, out ClientSemanticChunkHandle westHandle);
        bool hasEast = cache.TryGetResidentHandle(eastKey, out ClientSemanticChunkHandle eastHandle);
        Expect(hasWest && hasEast,
            "The first working set did not span both Pack addresses.");
        CheckNativeSeam(westHandle, eastHandle);

        Track(new WorldPosition(128, 0, 1_000, 8_000, 0));
        bool hasWestAfter = cache.TryGetResidentHandle(westKey, out ClientSemanticChunkHandle westAfter);
        bool hasEastAfter = cache.TryGetResidentHandle(eastKey, out ClientSemanticChunkHandle eastAfter);
        Expect(ReferenceEquals(root, renderer.Root) && hasWestAfter && hasEastAfter &&
            westAfter == westHandle && eastAfter == eastHandle &&
            residency.GetSnapshot().ResetTransitions == 0 &&
            residency.GetSnapshot().SemanticResidentChunks == 25 &&
            renderer.GetSnapshot().ActiveRenderChunks == 9,
            "Crossing the Pack seam reset or replaced semantic/native World ownership.");
        CheckNativeSeam(westAfter, eastAfter);

        Expect(window.TryResetForJump(new WorldPosition(128, 0, 1_000, 8_000, 0), changes,
                out _, out _, out _, out _), "W5 native presentation reset was rejected.");
        Drain();
        Expect(residency.TryPrepareReset(out ClientStructuralResidencyPlan reset, out _, out _),
            "W5 semantic retirement could not prepare.");
        residency.Commit(reset);
        GodotWorldRendererSnapshot retired = renderer.GetSnapshot();
        Expect(cache.GetSnapshot().ResidentChunks == 0 && retired.ActiveRenderChunks == 0 &&
            retired.ActiveResources == 0 && retired.ActiveNodes == 1,
            "W5 native/semantic session resources did not retire.");
        GD.Print($"W5 TWO-PACK NATIVE manifest={composition.ManifestFingerprint} " +
            $"semantic_peak={residency.GetSnapshot().PeakSemanticResidentChunks} " +
            $"builds={retired.CompletedBuilds} retirements={retired.CompletedRetirements}");

        void Track(WorldPosition player)
        {
            SpatialChunkKey center = residency.GetPresentationCenter(player);
            Expect(residency.TryPrepare(center, false, out ClientStructuralResidencyPlan plan,
                out _, out ClientStructuralResidencyError residenceError),
                $"W5 semantic window rejected: {residenceError}.");
            if (!window.TryTrackPlayer(player, center, changes, out _, out _,
                    out ClientPresentationWindowError windowError,
                    out GodotWorldRenderQueueError rendererError))
            {
                residency.Cancel(plan);
                throw new InvalidOperationException($"W5 native window rejected: {windowError}/{rendererError}.");
            }
            residency.Commit(plan);
            Drain();
        }

        void Drain()
        {
            int frames = 0;
            while (renderer.GetSnapshot().PendingBuilds != 0 ||
                   renderer.GetSnapshot().PendingRetirements != 0)
            {
                renderer.ProcessFrame(new GodotWorldFrameBudget(4, 4));
                Expect(++frames <= 64, "W5 native work did not drain.");
            }
        }

        void CheckNativeSeam(ClientSemanticChunkHandle west, ClientSemanticChunkHandle east)
        {
            Expect(renderer.TryGetChunkDiagnostics(west, out GodotWorldRenderChunkDiagnostics w) &&
                renderer.TryGetChunkDiagnostics(east, out GodotWorldRenderChunkDiagnostics e) &&
                w.TerrainTriangleCount > 0 && e.TerrainTriangleCount > 0 &&
                w.TerrainMeshObjectId != 0 && e.TerrainMeshObjectId != 0 &&
                renderer.GetSnapshot().ActiveStaticBatchGroups >= 4,
                "Both Pack seam chunks and their static/Multi groups were not natively realized.");
            var westVertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
            var eastVertices = new TerrainBaseMeshVertex[westVertices.Length];
            var westIndices = new ushort[TerrainChunkFormat.TriangleIndexCount];
            var eastIndices = new ushort[westIndices.Length];
            var westMaterials = new ushort[TerrainChunkFormat.TriangleCount];
            var eastMaterials = new ushort[westMaterials.Length];
            Expect(cache.TryBuildResidentTerrainMesh(west, westVertices, westIndices, westMaterials,
                    out _, out _, out _) &&
                cache.TryBuildResidentTerrainMesh(east, eastVertices, eastIndices, eastMaterials,
                    out _, out _, out _), "The native seam mesh source could not build.");
            for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
            {
                int wi = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(32, vertex));
                int ei = TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(0, vertex));
                Expect(westVertices[wi].AbsoluteYMillimeters == eastVertices[ei].AbsoluteYMillimeters &&
                    westVertices[wi].LocalXMillimeters == 16_000 &&
                    eastVertices[ei].LocalXMillimeters == 0 &&
                    westVertices[wi].LocalZMillimeters == eastVertices[ei].LocalZMillimeters,
                    "Native seam mesh vertices disagree in authoritative world position.");
            }
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
