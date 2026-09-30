using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.Composition;
using UltraSim.Game.World;

/// <summary>Native, bounded actor-only controls; not product CCU or the static F1.9B fixture.</summary>
public partial class F110ActorTierAcceptance : Node3D
{
    private static bool Native => DisplayServer.GetName() != "headless";
    private readonly List<object> _rows = [];
    private static WorldPosition Origin => AtPosition(0);

    public override async void _Ready()
    {
        try
        {
            GodotLifetimeOwnershipTests.Run(this);
            await GodotLifetimeOwnershipTests.AudioGenerationReplacement(this);
            await Assurance();
            foreach (int count in Native ? new[] { 16, 64, 128, 256 } : new[] { 16 })
                for (int repetition = 0; repetition < (Native ? 3 : 1); repetition++)
                    for (int order = 0; order < 2; order++)
                        await Measure(count, (order + repetition) % 2 == 1, repetition);
            string? output = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--evidence="));
            if (output is not null)
                File.WriteAllText(output[11..], JsonSerializer.Serialize(new
                {
                    checkpoint = "F1.10", sourceHead = System.Environment.GetEnvironmentVariable("ULTRASIM_F110_TESTED_HEAD"),
                    frequency = Stopwatch.Frequency, nativeGeometry = Native,
                    jitOptimizerDisabled = typeof(F110ActorTierAcceptance).Assembly
                        .GetCustomAttributes(typeof(DebuggableAttribute), false).Cast<DebuggableAttribute>()
                        .Any(x => x.IsJITOptimizerDisabled),
                    policy = ClientActorRepresentationPolicy.ConceptDemo, rows = _rows,
                }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"F1.10 ACTOR TIER ACCEPTANCE PASS nativeGeometry={Native} identity=exact retirement=clean cpuInterpolation=unchanged");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Assurance()
    {
        var host = new Node3D(); AddChild(host);
        var resolver = new VersionedResolver();
        using var presenter = new GodotActorPresenter(host, ClientActorDefinitionCatalog.ConceptDemo, 64, resolver);
        var records = Records(32);
        presenter.SetControlledActorPose(records[0].Id, Origin, WorldYaw.Zero, 1000);
        presenter.Apply(Changes(records), Origin, 1000);
        Expect(presenter.GetSnapshot() is { DetailedActors: 2, BatchedPrimitiveActors: 30 }, "Initial policy/cohort mismatch.");
        AssertBodies(presenter, records);
        WorldPublicEntityId id = records[2].Id;
        Expect(!presenter.TryGetActorRoot(id, out _) && presenter.TryGetPresentedPose(id, out _), "Pose required a root.");
        ClientMotionPose near = new(18_000, 1_000, 0, WorldYaw.QuarterTurn);
        presenter.SetPresentedPose(id, near); presenter.UpdateOriginAndLifetimes(Origin, 1100);
        Expect(presenter.TryGetActorRoot(id, out var root) && root is not null,
            "Promotion failed.");
        Expect(root!.Position.IsEqualApprox(new(18, 0, 0)), "Promotion snapped to authoritative position.");
        await Boundary(); AssertBodies(presenter, records);
        ArrayMesh[] retiredActorMeshes = GodotLifetimeOwnershipTests.CaptureActorTools(root!);
        ClientMotionPose far = near with { GlobalXMillimeters = 32_000 };
        presenter.SetPresentedPose(id, far); presenter.UpdateOriginAndLifetimes(Origin, 1200);
        await Boundary(); AssertBodies(presenter, records);
        Expect(!GodotObject.IsInstanceValid(root) && presenter.TryGetPresentedPose(id, out var pose) && pose == far,
            "Demotion kept a body or lost the presented pose.");
        GodotLifetimeOwnershipTests.RequireRetired(retiredActorMeshes, "actor demotion");
        presenter.SetPresentedPose(id, far with { GlobalXMillimeters = 24_000 });
        presenter.UpdateOriginAndLifetimes(Origin, 1300);
        Expect(IsCheap(presenter, id), "Hysteresis oscillated in its dead band.");

        // Reveal is overlay ownership, not body promotion; release removes cheap labels.
        int cheap = presenter.GetSnapshot().BatchedPrimitiveActors;
        presenter.SetRevealHeld(true); await Boundary();
        Expect(presenter.GetSnapshot().VisibleNameplates == records.Length &&
            presenter.GetSnapshot().BatchedPrimitiveActors == cheap, "Reveal promoted bodies/lost labels.");
        presenter.SetRevealHeld(false);
        Expect(presenter.GetSnapshot().VisibleNameplates == 1, "Controlled label grace changed.");

        var speech = new ClientChatOccurrence(7, 1, 1, 1, 1, ClientChatOccurrenceKind.SpatialSpeech,
            ClientChatSpeechMode.Say, id, "Actor", "Retained across threshold");
        Expect(presenter.TryPresentOverhead(speech, 7, 2000), "Overhead could not promote a cheap actor.");
        presenter.SetPresentedPose(id, far with { GlobalXMillimeters = 90_000 });
        presenter.UpdateOriginAndLifetimes(Origin, 1500); await Boundary();
        Expect(presenter.GetSnapshot().VisibleOverheadLabels == 1 && !IsCheap(presenter, id),
            "Active overhead expired on a threshold crossing.");
        presenter.UpdateOriginAndLifetimes(Origin, 2100);
        Expect(IsCheap(presenter, id) && presenter.GetSnapshot().VisibleOverheadLabels == 0, "Overhead pin did not retire.");

        var equipped = records[2] with { MainHandPrefabId = GameWorldPublicPrefabIds.IronSword, PublicVersion = 2 };
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, id, equipped)], Origin, 2200);
        Expect(!IsCheap(presenter, id) && presenter.GetSnapshot().EquippedMainHands == 1,
            "Equipment lost on promotion.");
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, id, equipped with { MainHandPrefabId = 0,
            Action = ClientPublicActorActionKind.Primary, ActionSequence = 9, PublicVersion = 3 })], Origin, 2300);
        Expect(presenter.GetSnapshot().PrimaryCuesPresented == 1, "Accepted action was lost.");
        presenter.UpdateOriginAndLifetimes(Origin, 2600);
        Expect(IsCheap(presenter, id), "Completed action pinned forever.");
        presenter.SetPresentedPose(id, near); presenter.UpdateOriginAndLifetimes(Origin, 2700);
        Expect(presenter.GetSnapshot().PrimaryCuesPresented == 1, "Transition replayed an old action.");
        presenter.SetPresentedPose(id, far); presenter.UpdateOriginAndLifetimes(Origin, 2800);
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, id, records[2] with {
            Action = ClientPublicActorActionKind.Interact, ActionSequence = 10, PublicVersion = 4 })], Origin, 2900);
        Expect(presenter.GetSnapshot().InteractCuesPresented == 1 && !IsCheap(presenter, id), "Interact was lost.");
        presenter.UpdateOriginAndLifetimes(Origin, 3300);
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, id, records[2] with {
            Action = ClientPublicActorActionKind.Interact, ActionSequence = 10,
            VerticalPhase = ClientPublicActorVerticalPhase.Falling, PublicVersion = 5 })], Origin, 3400);
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, id, records[2] with {
            Action = ClientPublicActorActionKind.Interact, ActionSequence = 10,
            VerticalPhase = ClientPublicActorVerticalPhase.Grounded, PublicVersion = 6 })], Origin, 3500);
        Expect(presenter.GetSnapshot().LandingCuesPresented == 1 && !IsCheap(presenter, id), "Landing was lost.");
        presenter.UpdateOriginAndLifetimes(Origin, 3800);

        // Failed hidden preparation must not remove the old cheap body or publish a root.
        int committedChildren = presenter.Root.GetChildCount();
        resolver.Fail = true; presenter.SetPresentedPose(id, near);
        try { presenter.UpdateOriginAndLifetimes(Origin, 3900); throw new Exception("Injected capture did not throw."); }
        catch (InvalidDataException) { }
        Expect(IsCheap(presenter, id) && !presenter.TryGetActorRoot(id, out _), "Failed transition lost the old body.");
        Expect(presenter.Root.GetChildCount() == committedChildren, "Failed preparation left a hidden candidate.");
        resolver.Fail = false; resolver.Version = 2;
        presenter.UpdateOriginAndLifetimes(Origin, 4000);
        Expect(presenter.TryGetActorRoot(id, out root) && root!.GetNodeOrNull<Node3D>("Visual/Generation2") is not null,
            "Promotion used a retired content generation.");

        // Same interpolator survives both transitions; teleport uses the real F1.8A discontinuity.
        var interpolation = new ClientRemoteActorInterpolation();
        interpolation.Observe(1, id, 1, far, 1_000_000);
        interpolation.Observe(1, id, 2, far with { GlobalXMillimeters = 33_000 }, 1_100_000);
        Expect(interpolation.TrySampleTemporal(id, 1_150_000, out var temporal), "Missing real bracket.");
        presenter.SetPresentedPose(id, temporal.Resolved.Pose); presenter.UpdateOriginAndLifetimes(Origin, 4100);
        Expect(IsCheap(presenter, id) && presenter.TryGetPresentedPose(id, out pose) && pose == temporal.Resolved.Pose,
            "Cheap motion skipped F1.8A.");
        interpolation.Observe(1, id, 3, far with { YMillimeters = 11_000 }, 1_200_000);
        interpolation.TrySampleTemporal(id, 1_201_000, out temporal);
        Expect(temporal.Resolved.Provenance == ClientMotionPoseProvenance.DiscontinuitySnap, "Teleport policy changed.");
        presenter.SetPresentedPose(id, temporal.Resolved.Pose); presenter.UpdateOriginAndLifetimes(Origin, 4200);
        Expect(presenter.TryGetCheapActorDiagnostics(id, out var geometry) &&
            geometry.GroupBounds.End.Y >= 11, "Moving cheap bounds became stale.");

        // Same disclosed animal ray is nominated independent of tier/root and raw record position.
        WorldPublicEntityId animal = records[4].Id;
        var animalPose = new ClientMotionPose(35_000, 1_000, 0, WorldYaw.Zero);
        presenter.SetPresentedPose(animal, animalPose); presenter.UpdateOriginAndLifetimes(Origin, 4300);
        Expect(presenter.TryGetPresentedPose(animal, out pose) &&
            GodotPublicPointerPicker.TryIntersect(GameWorldPublicPrefabIds.WoodlandAnimal,
                new(35, .5f, -10), Vector3.Back, new(35, 0, 0), out _), "Cheap pick volume failed.");
        // Feature promotion holds EXACTLY the same pose, so the same semantic ray/ID remains valid.
        Expect(presenter.TryPresentPrimaryAction(animal, 1, 4300), "Animal promotion failed.");
        Expect(presenter.TryGetPresentedPose(animal, out var promotedPose) && promotedPose == pose, "Pick pose changed by tier.");

        if (Native)
        {
            presenter.UpdateOriginAndLifetimes(Origin, 4700);
            Expect(presenter.TryGetCheapActorDiagnostics(id, out geometry), "Missing cheap bounds control.");
            var camera = new Camera3D { Current = true, Position = new(35, 10, 15), Far = 300 };
            host.AddChild(camera); camera.LookAt(new(35, 4, 0));
            await Boundary();
            bool inside = RenderingServer.InstancesCullConvex(camera.GetFrustum(), host.GetWorld3D().Scenario)
                .Contains((long)geometry.NativeOwnerId);
            camera.LookAt(new(35, 10, 100)); await Boundary();
            bool outside = RenderingServer.InstancesCullConvex(camera.GetFrustum(), host.GetWorld3D().Scenario)
                .Contains((long)geometry.NativeOwnerId);
            Expect(inside && !outside, "Moving actor group frustum/culling failed."); camera.Free();
        }

        presenter.Apply([new(ClientActorPresentationChangeKind.Removed, id, default)], Origin, 4800);
        Expect(!presenter.SetPresentedPose(id, far) && !presenter.TryGetPresentedPose(id, out _), "Removed actor still presented.");
        var replacement = records[2] with { Id = new(id.Index, id.Generation + 1) };
        presenter.Apply([new(ClientActorPresentationChangeKind.Added, replacement.Id, replacement)], Origin, 4900);
        Expect(!presenter.TryGetCheapActorDiagnostics(id, out _) &&
            presenter.TryGetCheapActorDiagnostics(replacement.Id, out geometry) && geometry.Id == replacement.Id,
            "Swap removal/slot reuse kept stale identity.");
        foreach (ClientActorPresentationRecord actor in records.Where(x => x.Id != id))
            if (IsCheap(presenter, actor.Id))
                Expect(presenter.TryGetCheapActorDiagnostics(actor.Id, out var row) && row.Id == actor.Id,
                    "Swap-back mapping displaced another actor.");
        presenter.SetControlledActorPose(replacement.Id, Origin, WorldYaw.Zero, 5000);
        Expect(presenter.TryGetActorRoot(replacement.Id, out _), "Control transfer demoted/retained a cheap controlled body.");

        // A cheap family/scale replacement and floating-origin change preserve the CPU pose/ID.
        WorldPublicEntityId changing = records[5].Id;
        Expect(IsCheap(presenter, changing), "Family control must begin cheap.");
        presenter.TryGetPresentedPose(changing, out var retainedPose);
        ClientActorDefinitionCatalog.ConceptDemo.TryGet(GameWorldPublicPrefabIds.WoodlandAnimal, out var animalDefinition);
        var changed = records[5] with { PrefabId = GameWorldPublicPrefabIds.WoodlandAnimal,
            Definition = animalDefinition with { VisualScalePermille = 750 }, PublicVersion = 2 };
        presenter.Apply([new(ClientActorPresentationChangeKind.Updated, changing, changed)], Origin, 5100);
        var rebasedOrigin = AtPosition(16_000);
        presenter.UpdateOriginAndLifetimes(rebasedOrigin, 5200); await Boundary();
        Expect(presenter.TryGetPresentedPose(changing, out pose) && pose == retainedPose &&
            presenter.TryGetCheapActorDiagnostics(changing, out geometry) && geometry.Id == changing &&
            Mathf.IsEqualApprox(geometry.Transform.Origin.X,
                (retainedPose.GlobalXMillimeters - rebasedOrigin.GlobalXMillimeters) * .001f),
            "Family/scale/origin update lost pose or dense-row identity.");
        if (Native) Expect(geometry.NativeTransform.IsEqualApprox(geometry.Transform), "Rebased native row was stale.");
        presenter.Apply([new(ClientActorPresentationChangeKind.Removed, changing, default)], rebasedOrigin, 5300);
        Expect(!presenter.TryGetActorRoot(changing, out _) && !presenter.TryGetCheapActorDiagnostics(changing, out _),
            "Interest exit kept native ownership.");
        presenter.Apply([new(ClientActorPresentationChangeKind.Added, changing, changed)], rebasedOrigin, 5400);
        presenter.UpdateOriginAndLifetimes(rebasedOrigin, 5400); await Boundary();
        Expect(presenter.TryGetActorRecord(changing, out var reentered) && reentered == changed,
            "Interest reentry changed semantic truth.");
        presenter.Dispose(); presenter.Dispose(); await Boundary();
        Expect(host.GetChildCount() == 0, "Mixed actor retirement left native ownership."); host.Free();
        GD.Print("F1.10 ASSURANCE PASS transitions=atomic pins=explicit reveal=independent actions=once stale-slots=rejected");
    }

    private async Task Measure(int count, bool tiered, int repetition)
    {
        var owner = new Control(); AddChild(owner);
        using var composition = new GodotClientUiComposition(owner,
            ClientDisplayResolver.Resolve(ClientDisplaySettings.Default));
        var host = new Node3D(); composition.GameplayViewport.AddChild(host);
        var camera = new Camera3D { Current = true, Position = new(30, 40, 45), Far = 200 };
        host.AddChild(camera); camera.LookAt(new(30, 0, 4));
        var records = Records(count); var changes = Changes(records);
        using var presenter = new GodotActorPresenter(host, ClientActorDefinitionCatalog.ConceptDemo, count,
            representationPolicy: tiered ? ClientActorRepresentationPolicy.ConceptDemo : ClientActorRepresentationPolicy.AllDetailed);
        presenter.SetControlledActorPose(records[0].Id, Origin, WorldYaw.Zero, 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        presenter.Apply(changes, Origin, 1000);
        long initial = Stopwatch.GetTimestamp() - started;
        long initialAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var snapshot = presenter.GetSnapshot(); AssertBodies(presenter, records);
        long[] idle = new long[128], moving = new long[256];
        for (int frame = 0; frame < 32; frame++) Move(presenter, records, frame, 5000);
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < idle.Length; frame++)
        { started = Stopwatch.GetTimestamp(); presenter.UpdateOriginAndLifetimes(Origin, 10_000); idle[frame] = Stopwatch.GetTimestamp() - started; }
        long idleAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < moving.Length; frame++)
        { started = Stopwatch.GetTimestamp(); Move(presenter, records, frame, 20_000); moving[frame] = Stopwatch.GetTimestamp() - started; }
        long movingAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Expect(idleAllocated == 0 && movingAllocated == 0, "Warmed actor update allocated.");
        started = Stopwatch.GetTimestamp(); presenter.SetRevealHeld(true);
        long reveal = Stopwatch.GetTimestamp() - started;
        var revealed = presenter.GetSnapshot();
        Expect(revealed.VisibleNameplates == count && revealed.BatchedPrimitiveActors == snapshot.BatchedPrimitiveActors,
            "Measured reveal changed body policy.");
        presenter.SetRevealHeld(false);
        var subject = records[2].Id;
        started = Stopwatch.GetTimestamp();
        presenter.SetPresentedPose(subject, new(18_000, 1000, 0, WorldYaw.Zero));
        presenter.UpdateOriginAndLifetimes(Origin, 30_000);
        presenter.SetPresentedPose(subject, new(32_000, 1000, 0, WorldYaw.Zero));
        presenter.UpdateOriginAndLifetimes(Origin, 30_001);
        long transition = Stopwatch.GetTimestamp() - started;
        await Boundary(); AssertBodies(presenter, records);

        // Display input changes do not enter representation policy. Native ownership stays exact.
        var display = ClientDisplaySettings.Default;
        foreach (var settings in new[] { display, display.WithAdvancedFromSimple() with {
            AdvancedOutput = ClientPixelSize.Create(1280, 720) },
            display.WithAdvancedFromSimple() with { AdvancedInternalScalePercent = 50 } })
        {
            var before = presenter.GetSnapshot(); composition.ApplyDisplay(ClientDisplayResolver.Resolve(settings));
            await Boundary(); var after = presenter.GetSnapshot();
            Expect(before.DetailedActors == after.DetailedActors && before.BatchedPrimitiveActors == after.BatchedPrimitiveActors,
                "Pixel resolution changed actor tiers.");
        }
        double[] gpu = [], renderCpu = [];
        if (Native)
        {
            var viewport = composition.GameplayViewport.GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            for (int i = 0; i < 4; i++) await Boundary();
            gpu = new double[8]; renderCpu = new double[8];
            for (int i = 0; i < gpu.Length; i++)
            {
                await Boundary(); gpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
                renderCpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
            }
            RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
        }
        started = Stopwatch.GetTimestamp(); presenter.Dispose(); long retirement = Stopwatch.GetTimestamp() - started;
        double drawCalls = Native ? Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame) : 0;
        await Boundary(); Expect(host.GetChildCount() == 1, "Measurement retirement left actor body/label/group.");
        host.Free(); composition.Dispose(); owner.Free();
        _rows.Add(new { count, tiered, repetition, snapshot, revealed, initialTicks = initial, initialAllocated,
            idleTicks = idle, idleAllocated, movingTicks = moving, movingAllocated,
            revealTicks = reveal, roundTripTransitionTicks = transition, retirementTicks = retirement,
            gpuMs = gpu, viewportRenderCpuMs = renderCpu, observedGlobalDrawCalls = drawCalls });
        GD.Print($"F1.10 ROW count={count} tiered={tiered} detailed/cheap={snapshot.DetailedActors}/{snapshot.BatchedPrimitiveActors} " +
            $"bodyNodes={snapshot.NativeBodyNodes} moving_mean_us={moving.Average() * 1e6 / Stopwatch.Frequency:F3} alloc={movingAllocated}");
    }

    private static void Move(GodotActorPresenter presenter, ClientActorPresentationRecord[] records, int frame, ulong start)
    {
        for (int i = 1; i < records.Length; i++)
            presenter.SetPresentedPose(records[i].Id, ClientMotionPose.FromAuthority(records[i].Position,
                new WorldYaw((ushort)(frame % WorldYaw.UnitsPerTurn))) with {
                    GlobalXMillimeters = records[i].Position.GlobalXMillimeters + frame % 100 * 3 });
        presenter.UpdateOriginAndLifetimes(Origin, start + (ulong)frame * 16);
    }

    private static ClientActorPresentationRecord[] Records(int count)
    {
        var result = new ClientActorPresentationRecord[count];
        uint[] prefabs = [GameWorldPublicPrefabIds.Character, GameWorldPublicPrefabIds.CharacterMale,
            GameWorldPublicPrefabIds.WoodlandAnimal];
        for (int i = 0; i < count; i++)
        {
            // First two near; remaining same bounded far region in all comparisons.
            uint prefab = prefabs[(i + 1) % 3];
            ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefab, out var definition);
            result[i] = new(new((uint)i + 1, 1), prefab, AtPosition(i < 2 ? i * 1000 : 32_000 + i * 100,
                i < 2 ? 0 : i % 8 * 800), WorldYaw.Zero, definition, $"Actor {i}", true,
                VerticalPhase: ClientPublicActorVerticalPhase.Grounded, PublicVersion: 1);
        }
        return result;
    }

    private static ClientActorPresentationChange[] Changes(ClientActorPresentationRecord[] records) =>
        records.Select(x => new ClientActorPresentationChange(ClientActorPresentationChangeKind.Added, x.Id, x)).ToArray();
    private static WorldPosition AtPosition(long x, long z = 0)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(x, 1000, z, out var position)) throw new Exception("Invalid fixture position.");
        return position;
    }
    private static bool IsCheap(GodotActorPresenter presenter, WorldPublicEntityId id) =>
        presenter.TryGetRepresentationTier(id, out var tier) && tier == ClientActorRepresentationTier.BatchedPrimitive;
    private static void AssertBodies(GodotActorPresenter presenter, ClientActorPresentationRecord[] records)
    {
        var snapshot = presenter.GetSnapshot();
        Expect(snapshot.ActiveActors == records.Length && snapshot.DetailedActors + snapshot.BatchedPrimitiveActors == records.Length,
            "Body conservation failed.");
        foreach (var actor in records)
        {
            bool detailed = presenter.TryGetActorRoot(actor.Id, out var root);
            bool cheap = presenter.TryGetCheapActorDiagnostics(actor.Id, out var row);
            Expect(detailed != cheap && presenter.TryGetPresentedPose(actor.Id, out _), "Actor has zero/two representations.");
            if (detailed) Expect(root!.IsVisibleInTree(), "Detailed candidate remained hidden.");
            Expect(presenter.TryGetActorRecord(actor.Id, out var semantic) && semantic == actor,
                "Tier/temporal selection mutated exact semantic records.");
            if (cheap)
            {
                Expect(row.Id == actor.Id && row.Row < row.VisibleRows && row.GroupBounds.Size.LengthSquared() > 0,
                    "Cheap identity/bounds are stale.");
                if (Native) Expect(row.Transform.IsEqualApprox(row.NativeTransform), "Native cheap row did not match retained geometry.");
            }
        }
    }
    private async Task Boundary()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (Native) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private static void Expect(bool truth, string message) { if (!truth) throw new InvalidOperationException(message); }
    private sealed class VersionedResolver : IGodotActorVisualResolver
    {
        internal bool Fail; internal int Version = 1;
        public Node3D? Instantiate(in ClientActorDefinition definition)
        {
            if (Fail) throw new InvalidDataException("Controlled visual preparation failure.");
            var node = new Node3D(); node.AddChild(new Node3D { Name = $"Generation{Version}" }); return node;
        }
    }
}
