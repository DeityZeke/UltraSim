using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

/// <summary>Same semantic input through the maintained window/renderer/output path; no Game tuning.</summary>
public partial class F19bWorldBackendAcceptance : Node3D
{
    private static readonly SpatialSpaceId Space = new(919);
    private static readonly WorldContentFingerprint Content = new(919, 20260925);
    private readonly List<object> _rows = [];
    private readonly List<object> _submission = [];
    private static bool NativeGeometry => DisplayServer.GetName() != "headless";
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif
    private static readonly (GodotWorldInstanceBackend Backend, GodotWorldInstanceUpload Upload)[] Modes =
    [
        (GodotWorldInstanceBackend.NodeMultiMesh, GodotWorldInstanceUpload.PerRow),
        (GodotWorldInstanceBackend.DirectRid, GodotWorldInstanceUpload.PerRow),
        (GodotWorldInstanceBackend.DirectRid, GodotWorldInstanceUpload.Bulk),
        (GodotWorldInstanceBackend.NodeMultiMesh, GodotWorldInstanceUpload.Bulk),
    ];

    public override async void _Ready()
    {
        try
        {
            GD.Print($"F1.9B configuration={BuildConfiguration} assembly={typeof(F19bWorldBackendAcceptance).Assembly.Location}");
            if (NativeGeometry) MeasureSubmission();
            foreach (int scale in NativeGeometry ? new[] { 100, 1000, 5000, 10000 } : new[] { 100 })
            {
                Fixture fixture = CreateFixture(scale);
                Dictionary<string, Transform3D[]>? reference = null;
                ClientSemanticHash? hash = null;
                for (int repetition = 0; repetition < 3; repetition++)
                {
                    // Rotate order, do not run competing renderer campaigns concurrently.
                    for (int order = 0; order < Modes.Length; order++)
                    {
                        var mode = Modes[(order + repetition) % Modes.Length];
                        await RunFixture(fixture, mode.Backend, mode.Upload, repetition,
                            reference, hash, captured => { reference ??= captured.Transforms; hash ??= captured.Hash; });
                    }
                }
            }
            string? output = OS.GetCmdlineUserArgs().FirstOrDefault(
                static argument => argument.StartsWith("--evidence=", StringComparison.Ordinal));
            if (output is not null)
                File.WriteAllText(output["--evidence=".Length..], JsonSerializer.Serialize(new
                {
                    checkpoint = "F1.9B", fixture = "9 chunks; half frozen, quarter Multi pieces, remainder public",
                    configuration = BuildConfiguration, timestampFrequency = Stopwatch.Frequency,
                    jitOptimizerDisabled = typeof(F19bWorldBackendAcceptance).Assembly
                        .GetCustomAttributes(typeof(DebuggableAttribute), false).Cast<DebuggableAttribute>()
                        .Any(static attribute => attribute.IsJITOptimizerDisabled),
                    testedHead = System.Environment.GetEnvironmentVariable("ULTRASIM_F19B_TESTED_HEAD"),
                    assemblyLocation = typeof(F19bWorldBackendAcceptance).Assembly.Location,
                    repetitions = 3, nativeGeometryReadback = NativeGeometry,
                    gpuTiming = "separate eight-frame viewport diagnostic; zero means unavailable, not a measured zero cost",
                    submission = _submission, rows = _rows,
                }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"F1.9B BACKEND ACCEPTANCE PASS nativeGeometry={NativeGeometry} " +
                "backends=4 lifecycle=clean display=retained scenario=rebound");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F1.9B BACKEND ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task RunFixture(Fixture fixture, GodotWorldInstanceBackend backend,
        GodotWorldInstanceUpload upload, int repetition, Dictionary<string, Transform3D[]>? reference,
        ClientSemanticHash? referenceHash, Action<Capture> capture)
    {
        var cache = new ClientWorldSemanticCache(fixture.World,
            new(9, fixture.MaximumRows, fixture.MaximumRows, fixture.MaximumRows, fixture.PublicCount + 16));
        var handles = new ClientSemanticChunkHandle[9];
        for (int chunk = 0; chunk < 9; chunk++)
        {
            SpatialChunkKey key = Key(chunk);
            Expect(cache.TryRequest(key, out handles[chunk], out _) &&
                cache.TryStageFromBaseWorld(handles[chunk], fixture.Public[chunk],
                    new(fixture.Epoch, key, new(1)), out _) && cache.TryPublish(handles[chunk], out _),
                "Fixture semantic staging failed.");
        }
        ClientSemanticHash semantic = cache.GetSnapshot().WorldSemanticHash;
        Expect(referenceHash is null || referenceHash.Value == semantic, "Backend changed semantic hash.");
        var owner = new Control { Name = "FixtureUiOwner" };
        AddChild(owner);
        using var composition = new GodotClientUiComposition(owner,
            ClientDisplayResolver.Resolve(ClientDisplaySettings.Default));
        var host = new Node3D { Name = "BackendFixtureWorld" };
        composition.GameplayViewport.AddChild(host);
        var viewCamera = new Camera3D { Position = new(0, 45, 60), Far = 250, Current = true };
        host.AddChild(viewCamera); viewCamera.LookAt(Vector3.Zero);
        using var resolver = new FixtureResolver();
        // All four fallback availability results are exercised identically by every backend.
        using var renderer = new GodotWorldRenderer(host, cache, new(9, 16), resolver,
            instanceBackend: backend, instanceUpload: upload);
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        WorldPosition player = new(0, 0, 0, 0, 1000);
        Expect(window.TryTrackPlayer(player, changes, out _, out _, out _, out _), "Window entry failed.");
        long allocations = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        long buildTicks = 0, commitTicks = 0;
        for (int chunk = 0; chunk < 9; chunk++)
        {
            renderer.ProcessFrame(new(1, 0));
            GodotWorldRendererSnapshot tick = renderer.GetSnapshot();
            buildTicks += tick.LastMeshBuildTicks;
            commitTicks += tick.LastResourceCommitTicks;
        }
        long initialTicks = Stopwatch.GetTimestamp() - started;
        long buildAllocated = GC.GetAllocatedBytesForCurrentThread() - allocations;
        await Frames();
        GodotWorldRendererSnapshot initial = renderer.GetSnapshot();
        Expect(initial.ActiveInstances == fixture.Count && initial.ActiveRenderChunks == 9,
            $"Wrong realized count {initial.ActiveInstances}/{fixture.Count}.");
        Expect(CountNodes(renderer.Root) == initial.ActiveNodes, "Maintained node census was not actual Nodes.");
        var transforms = new Dictionary<string, Transform3D[]>();
        var meshes = new HashSet<Rid>();
        var overrides = new HashSet<Rid>();
        for (int chunk = 0; chunk < 9; chunk++)
        {
            foreach (string name in GroupNames)
            {
                var rows = new Transform3D[fixture.MaximumRows];
                if (!renderer.TryGetInstanceGroup(handles[chunk], name, out var group, rows)) continue;
                Expect(group.Scenario == composition.GameplayViewport.FindWorld3D().Scenario,
                    "Direct group attached to a different World3D scenario.");
                rows = rows[..group.Count];
                string key = $"{chunk}/{name}";
                transforms.Add(key, rows);
                meshes.Add(group.MeshRid);
                if (group.MaterialOverrideRid.IsValid) overrides.Add(group.MaterialOverrideRid);
                if (NativeGeometry && reference is not null)
                {
                    Expect(reference.TryGetValue(key, out var previous) && previous.Length == rows.Length,
                        "Group membership changed between backends.");
                    for (int index = 0; index < rows.Length; index++)
                        Expect(rows[index].IsEqualApprox(previous![index]), "Native transform mismatch.");
                }
                if (NativeGeometry)
                {
                    foreach (Transform3D transform in rows)
                        Expect(group.Bounds.Encloses(transform * resolver.BoundsFor(name)), "Group AABB clips a row.");
                    CheckCanonicalStaticRows(cache, handles[chunk], name, rows);
                }
            }
        }
        capture(new(transforms, semantic));
        PickingParity(cache, handles, renderer);
        var gpuMs = new double[NativeGeometry ? 8 : 0];
        var renderCpuMs = new double[gpuMs.Length];
        double drawCalls = 0;
        if (NativeGeometry)
        {
            Rid viewport = composition.GameplayViewport.GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            // GPU query results lag submission; retain zero/unavailable results honestly.
            for (int warmup = 0; warmup < 4; warmup++) await Frames();
            for (int frame = 0; frame < gpuMs.Length; frame++)
            {
                await Frames();
                gpuMs[frame] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
                renderCpuMs[frame] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
            }
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
        }
        if (NativeGeometry) await Culling(renderer, handles[4], host);
        var publicRows = new Transform3D[fixture.MaximumRows];
        string publicName = $"Public_{GameWorldPublicPrefabIds.WorldChest}_Fallback_ModelAndTextureMissing";
        Expect(renderer.TryGetInstanceGroup(handles[4], publicName, out var beforeDisplay, publicRows),
            "Public group not found.");
        foreach (var display in new[]
        {
            ClientDisplaySettings.Default,
            ClientDisplaySettings.Default.WithAdvancedFromSimple() with { AdvancedOutput = Size(1280,720) },
            ClientDisplaySettings.Default.WithAdvancedFromSimple() with { AdvancedInternalScalePercent = 50 },
        })
        {
            composition.ApplyDisplay(ClientDisplayResolver.Resolve(display));
            renderer.ProcessFrame(new(0, 0));
            await Frames();
            Expect(renderer.TryGetInstanceGroup(handles[4], publicName, out var afterDisplay, publicRows) &&
                beforeDisplay.VisualRid == afterDisplay.VisualRid && beforeDisplay.Scenario == afterDisplay.Scenario &&
                cache.GetSnapshot().WorldSemanticHash == semantic, "Display replaced/corrupted native ownership.");
        }
        // Actual SubViewport World3D replacement must rebind already-owned direct groups.
        using var replacementWorld = new World3D();
        composition.GameplayViewport.World3D = replacementWorld;
        renderer.ProcessFrame(new(0, 0));
        await Frames();
        Expect(renderer.TryGetInstanceGroup(handles[4], publicName, out var rebound, publicRows) &&
            rebound.Scenario == composition.GameplayViewport.FindWorld3D().Scenario,
            "Scenario replacement retained a dead scenario.");

        // Rebase only the visual group, never its local row data, including high/low anchors.
        long rebaseStart = Stopwatch.GetTimestamp();
        for (int index = 0; index < 256; index++)
            renderer.SetRenderOrigin(new(new(0, 0), index % 2 == 0 ? 1_250_000 : -1_250_000,
                (ushort)(index % 15000), (ushort)(index % 14000)));
        long rebaseTicks = Stopwatch.GetTimestamp() - rebaseStart;
        Expect(renderer.TryGetInstanceGroup(handles[4], publicName, out var rebased, publicRows), "Rebase lost group.");
        var expectedRows = transforms[$"4/{publicName}"];
        if (NativeGeometry)
        {
            for (int index = 0; index < rebased.Count; index++)
                Expect(publicRows[index].IsEqualApprox(expectedRows[index]), "Rebase rewrote local rows.");
            Expect(rebased.GlobalTransform.Origin.IsEqualApprox(new Vector3(-.255f, 1251, -.255f)),
                "High/low rebase did not reach the visual group.");
        }
        Expect(cache.GetSnapshot().WorldSemanticHash == semantic, "Rebase changed semantics.");
        renderer.SetRenderOrigin(new(new(0, 0), 1000));
        renderer.TrySetVisible(handles[4], false);
        renderer.TryGetInstanceGroup(handles[4], publicName, out var hidden, publicRows);
        Expect(!hidden.Visible, "Group visibility ignored chunk ownership.");
        renderer.TrySetVisible(handles[4], true);
        host.Position = new(3, 4, 5); renderer.ProcessFrame(new(0, 0));
        renderer.TryGetInstanceGroup(handles[4], publicName, out var movedOwner, publicRows);
        Expect(movedOwner.GlobalTransform.Origin.IsEqualApprox(new Vector3(3, 4, 5)),
            "Ancestor/root movement did not reach native realization.");
        host.Position = Vector3.Zero; renderer.ProcessFrame(new(0, 0));

        // Content owners invalidate by retiring/re-entering realization, not semantic records.
        renderer.TryGetInstanceGroup(handles[0], GroupNames[0], out var oldContent, publicRows);
        renderer.TryQueueRetire(handles[0], out _); renderer.ProcessFrame(new(0, 1));
        resolver.Alternate = true;
        renderer.TryEnterPresentation(handles[0], true, out _); renderer.ProcessFrame(new(1, 0));
        renderer.TryGetInstanceGroup(handles[0], GroupNames[0], out var newContent, publicRows);
        Expect(oldContent.MeshRid != newContent.MeshRid && oldContent.VisualRid != newContent.VisualRid &&
            cache.GetSnapshot().WorldSemanticHash == semantic, "Replacement pinned obsolete native geometry or changed truth.");

        // Measure the existing exact whole-public-layer refresh, no speculative dynamic slot map.
        ulong revision = 1;
        long refreshTicks = 0, refreshAllocated = 0;
        var refreshSamples = new List<object>();
        foreach (int dirty in new[] { 1, Math.Min(4, fixture.Public[4].Length), fixture.Public[4].Length })
        {
            WorldPublicRecord[] records = (WorldPublicRecord[])fixture.Public[4].Clone();
            revision++;
            for (int index = 0; index < dirty; index++)
            {
                var row = records[index];
                records[index] = row with { EntityPublicVersion = revision,
                    Position = new(row.Position.Chunk.X, row.Position.Chunk.Z, row.Position.Local.XMillimeters,
                        row.Position.Local.ZMillimeters, row.Position.YMillimeters + 500),
                    LastChangedRevision = new(revision) };
            }
            Expect(cache.TryApplySnapshot(handles[4], new(ChunkPublicationReadStatus.Snapshot, Key(4),
                new(fixture.Epoch, Key(4), new(revision)), default, records.Length, records.Length), records, out _),
                "Public update did not apply.");
            renderer.TryQueuePublicRefresh(handles[4], out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            renderer.ProcessFrame(new(1, 0));
            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            refreshTicks += elapsed;
            refreshAllocated += allocatedBytes;
            refreshSamples.Add(new { dirty, rebuiltRecords = records.Length, ticks = elapsed,
                microseconds = Us(elapsed), allocatedBytes });
            if (NativeGeometry)
            {
                renderer.TryGetInstanceGroup(handles[4], publicName, out var refreshed, publicRows);
                for (int row = 0; row < refreshed.Count; row++)
                    Expect(refreshed.Bounds.Encloses(publicRows[row] * resolver.BoundsFor(publicName)), "Refresh AABB clips new position.");
            }
        }
        // Empty replacement, then fresh lifetime. No native slot may nominate retired identity.
        revision++;
        Expect(cache.TryApplySnapshot(handles[4], new(ChunkPublicationReadStatus.Snapshot, Key(4),
            new(fixture.Epoch, Key(4), new(revision)), default, 0, 0), [], out _), "Removal failed.");
        renderer.TryQueuePublicRefresh(handles[4], out _); renderer.ProcessFrame(new(1, 0));
        Expect(!renderer.TryGetInstanceGroup(handles[4], publicName, out _, publicRows), "Empty group retained native identity.");
        revision++;
        var fresh = fixture.Public[4].Select(row => row with { Id = new(row.Id.Index, 2),
            EntityPublicVersion = revision, LastChangedRevision = new(revision) }).ToArray();
        Expect(cache.TryApplySnapshot(handles[4], new(ChunkPublicationReadStatus.Snapshot, Key(4),
            new(fixture.Epoch, Key(4), new(revision)), default, fresh.Length, fresh.Length), fresh, out _), "Slot reuse failed.");
        renderer.TryQueuePublicRefresh(handles[4], out _); renderer.ProcessFrame(new(1, 0));
        PickingParity(cache, handles, renderer);
        for (int index = 0; index < 32; index++) renderer.ProcessFrame(new(0, 0));
        allocations = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
        for (int index = 0; index < 512; index++) renderer.ProcessFrame(new(0, 0));
        long steadyTicks = Stopwatch.GetTimestamp() - started;
        long steadyAllocated = GC.GetAllocatedBytesForCurrentThread() - allocations;
        Expect(steadyAllocated == 0, $"Warmed backend allocated {steadyAllocated} bytes.");
        // Explicit cancellation before commit; disposal with an active/deferred cohort.
        renderer.TryQueueRetire(handles[0], out _); renderer.ProcessFrame(new(0, 1));
        renderer.TryEnterPresentation(handles[0], true, out _);
        renderer.TryQueueRetire(handles[0], out _); renderer.ProcessFrame(new(1, 1));
        Expect(!renderer.IsRendered(handles[0]), "Cancelled build produced an orphan group.");
        renderer.TryEnterPresentation(handles[0], true, out _); renderer.ProcessFrame(new(1, 0));
        foreach (var handle in handles) renderer.TryQueueRetire(handle, out _);
        started = Stopwatch.GetTimestamp(); renderer.ProcessFrame(new(0, 9));
        long retireTicks = Stopwatch.GetTimestamp() - started;
        await Frames();
        Expect(renderer.GetSnapshot() is { ActiveInstances: 0, ActiveMultiMeshes: 0, ActiveVisualInstanceRids: 0,
            ActiveResources: 0, ActiveNodes: 1 }, "Group/resource retirement leaked.");
        renderer.TryEnterPresentation(handles[0], true, out _); renderer.ProcessFrame(new(1, 0));
        renderer.TryEnterPresentation(handles[1], true, out _); renderer.TryQueueRetire(handles[0], out _);
        renderer.Dispose(); renderer.Dispose();
        await Frames();
        Expect(renderer.GetSnapshot() is { ActiveNodes: 0, ActiveInstances: 0, PendingBuilds: 0, PendingRetirements: 0 },
            "Dispose with pending work leaked ownership.");
        _rows.Add(new
        {
            scale = fixture.Count, frozen = fixture.StaticCount, multi = fixture.MultiCount, publicObjects = fixture.PublicCount,
            backend = backend.ToString(), upload = upload.ToString(), repetition,
            semanticHash = $"{semantic.Low:x16}{semantic.High:x16}", groups = initial.ActiveMultiMeshes,
            nodes = initial.ActiveNodes, visualInstances = initial.ActiveVisualInstanceRids,
            resources = initial.ActiveResources, multimeshes = initial.ActiveMultiMeshes,
            instances = initial.ActiveInstances, buildUs = Us(buildTicks), commitUs = Us(commitTicks),
            distinctGroupMeshes = meshes.Count, distinctGroupMaterialOverrides = overrides.Count,
            terrainMeshes = initial.ActiveTerrainSurfaceMeshes,
            gpuMs, renderCpuMs, drawCalls,
            initialUs = Us(initialTicks), buildAllocated, groupingUs = Us(initial.GroupingTicks),
            groupPreparationUs = Us(initial.GroupPreparationTicks), refreshTotalUs = Us(refreshTicks), refreshAllocated, refreshSamples,
            steadyNsPerFrame = Ns(steadyTicks) / 512, steadyAllocated,
            rebaseNsPerCall = Ns(rebaseTicks) / 256, retireUs = Us(retireTicks),
            bulkUploads = initial.BulkUploads, targetedRows = initial.TargetedInstanceUpdates,
            uploadedRows = initial.UploadedInstanceRows, bulkBytes = initial.BulkUploadBytes,
            uploadScratchBytes = initial.UploadScratchBytes, semanticReservedBytes = cache.GetSnapshot().ReservedBytes,
        });
        GD.Print($"F1.9B ROW scale={fixture.Count} backend={backend}/{upload} rep={repetition} " +
            $"nodes={initial.ActiveNodes} groups={initial.ActiveMultiMeshes} initial_us={Us(initialTicks):F1} " +
            $"rebase_ns={Ns(rebaseTicks)/256:F1} alloc={buildAllocated} steady_alloc={steadyAllocated}");
        host.Free();
        composition.Dispose();
        await Frames();
        owner.Free();
    }

    private void MeasureSubmission()
    {
        using var material = new StandardMaterial3D();
        using var mesh = GodotMeshGeometry.CreateGroundedBox(Vector3.One, material);
        foreach (int count in new[] { 1, 4, 16, 64, 256, 1024 })
        {
            using var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh, InstanceCount = count };
            var buffer = new float[count * 12];
            var rows = Enumerable.Range(0, count).Select(index => new Transform3D(
                new Basis(Vector3.Up, index * .17f).Scaled(new Vector3(1, 2, .5f)), new(index, 4, -index))).ToArray();
            for (int index = 0; index < count; index++)
                GodotWorldInstanceGroup.WriteTransform(rows[index], buffer.AsSpan(index * 12, 12));
            RenderingServer.MultimeshSetBuffer(multi.GetRid(), (ReadOnlySpan<float>)buffer);
            foreach (int dirty in new[] { 1, Math.Min(4, count), count }.Distinct())
            {
                for (int warmup = 0; warmup < 32; warmup++) { Upload(true); Upload(false); }
                for (int repetition = 0; repetition < 3; repetition++)
                foreach (bool bulk in new[] { false, true })
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int iteration = 0; iteration < 1000; iteration++) Upload(bulk);
                    long ticks = Stopwatch.GetTimestamp() - start;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    Expect(bytes == 0, "Warmed submission allocated.");
                    for (int index = 0; index < count; index++)
                        Expect(multi.GetInstanceTransform(index).IsEqualApprox(rows[index]),
                            $"Bulk layout is not native Transform3D parity count={count} dirty={dirty} bulk={bulk} row={index}: actual={multi.GetInstanceTransform(index)} expected={rows[index]}.");
                    _submission.Add(new { count, dirty, bulk, repetition, nsPerCall = Ns(ticks)/1000, allocated = bytes });
                }
                void Upload(bool bulk)
                {
                    if (bulk)
                    {
                        // Include encoder preparation; dirty edits preserve every other retained row.
                        for (int index = 0; index < dirty; index++)
                            GodotWorldInstanceGroup.WriteTransform(rows[index], buffer.AsSpan(index * 12, 12));
                        RenderingServer.MultimeshSetBuffer(multi.GetRid(), (ReadOnlySpan<float>)buffer);
                    }
                    else for (int index = 0; index < dirty; index++) multi.SetInstanceTransform(index, rows[index]);
                }
                // Fill unchanged rows before sparse tests as a real retained staging owner would.
            }
        }
    }

    private static void PickingParity(ClientWorldSemanticCache cache, ClientSemanticChunkHandle[] handles,
        GodotWorldRenderer renderer)
    {
        Expect(cache.TryGetResidentView(handles[4], out var view), "Picking semantic view missing.");
        var rows = new Transform3D[view.PublicRecords.Length];
        Expect(renderer.TryGetInstanceGroup(handles[4], $"Public_{GameWorldPublicPrefabIds.WorldChest}_Fallback_ModelAndTextureMissing",
            out var group, rows), "Pickable semantic objects lost batch.");
        int slot = 0;
        foreach (var target in view.PublicRecords)
        {
            if (target.PrefabId != GameWorldPublicPrefabIds.WorldChest) continue;
            Vector3 position = PublicTransform(target).Origin;
            if (NativeGeometry)
                Expect(rows[slot].IsEqualApprox(PublicTransform(target)), "Public row differs from canonical disclosed transform.");
            // Start inside this prop's stock volume to disambiguate crowded/overlapping rows.
            Vector3 origin = position + new Vector3(0, .4f, 0);
            WorldPublicEntityId nominee = default;
            float nearest = float.MaxValue;
            foreach (var record in view.PublicRecords)
            {
                if (!GodotPublicPointerPicker.IsPointerTarget(record.PrefabId, record.StateFlags)) continue;
                if (GodotPublicPointerPicker.TryIntersect(record.PrefabId, origin, Vector3.Up,
                    PublicTransform(record).Origin, out float distance) && distance < nearest)
                { nearest = distance; nominee = record.Id; }
            }
            // Test five disjoint early props; later fixture rows deliberately repeat geometry.
            if (slot < 5) Expect(nominee == target.Id, "Batch picking changed exact disclosed lifetime.");
            slot++;
        }
        Expect(slot == group.Count, "Native public membership differs from semantic membership.");
    }

    private static Transform3D PublicTransform(WorldPublicRecord record)
    {
        var primitive = ClientPrimitivePresentationCatalog.ForPublic(record.PrefabId);
        return new(new Basis(Vector3.Up, record.Orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn))
            .Scaled(new Vector3(primitive.WidthMillimeters, primitive.HeightMillimeters, primitive.DepthMillimeters) * .001f),
            new(record.Position.Local.XMillimeters * .001f, (record.Position.YMillimeters - 1000) * .001f,
                record.Position.Local.ZMillimeters * .001f));
    }

