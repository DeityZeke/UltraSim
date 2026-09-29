using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

/// <summary>Production residency/window/renderer/output consumers. Synthetic cardinality, not CCU.</summary>
public partial class F111RendererStressAcceptance : Node3D
{
    private static readonly SpatialSpaceId Space = new(111);
    private static readonly WorldContentFingerprint Content = new(111, 20260926);
    private static bool Native => DisplayServer.GetName() != "headless";
    private readonly List<object> _rows = [];

    public override async void _Ready()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ultrasim-f111-{Guid.NewGuid():N}");
        try
        {
            ContentCache.TryOpen(root, out var cache, out _);
            using (cache)
            {
                CabinContentOracle oracle = CabinContentOracle.Create();
                var local = oracle.CreateLocalPresentation(CabinContentVariant.Default);
                foreach (var package in local.Packages)
                    Expect(cache!.TryInstallPackage(ContentScopeKey.Local, package.Manifest, package.Payloads,
                        out _, out _), "Local palette install failed.");
                Expect(cache!.TryPublishCatalog(ContentBaseCatalog.Empty, local.Plan, true,
                    out var catalog, out _), "Local palette publication failed."); catalog!.Dispose();
                Parity(cache, local, oracle, root);
                string officialRoot = Path.Combine(Path.GetFullPath("../..", ProjectSettings.GlobalizePath("res://")),
                    "Client", "OfficialContent");
                Expect(OfficialLocalPresentation.TryOpenOrAcquire(cache, officialRoot, out var actorCatalog, out _),
                    "Stress consumer requires the removable Official actor package.");
                actorCatalog!.Dispose();
                foreach (int count in Native ? new[] { 1000, 5000, 10000 } : new[] { 100 })
                {
                    var fixture = CreateFixture(count);
                    foreach (bool vegetation in new[] { false, true })
                        await Measure(fixture, count == 1000 ? 64 : count == 5000 ? 128 : 256,
                            1, vegetation, false, cache, local, root);
                    if (count == (Native ? 10000 : 100))
                    {
                        await Measure(fixture, 256, 2, true, false, cache, local, root);
                        await Measure(fixture, 256, 3, true, false, cache, local, root);
                        await Measure(fixture, 256, 1, false, true, cache, local, root);
                    }
                }
                Expect(cache.GetSnapshot() is { LiveBlobPins: 0, LiveCatalogPins: 0 }, "Final generation pins leaked.");
            }
            string? output = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--evidence="));
            if (output is not null) File.WriteAllText(output[11..], JsonSerializer.Serialize(new
            {
                checkpoint = "F1.11",
                sourceHead = System.Environment.GetEnvironmentVariable("ULTRASIM_F111_TESTED_HEAD"),
                frequency = Stopwatch.Frequency,
                nativeGeometry = Native,
                policy = "NodeMultiMesh/PerRow; F1.10 ConceptDemo; vegetation opt-in/base-only",
                fixedSeed = GodotVegetation.WorldSeed,
                rows = _rows,
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("F1.11 RENDERER STRESS ACCEPTANCE PASS parity=shared lifecycle=clean vegetation=bounded");
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError($"F1.11 FAIL: {e}"); GetTree().Quit(1); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private void Parity(ContentCache cache, CabinLocalPresentationContent local, CabinContentOracle oracle, string root)
    {
        ContentScopeKey realm = ContentScopeKey.ForRealm(new ApplicationRealmId(111, 11));
        foreach (var package in local.Packages)
            Expect(cache.TryInstallPackage(realm, package.Manifest, package.Payloads, out _, out _), "Realm install failed.");
        Expect(ContentActivationPlan.TryCreate(realm, new(111, 1), ContentBaseCatalog.Empty.Fingerprint,
            local.Plan.Packages, out var plan, out _), "Realm plan failed.");
        Expect(cache.TryPublishCatalog(ContentBaseCatalog.Empty, plan!, true, out var realmCatalog, out _), "Realm publication failed.");
        using var remote = PresentationGeneration.CreateWithResources(null, null, null, realmCatalog, null);
        using var generation = OpenLocal(cache, local);
        Expect(generation.StableFingerprint == remote.StableFingerprint, "Scope changed resolved content identity.");
        using var store = new GodotDerivedResourceStore(root, GodotDerivedResourcePolicy.ProductionStandalone("f111-test"));
        using var localAssets = new GodotCatalogWorldAssetResolver(generation, store);
        using var remoteAssets = new GodotCatalogWorldAssetResolver(remote, store);
        Expect(localAssets.WallTextureHash == remoteAssets.WallTextureHash &&
            localAssets.WallDerivedKey == remoteAssets.WallDerivedKey && remoteAssets.WallTextureSource == PresentationSourceKind.Realm,
            "Matched Local/Realm native resource identity differs.");
        IGodotWorldAssetResolver localResolver = localAssets, remoteResolver = remoteAssets;
        var key = new WorldStaticRenderKey(new(GameWorldStaticDefinitionIds.CabinWall), 0, WorldStaticFlags.Renderable);
        Mesh localMesh = localResolver.ResolveStaticMesh(key).Mesh!;
        Mesh remoteMesh = remoteResolver.ResolveStaticMesh(key).Mesh!;
        Material localTerrain = localResolver.ResolveTerrainMaterial(GameTerrainMaterialIds.Grass)!;
        Material remoteTerrain = remoteResolver.ResolveTerrainMaterial(GameTerrainMaterialIds.Grass)!;
        StandardMaterial3D originalRoof = localAssets.DiagnosticRoofMaterial!;
        Expect(localMesh.GetAabb() == remoteMesh.GetAabb(), "Local/Realm mesh geometry differs.");
        foreach (ushort material in new ushort[] { 1, 2, 3, 4, 5 })
            Expect(((StandardMaterial3D)localResolver.ResolveTerrainMaterial(material)!).AlbedoColor ==
                ((StandardMaterial3D)remoteResolver.ResolveTerrainMaterial(material)!).AlbedoColor, "Terrain palette differs.");
        var user = oracle.CreateUserWallTexturePack();
        Expect(cache.TryInstallUserResourcePack(user.Manifest, user.Payloads, out _, out _), "User texture install failed.");
        Expect(cache.TrySetEnabledUserResourcePacks([new(user.Manifest.PackageId, user.Manifest.ManifestHash)], out _), "User selection failed.");
        using (var overridden = OpenLocal(cache, local, true))
        {
            Rid meshRid = localMesh.GetRid(); var before = localAssets.WallTextureHash;
            localAssets.Refresh(overridden, store);
            if (!ReferenceEquals(originalRoof, localAssets.DiagnosticRoofMaterial))
                Expect(originalRoof.NativeInstance == IntPtr.Zero,
                    "Replaced roof material wrapper was left to finalization.");
            Expect(localAssets.WallTextureSource == PresentationSourceKind.User && localAssets.WallTextureHash != before &&
                localResolver.ResolveStaticMesh(key).Mesh!.GetRid() == meshRid,
                "User refresh did not preserve unaffected native geometry.");
        }
        cache.TrySetEnabledUserResourcePacks([], out _);
        var userWood = oracle.CreateUserWoodTexturePack();
        Expect(cache.TryInstallUserResourcePack(userWood.Manifest, userWood.Payloads, out _, out _),
            "User wood texture install failed.");
        Expect(cache.TrySetEnabledUserResourcePacks([
            new(userWood.Manifest.PackageId, userWood.Manifest.ManifestHash)], out _),
            "User wood selection failed.");
        using (var overriddenWood = OpenLocal(cache, local, true))
        {
            StandardMaterial3D previousRoof = localAssets.DiagnosticRoofMaterial!;
            localAssets.Refresh(overriddenWood, store);
            Expect(!ReferenceEquals(previousRoof, localAssets.DiagnosticRoofMaterial) &&
                previousRoof.NativeInstance == IntPtr.Zero,
                "Wood refresh did not retire the replaced roof material wrapper.");
        }
        cache.TrySetEnabledUserResourcePacks([], out _);
        using var restored = OpenLocal(cache, local);
        StandardMaterial3D userRoof = localAssets.DiagnosticRoofMaterial!;
        localAssets.Refresh(restored, store);
        Expect(userRoof.NativeInstance == IntPtr.Zero,
            "User roof material wrapper survived restoration to world content.");
        Expect(localAssets.WallTextureHash == remoteAssets.WallTextureHash, "Exact disable restoration failed.");
        _rows.Add(new
        {
            kind = "parity",
            stableFingerprint = generation.StableFingerprint.ToString(),
            localTextureHash = localAssets.WallTextureHash.ToString(),
            remoteTextureHash = remoteAssets.WallTextureHash.ToString(),
            derived = store.GetSnapshot(),
            nativeMeshRetained = true,
            semanticActivationChanged = false
        });
        StandardMaterial3D currentRoof = localAssets.DiagnosticRoofMaterial!;
        localAssets.Dispose();
        remoteAssets.Dispose();
        Expect(localMesh.NativeInstance == IntPtr.Zero && remoteMesh.NativeInstance == IntPtr.Zero &&
            localTerrain.NativeInstance == IntPtr.Zero && remoteTerrain.NativeInstance == IntPtr.Zero &&
            currentRoof.NativeInstance == IntPtr.Zero,
            "Local/Realm world palette wrappers survived their explicit owner retirement.");
    }

    private async Task Measure(Fixture fixture, int actorCount, int radius, bool vegetation, bool primitive,
        ContentCache content, CabinLocalPresentationContent local, string root)
    {
        var cache = new ClientWorldSemanticCache(fixture.World,
            new(162, fixture.MaximumRows, fixture.MaximumRows, fixture.MaximumRows, fixture.PublicCount + 1024));
        var residency = new ClientStructuralResidency(cache, radius);
        var owner = new Control(); AddChild(owner);
        using var composition = new GodotClientUiComposition(owner, ClientDisplayResolver.Resolve(ClientDisplaySettings.Default));
        var host = new Node3D(); composition.GameplayViewport.AddChild(host);
        var camera = new Camera3D { Position = new(5, 35, 48), Far = 250, Current = true };
        host.AddChild(camera); camera.LookAt(new(0, 0, 0));
        using var environment = new GodotPresentationEnvironment(host);
        long resourceStart = Stopwatch.GetTimestamp();
        content.TryOpenCatalog(ContentBaseCatalog.Empty, local.ActivationKey, out var localResources, out _);
        ContentCatalogLease? localActors = null;
        if (!primitive) content.TryOpenCatalog(ContentBaseCatalog.Empty, OfficialLocalPresentation.ActivationKey, out localActors, out _);
        using var generation = PresentationGeneration.CreateWithResources(null, null, localActors, null, localResources);
        using var store = new GodotDerivedResourceStore(root, GodotDerivedResourcePolicy.ProductionStandalone("f111-stress"));
        using var assets = primitive ? null : new GodotCatalogWorldAssetResolver(generation, store);
        long resourcePreparationTicks = Stopwatch.GetTimestamp() - resourceStart;
        using var actorVisuals = primitive ? null : new GodotContentActorVisualResolver(generation);
        using var actors = new GodotActorPresenter(host, ClientActorDefinitionCatalog.ConceptDemo, actorCount, actorVisuals);
        using var renderer = new GodotWorldRenderer(host, cache, new(49, 16), assets,
            GodotWorldFallbackMaterialMode.Neutral, actors, vegetationPrototype: vegetation);
        var window = new GodotSlidingRenderWindow(cache, renderer, radius);
        var changes = new ClientPresentationChange[98];
        WorldPosition player = new(4, 4, 0, 0, 1640);
        void Track(int x)
        {
            var point = new WorldPosition(x, 4, 0, 0, 1640);
            Expect(residency.TryPrepare(new(Space, x, 4), false, out var plan, out _, out _), "Residency prepare failed.");
            Expect(window.TryTrackPlayer(point, new(Space, x, 4), changes, out _, out _, out _, out _), "Window track failed.");
            residency.Commit(plan);
            for (int pass = 0; pass < 64 && (renderer.GetSnapshot().PendingBuilds > 0 || renderer.GetSnapshot().PendingRetirements > 0); pass++)
                renderer.ProcessFrame(new(16, 16));
        }
        long started = Stopwatch.GetTimestamp(); Track(4);
        long initial = Stopwatch.GetTimestamp() - started;
        ClientSemanticHash structuralHash = cache.GetSnapshot().WorldSemanticHash;
        long publicStarted = Stopwatch.GetTimestamp();
        foreach (var pair in fixture.Public)
        {
            Expect(cache.TryGetResidentHandle(pair.Key, out var handle), "Public chunk not resident.");
            Expect(cache.TryApplySnapshot(handle, new(ChunkPublicationReadStatus.Snapshot,
                pair.Key, new(fixture.Epoch, pair.Key, new(1)), new(1), pair.Value.Length, pair.Value.Length), pair.Value, out _), "Public snapshot failed.");
            renderer.TryQueuePublicRefresh(handle, out _);
        }
        renderer.ProcessFrame(new(49, 49));
        long publicPreparationTicks = Stopwatch.GetTimestamp() - publicStarted;
        long actorStarted = Stopwatch.GetTimestamp();
        var records = ActorRecords(actorCount, player);
        actors.SetControlledActorPose(records[0].Id, player, WorldYaw.Zero, 1000);
        actors.Apply(records.Select(r => new ClientActorPresentationChange(ClientActorPresentationChangeKind.Added, r.Id, r)).ToArray(), player, 1000);
        long actorPreparationTicks = Stopwatch.GetTimestamp() - actorStarted;
        await Frames();
        var initialRenderer = renderer.GetSnapshot(); var initialActors = actors.GetSnapshot(); var grass = renderer.GetVegetationSnapshot();
        Expect(initialRenderer.ActiveInstances == fixture.Count, "World realization count changed across radius/output.");
        Expect(CountNodes(renderer.Root) == initialRenderer.ActiveNodes, "Native owned-node census differs.");
        Expect(initialActors.ActiveActors == actorCount && initialActors.DetailedActors >= 1 && initialActors.BatchedPrimitiveActors > 0,
            "Actual F1.10 policy was not exercised.");
        if (!primitive) Expect(initialActors.AnimatedActors >= 1 && actorVisuals!.GetSnapshot().RealizedResources >= 1,
            "Intended presentation silently used primitive actors.");
        if (assets is not null)
        {
            var userPack = CabinContentOracle.Create().CreateUserWallTexturePack();
            ClientSemanticHash unchanged = cache.GetSnapshot().WorldSemanticHash;
            ulong priorBuilds = renderer.GetSnapshot().CompletedBuilds;
            Expect(content.TrySetEnabledUserResourcePacks([new(userPack.Manifest.PackageId, userPack.Manifest.ManifestHash)], out _),
                "Crowd User palette selection failed.");
            using (var userGeneration = OpenLocal(content, local, true)) assets.Refresh(userGeneration, store);
            Expect(assets.WallTextureSource == PresentationSourceKind.User &&
                cache.GetSnapshot().WorldSemanticHash == unchanged && renderer.GetSnapshot().CompletedBuilds == priorBuilds &&
                actors.GetSnapshot().ActiveActors == actorCount, "World replacement reset unrelated live consumers.");
            content.TrySetEnabledUserResourcePacks([], out _);
            assets.Refresh(generation, store);
        }
        var patch = new ClientVegetationPatch[1024]; var again = new ClientVegetationPatch[1024];
        cache.TryGetResidentHandle(new(Space, 4, 4), out var center);
        int patchCount = ClientVegetationScatter.Prepare(cache, center, GodotVegetation.WorldSeed, patch);
        long cutTicks = 0;
        Expect(patchCount == 1023, "Intentional void was not excluded from scatter.");
        for (int i = 0; i < patchCount; i++)
            Expect(patch[i].AbsoluteYMillimeters == (int)Math.Round(1000 + (64_000 + patch[i].LocalXMillimeters) * .01,
                MidpointRounding.AwayFromZero), "Scatter root differs from independent sloped-plane height.");
        if (vegetation)
        {
            Expect(renderer.TryGetVegetation(center, out var batch), "Vegetation batch missing.");
            var transform = batch!.MultiMesh.GetInstanceTransform(0);
            renderer.TryGetChunkDiagnostics(center, out var chunkDiagnostic);
            if (Native) Expect(Mathf.IsEqualApprox(transform.Origin.Y,
                (patch[0].AbsoluteYMillimeters - chunkDiagnostic.YAnchorMillimeters) * .001f),
                "Native root used a chunk height instead of exact ground.");
            ClientSemanticHash before = cache.GetSnapshot().WorldSemanticHash;
            ulong builds = renderer.GetSnapshot().CompletedBuilds;
            long cutStarted = Stopwatch.GetTimestamp();
            Expect(batch.TrySetCut(0, .75f) && batch.CollapsedBlades == 6, "Targeted cut failed.");
            cutTicks = Stopwatch.GetTimestamp() - cutStarted;
            Expect(cache.GetSnapshot().WorldSemanticHash == before && renderer.GetSnapshot().CompletedBuilds == builds,
                "Debug cut mutated authority/structural state or rebuilt the world.");
            batch.TrySetCut(0, 0);
            if (Native)
            {
                ulong identity = batch.Node.GetInstanceId();
                Rid scenario = composition.GameplayViewport.FindWorld3D().Scenario;
                RenderingServer.InstanceAttachObjectInstanceId(batch.Node.GetInstance(), identity);
                await Frames();
                Expect(RenderingServer.InstancesCullConvex(camera.GetFrustum(), scenario).Contains((long)identity),
                    "Grass finite native AABB was culled out of its actual viewport.");
                camera.LookAt(new Vector3(1000, 35, 48)); await Frames();
                Expect(!RenderingServer.InstancesCullConvex(camera.GetFrustum(), scenario).Contains((long)identity),
                    "Grass remained in a disjoint frustum.");
                camera.LookAt(Vector3.Zero);
                Transform3D retained = batch.MultiMesh.GetInstanceTransform(0);
                renderer.SetRenderOrigin(new(new(5, 4), 1_250_000));
                Expect(batch.MultiMesh.GetInstanceTransform(0).IsEqualApprox(retained) &&
                    renderer.GetSnapshot().CompletedBuilds == builds,
                    "Horizontal/vertical rebase rebuilt vegetation or accumulated global floats in patch rows.");
                renderer.SetRenderOrigin(new(new(4, 4), 1640));
            }
        }
        // Warm owner CPU boundary excludes awaited frames, snapshots, reveal allocations and GPU diagnostics.
        var worldTicks = new long[256]; var actorTicks = new long[256];
        long worldAllocated = 0, actorAllocated = 0;
        void Service(int frame)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp(); renderer.ProcessFrame(new(16, 16)); worldTicks[frame % 256] = Stopwatch.GetTimestamp() - start;
            worldAllocated += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            allocationStart = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            for (int i = 1; i < records.Length; i++) actors.SetPresentedPose(records[i].Id,
                ClientMotionPose.FromAuthority(records[i].Position, WorldYaw.Zero) with { GlobalXMillimeters = records[i].Position.GlobalXMillimeters + frame % 100 * 3 });
            actors.UpdateOriginAndLifetimes(player, 2000 + (ulong)frame * 16); actorTicks[frame % 256] = Stopwatch.GetTimestamp() - start;
            actorAllocated += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        }
        for (int frame = 0; frame < 32; frame++) Service(frame);
        worldAllocated = actorAllocated = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 256; frame++) Service(frame);
        long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Expect(worldAllocated == 0 && frameAllocated == worldAllocated + actorAllocated &&
            actorAllocated <= 64 * 1024 && (!primitive || actorAllocated == 0),
            "Warmed allocation census is unreconciled or exceeded its finite control bound.");
        for (int frame = 0; frame < 32; frame++) actors.UpdateOriginAndLifetimes(player, 10000);
        var unchangedActorTicks = new long[256];
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 256; frame++)
        {
            long start = Stopwatch.GetTimestamp(); actors.UpdateOriginAndLifetimes(player, 10000);
            unchangedActorTicks[frame] = Stopwatch.GetTimestamp() - start;
        }
        long unchangedActorAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Expect(unchangedActorAllocated == 0, "Unchanged warmed actor cohort allocated.");
        int cheap = actors.GetSnapshot().BatchedPrimitiveActors;
        long revealStart = Stopwatch.GetTimestamp();
        long revealAllocated = GC.GetAllocatedBytesForCurrentThread();
        actors.SetRevealHeld(true);
        long revealTicks = Stopwatch.GetTimestamp() - revealStart;
        revealAllocated = GC.GetAllocatedBytesForCurrentThread() - revealAllocated;
        await Frames();
        Expect(actors.GetSnapshot().VisibleNameplates == actorCount && actors.GetSnapshot().BatchedPrimitiveActors == cheap, "Reveal changed body tiers.");
        var pixels = new List<object>();
        pixels.Add(new { view = "reveal-held", samples = await Gpu(composition) });
        actors.SetRevealHeld(false);
        foreach (var display in new[] { ClientDisplaySettings.Default,
            ClientDisplaySettings.Default.WithAdvancedFromSimple() with { AdvancedOutput = ClientPixelSize.Create(1280,720) },
            ClientDisplaySettings.Default.WithAdvancedFromSimple() with { AdvancedInternalScalePercent = 50 } })
        {
            composition.ApplyDisplay(ClientDisplayResolver.Resolve(display)); await Frames();
            Expect(renderer.GetSnapshot().ActiveInstances == initialRenderer.ActiveInstances && actors.GetSnapshot().ActiveActors == actorCount,
                "Pixel resolution changed semantic population.");
            foreach (bool shadows in new[] { false, true })
            {
                environment.Apply(GodotDiagnosticTimeOfDay.Noon, GodotDiagnosticAtmosphere.Temperate, shadows);
                pixels.Add(new { display, shadows, view = "diagnostic-top-down", samples = await Gpu(composition) });
            }
        }
        foreach (var rig in new[] { ClientStockCameraRigs.Orbit, ClientStockCameraRigs.ThirdPerson })
        {
            var realization = new GodotGameplayCameraRig(camera, rig, composition.Layout);
            pixels.Add(new
            {
                view = rig == ClientStockCameraRigs.Orbit ? "stock-orbit" : "stock-third-person",
                camera = realization.GetSnapshot(),
                samples = await Gpu(composition)
            });
        }
        camera.Position = new(0, 1.8f, 25); camera.LookAt(new(0, 1.2f, 0));
        pixels.Add(new { view = "near-horizontal", samples = await Gpu(composition) });
        if (vegetation)
        {
            renderer.SetVegetationShadows(true);
            pixels.Add(new { view = "near-horizontal-grass-shadows", samples = await Gpu(composition) });
            renderer.SetVegetationShadows(false);
        }
        var environments = new List<object>();
        foreach (var time in Enum.GetValues<GodotDiagnosticTimeOfDay>())
            foreach (var atmosphere in Enum.GetValues<GodotDiagnosticAtmosphere>())
            {
                environment.Apply(time, atmosphere, true);
                environments.Add(new { time, atmosphere, native = environment.GetSnapshot() });
                if (vegetation && radius == 3 && atmosphere == GodotDiagnosticAtmosphere.Temperate)
                    pixels.Add(new { view = "diagnostic-environment", time, samples = await Gpu(composition) });
            }
        cache.TryGetResidentHandle(new(Space, 4 - radius, 4), out var edge);
        int edgeCount = ClientVegetationScatter.Prepare(cache, edge, GodotVegetation.WorldSeed, patch);
        Rid priorEdge = default;
        if (vegetation && renderer.TryGetVegetation(edge, out var oldEdge)) priorEdge = oldEdge!.MultiMesh.GetRid();
        long churnStart = Stopwatch.GetTimestamp();
        for (int round = 0; round < 3; round++)
        {
            Track(5);
            if (vegetation) Expect(!renderer.TryGetVegetation(edge, out _), "Exited edge retained its grass batch.");
            Track(4);
            int newCount = ClientVegetationScatter.Prepare(cache, edge, GodotVegetation.WorldSeed, again);
            Expect(newCount == edgeCount && patch.AsSpan(0, edgeCount).SequenceEqual(again.AsSpan(0, newCount)),
                "Retired/re-entered edge changed deterministic scatter.");
            if (vegetation)
            {
                Expect(renderer.TryGetVegetation(edge, out var newEdge), "Re-entered edge lacks grass.");
                if (Native) Expect(newEdge!.MultiMesh.GetRid() != priorEdge, "Re-entry reused a retired native grass resource.");
                priorEdge = newEdge!.MultiMesh.GetRid();
            }
        }
        long churn = Stopwatch.GetTimestamp() - churnStart;
        patchCount = ClientVegetationScatter.Prepare(cache, center, GodotVegetation.WorldSeed, patch);
        int againCount = ClientVegetationScatter.Prepare(cache, center, GodotVegetation.WorldSeed, again);
        Expect(againCount == patchCount && patch.AsSpan(0, patchCount).SequenceEqual(again.AsSpan(0, againCount)), "Re-entry changed deterministic scatter.");
        Expect(renderer.GetSnapshot().ActiveNodes == initialRenderer.ActiveNodes && renderer.GetSnapshot().ActiveResources == initialRenderer.ActiveResources,
            "Native resource count drifted under churn.");
        long retiredStart = Stopwatch.GetTimestamp();
        actors.Apply(records.Select(r => new ClientActorPresentationChange(ClientActorPresentationChangeKind.Removed, r.Id, default)).ToArray(), player, 9000);
        Expect(actors.GetSnapshot().ActiveActors == 0, "Actor final drain did not retire exact lifetimes.");
        ArrayMesh[] retiredTerrainMeshes = GodotLifetimeOwnershipTests.CaptureTerrain(renderer.Root);
        renderer.Dispose(); actors.Dispose();
        GodotLifetimeOwnershipTests.RequireRetired(retiredTerrainMeshes, "renderer disposal");
        long retirement = Stopwatch.GetTimestamp() - retiredStart;
        Expect(renderer.GetSnapshot() is { ActiveNodes: 0, ActiveResources: 0 } && renderer.GetVegetationSnapshot().Patches == 0 &&
            host.GetChildCount() == 3, "Renderer/vegetation/actor retirement leaked.");
        _rows.Add(new
        {
            kind = "stress",
            fixture.Count,
            actorCount,
            radius,
            vegetation,
            primitive,
            initialTicks = initial,
            worldTicks,
            actorTicks,
            unchangedActorTicks,
            unchangedActorAllocated,
            frameAllocated,
            worldAllocated,
            actorAllocated,
            initialRenderer,
            initialActors,
            grass,
            structuralHash,
            residency = residency.GetSnapshot(),
            derived = store.GetSnapshot(),
            churnTicks = churn,
            finalRenderDiagnostics = renderer.GetSnapshot(),
            retirementTicks = retirement,
            revealTicks,
            revealAllocated,
            resourcePreparationTicks,
            publicPreparationTicks,
            actorPreparationTicks,
            cutTicks,
            pixels,
            environments,
            actorResources = actorVisuals?.GetSnapshot(),
            sourceResources = assets?.GetSnapshot(),
            reservedSemanticBytes = cache.GetSnapshot().ReservedBytes
        });
        GD.Print($"F1.11 ROW objects={fixture.Count} actors={actorCount} radius={radius} vegetation={vegetation} patches={grass.Patches} nodes={initialRenderer.ActiveNodes} alloc={frameAllocated}");
        environment.Dispose(); host.Free(); composition.Dispose(); owner.Free(); await Frames();
    }

    private async Task<object> Gpu(GodotClientUiComposition ui)
    {
        if (!Native) return new { available = false };
        Rid viewport = ui.GameplayViewport.GetViewportRid(); RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        for (int i = 0; i < 4; i++) await Frames();
        var gpu = new double[8]; var cpu = new double[8]; var draws = new long[8]; var vertices = new long[8];
        for (int i = 0; i < 8; i++)
        {
            await Frames(); gpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
            cpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
            draws[i] = RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
            vertices[i] = RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
        }
        RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
        return new { available = true, gpuMs = gpu, viewportRenderCpuMs = cpu, drawCalls = draws, primitives = vertices };
    }

    private static Fixture CreateFixture(int count)
    {
        int statics = count / 2, multis = count / 4, publics = count - statics - multis;
        var builder = new WorldPackBuilder(Space, Content, new(32 * 1024 * 1024, 121, 8, statics, 9, multis));
        var publicRows = new Dictionary<SpatialChunkKey, WorldPublicRecord[]>();
        for (int z = 0; z < 11; z++) for (int x = 0; x < 11; x++)
            {
                var key = new SpatialChunkKey(Space, x, z);
                TerrainHeightPlaneBase.TryCreate(1000 + x * 160, out var height);
                var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
                for (int v = 0; v < offsets.Length; v++) offsets[v] = new((ushort)(v % 33));
                var presence = Enumerable.Repeat(ulong.MaxValue, TerrainChunkFormat.SurfacePresenceWordCount).ToArray();
                if (x == 4 && z == 4) presence[0] &= ~1UL;
                Expect(builder.TryAddBaseTerrain(key, Enumerable.Repeat(GameTerrainMaterialIds.Grass, 1024).ToArray(), height, offsets, presence, out _), "Terrain authoring failed.");
                if (x == 4 && z == 4)
                {
                    TerrainHeightPlaneBase.TryCreate(2000, out var upper);
                    Expect(builder.TryAddAdditionalTerrainSurfaceBand(key, new(111), new(0, 0, 1, 1),
                        WorldTerrainSurfaceFaces.Top, [1UL], [TerrainSurfaceEdgeConnection.None],
                        [GameTerrainMaterialIds.Grass], upper, new TerrainHeightOffset[4], [], default, [], 0, out _),
                        "Upper grass-covered surface over base void authoring failed.");
                }
                if (x < 3 || x > 5 || z < 3 || z > 5) continue;
                int chunk = (z - 3) * 3 + x - 3;
                int a = Share(statics, chunk), b = Share(multis, chunk), c = Share(publics, chunk);
                for (int row = 0; row < a; row++)
                {
                    var p = FixturePosition(x, z, row); Expect(builder.TryAddFrozenStatic(new(new((ulong)(chunk * 10000 + row + 1)), new((uint)(row % 4 + 1)), p,
                    WorldYaw.Zero, 0, WorldStaticFlags.Renderable, Bounds(p)), out _), "Static authoring failed.");
                }
                var pieces = new WorldMultiPieceSource[b];
                for (int row = 0; row < b; row++) { var p = FixturePosition(x, z, row + 7); pieces[row] = new(new((uint)row + 1), new((uint)(row % 2 + 1)), p, WorldYaw.Zero, 0, WorldStaticFlags.Renderable, Bounds(p)); }
                Expect(builder.TryAddStaticMulti(new((ulong)chunk + 1), FixturePosition(x, z, 0), WorldYaw.Zero, pieces, out _), "Multi authoring failed.");
                publicRows[key] = Enumerable.Range(0, c).Select(row => new WorldPublicRecord(new((uint)(chunk * 10000 + row + 1), 1), 1,
                    row % 2 == 0 ? GameWorldPublicPrefabIds.WorldChest : GameWorldPublicPrefabIds.InformationalSign, FixturePosition(x, z, row), WorldYaw.Zero, WorldPublicStateFlags.Visible, new(1))).ToArray();
            }
        Expect(builder.TryBuild(out var artifact, out var error), $"World authoring failed: {error}");
        Expect(artifact!.Open().TryBind(new(Content, [3], [1, 2, 3, 4]), out var world, out _), "World binding failed.");
        return new(world!, PublicationEpoch.Create(), publicRows, count, publics, (count + 8) / 9);
    }
    private static ClientActorPresentationRecord[] ActorRecords(int count, WorldPosition player)
    {
        var rows = new ClientActorPresentationRecord[count]; uint[] prefabs = [GameWorldPublicPrefabIds.Character, GameWorldPublicPrefabIds.CharacterMale, GameWorldPublicPrefabIds.WoodlandAnimal];
        for (int i = 0; i < count; i++)
        {
            uint prefab = prefabs[i % 3]; ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefab, out var definition);
            WorldPosition.TryFromGlobalMillimeters(player.GlobalXMillimeters + (i < 2 ? i * 1000 : 32000 + i * 60), 1640, player.GlobalZMillimeters + i % 8 * 600, out var p);
            rows[i] = new(new((uint)i + 100000, 1), prefab, p, WorldYaw.Zero, definition, $"Actor {i}", true, VerticalPhase: ClientPublicActorVerticalPhase.Grounded, PublicVersion: 1);
        }
        return rows;
    }
    private static PresentationGeneration OpenLocal(ContentCache cache, CabinLocalPresentationContent local, bool user = false)
    {
        Expect(cache.TryOpenCatalog(ContentBaseCatalog.Empty, local.ActivationKey, out var catalog, out _), "Local generation reopen failed.");
        UserResourcePackStackLease? stack = null; if (user) cache.TryOpenEnabledUserResourcePacks(out stack, out _);
        return PresentationGeneration.CreateWithResources(stack, null, null, null, catalog);
    }
    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (Native) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private static int CountNodes(Node node) { int count = 1; foreach (Node child in node.GetChildren()) count += CountNodes(child); return count; }
    private static int Share(int total, int chunk) => total / 9 + (chunk < total % 9 ? 1 : 0);
    private static WorldPosition FixturePosition(int x, int z, int row) => new(x, z, (ushort)(500 + row % 16 * 900), (ushort)(500 + row / 16 % 16 * 900), 1000 + x * 160);
    private static WorldStaticBounds Bounds(WorldPosition p) => new(p.GlobalXMillimeters - 250, p.YMillimeters, p.GlobalZMillimeters - 250, p.GlobalXMillimeters + 250, p.YMillimeters + 750, p.GlobalZMillimeters + 250);
    private static void Expect(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private record Fixture(BoundWorldPack World, PublicationEpoch Epoch, Dictionary<SpatialChunkKey, WorldPublicRecord[]> Public, int Count, int PublicCount, int MaximumRows);
}