    private static void CheckCanonicalStaticRows(ClientWorldSemanticCache cache,
        ClientSemanticChunkHandle handle, string name, Transform3D[] actual)
    {
        if (!name.StartsWith("Static_", StringComparison.Ordinal)) return;
        cache.TryGetResidentView(handle, out var view);
        int slot = 0;
        foreach (var record in view.FrozenStatics)
            Check(record.RenderKey, record.Position, record.Orientation, record.Bounds);
        foreach (var record in view.MultiFragments)
            Check(record.RenderKey, record.Position, record.Orientation, record.Bounds);
        Expect(slot == actual.Length, "Structural membership differs from canonical input.");
        void Check(WorldStaticRenderKey key, WorldPosition position, WorldYaw yaw, WorldStaticBounds bounds)
        {
            if (!name.StartsWith($"Static_{key.DefinitionId.Value}_{key.Variant}_{(ushort)key.Flags}", StringComparison.Ordinal)) return;
            Vector3 origin = new(position.Local.XMillimeters * .001f, (position.YMillimeters - 1000) * .001f,
                position.Local.ZMillimeters * .001f);
            Transform3D expected;
            if (key.DefinitionId.Value <= 2)
                expected = new(new Basis(Vector3.Up, yaw.Units * (MathF.Tau / WorldYaw.UnitsPerTurn)), origin);
            else
                // Declared AABBs are orientation-inclusive, grounded .5×.75×.5m here.
                expected = new(Basis.Identity.Scaled(new(.5f, .75f, .5f)), origin);
            Expect(actual[slot++].IsEqualApprox(expected), "Structural native row differs from canonical geometry.");
        }
    }

    private async Task Culling(GodotWorldRenderer renderer, ClientSemanticChunkHandle handle, Node3D host)
    {
        var rows = new Transform3D[2048];
        Expect(renderer.TryGetInstanceGroup(handle, GroupNames[4], out var group, rows), "Culling group missing.");
        // Diagnostic owner ID only, never an entity identity or product picking path.
        ulong diagnosticId = GetInstanceId();
        RenderingServer.InstanceAttachObjectInstanceId(group.VisualRid, diagnosticId);
        var camera = new Camera3D { Position = new Vector3(8, 20, 30), Far = 200 };
        host.AddChild(camera);
        camera.LookAt(new Vector3(8, 0, 8));
        await Frames();
        Expect(RenderingServer.InstancesCullConvex(camera.GetFrustum(), group.Scenario).Contains((long)diagnosticId),
            "Native camera frustum incorrectly culled a visible group.");
        camera.Position = new Vector3(500, 20, 500); camera.LookAt(new Vector3(600, 20, 600));
        await Frames();
        Expect(!RenderingServer.InstancesCullConvex(camera.GetFrustum(), group.Scenario).Contains((long)diagnosticId),
            "Native group ignored camera/frustum movement.");
        camera.Free();
    }

    private static Fixture CreateFixture(int count)
    {
        int statics = count / 2, multis = count / 4, publics = count - statics - multis;
        var builder = new WorldPackBuilder(Space, Content,
            new(16 * 1024 * 1024, 9, 8, statics, 9, multis));
        var publicRows = new WorldPublicRecord[9][];
        int maximumRows = 0;
        for (int chunk = 0; chunk < 9; chunk++)
        {
            SpatialChunkKey key = Key(chunk);
            int baseHeight = chunk == 0 ? -1_000_000 : chunk == 8 ? 1_000_000 : 1000;
            // Adjacent terrain edges remain exact; high/low object elevations exercise local Y
            // without authoring an invalid discontinuity into the structural World.
            Expect(TerrainHeightPlaneBase.TryCreate(1000, out var height), "Height invalid.");
            Expect(builder.TryAddBaseTerrain(key, Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray(),
                height, new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount], out _), "Terrain failed.");
            int staticCount = Share(statics, chunk), multiCount = Share(multis, chunk), publicCount = Share(publics, chunk);
            maximumRows = Math.Max(maximumRows, Math.Max(staticCount + multiCount, publicCount));
            for (int row = 0; row < staticCount; row++)
            {
                var position = FixturePosition(key, row, baseHeight);
                Expect(builder.TryAddFrozenStatic(new(new((ulong)(chunk * 10000 + row + 1)), new((uint)(row % 4 + 1)),
                    position, new((ushort)(row * 4096 % 65536)), 0, WorldStaticFlags.Renderable,
                    Bounds(position)), out _), "Static failed.");
            }
            var pieces = new WorldMultiPieceSource[multiCount];
            for (int row = 0; row < multiCount; row++)
            {
                var position = FixturePosition(key, row + 7, baseHeight);
                pieces[row] = new(new((uint)(row + 1)), new((uint)(row % 2 + 1)), position,
                    WorldYaw.QuarterTurn, 0, WorldStaticFlags.Renderable, Bounds(position));
            }
            Expect(builder.TryAddStaticMulti(new((ulong)chunk + 1), FixturePosition(key, 0, baseHeight),
                WorldYaw.Zero, pieces, out _), "Multi failed.");
            publicRows[chunk] = new WorldPublicRecord[publicCount];
            for (int row = 0; row < publicCount; row++) publicRows[chunk][row] = new(
                new((uint)(chunk * 10000 + row + 1), 1), 1,
                row % 2 == 0 ? GameWorldPublicPrefabIds.WorldChest : GameWorldPublicPrefabIds.InformationalSign,
                FixturePosition(key, row, baseHeight), new((ushort)(row * 4096 % 65536)),
                WorldPublicStateFlags.Visible, new(1));
        }
        Expect(builder.TryBuild(out var artifact, out var buildReason), $"World build failed: {buildReason}.");
        Expect(artifact!.Open().TryBind(new(Content, [1], [1,2,3,4]), out var world, out _), "World bind failed.");
        return new(world!, PublicationEpoch.Create(), publicRows, count, statics, multis, publics, maximumRows);
    }

    private sealed class FixtureResolver : IGodotWorldAssetResolver, IDisposable
    {
        private readonly StandardMaterial3D _material = new() { AlbedoColor = Colors.CornflowerBlue };
        private readonly ArrayMesh _mesh;
        private readonly ArrayMesh _replacement;
        internal bool Alternate { get; set; }
        internal FixtureResolver()
        {
            _mesh = GodotMeshGeometry.CreateGroundedBox(Vector3.One, _material);
            _replacement = GodotMeshGeometry.CreateGroundedBox(new(1, 2, 1), _material);
        }
        public GodotWorldMeshResolution ResolveStaticMesh(WorldStaticRenderKey key) => key.DefinitionId.Value switch
        {
            1 => GodotWorldMeshResolution.Available(Alternate ? _replacement : _mesh),
            2 => GodotWorldMeshResolution.MissingTexture(_mesh),
            3 => GodotWorldMeshResolution.MissingModel(),
            _ => GodotWorldMeshResolution.MissingModelAndTexture(),
        };
        public GodotWorldMeshResolution ResolvePublicMesh(uint prefab) => GodotWorldMeshResolution.MissingModelAndTexture();
        public Material? ResolveTerrainMaterial(ushort id) => _material;
        internal Aabb BoundsFor(string name) => new(new(-.5f, 0, -.5f), Vector3.One);
        public void Dispose() { _mesh.Dispose(); _replacement.Dispose(); _material.Dispose(); }
    }
    private record Fixture(BoundWorldPack World, PublicationEpoch Epoch, WorldPublicRecord[][] Public,
        int Count, int StaticCount, int MultiCount, int PublicCount, int MaximumRows);
    private record Capture(Dictionary<string, Transform3D[]> Transforms, ClientSemanticHash Hash);
    private static readonly string[] GroupNames =
        ["Static_1_0_1", "Static_2_0_1_Fallback_TextureMissing", "Static_3_0_1_Fallback_ModelMissing",
         "Static_4_0_1_Fallback_ModelAndTextureMissing",
         $"Public_{GameWorldPublicPrefabIds.WorldChest}_Fallback_ModelAndTextureMissing",
         $"Public_{GameWorldPublicPrefabIds.InformationalSign}_Fallback_ModelAndTextureMissing"];
    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (NativeGeometry) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private static int Share(int total, int chunk) => total / 9 + (chunk < total % 9 ? 1 : 0);
    private static SpatialChunkKey Key(int chunk) => new(Space, chunk % 3 - 1, chunk / 3 - 1);
    private static WorldPosition FixturePosition(SpatialChunkKey key, int row, int height) => new(key.Coord.X, key.Coord.Z,
        (ushort)(500 + row % 16 * 900), (ushort)(500 + row / 16 % 16 * 900), height + row % 7 * 5);
    private static WorldStaticBounds Bounds(WorldPosition p) => new(p.GlobalXMillimeters - 250, p.YMillimeters,
        p.GlobalZMillimeters - 250, p.GlobalXMillimeters + 250, p.YMillimeters + 750, p.GlobalZMillimeters + 250);
    private static ClientPixelSize Size(int x, int y) => ClientPixelSize.Create(x,y);
    private static int CountNodes(Node node) { int count = 1; foreach (Node child in node.GetChildren()) count += CountNodes(child); return count; }
    private static double Us(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
    private static double Ns(long ticks) => ticks * 1_000_000_000d / Stopwatch.Frequency;
    private static void Expect(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
