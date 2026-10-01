using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Game.Composition;

namespace UltraSim.Client.Godot;

public abstract partial class LocalPlayClientRoot
{
    private async void RunF112NativeProduct()
    {
        var baseline = CaptureSessionBoundary();
        string directory = ReadLocalArgument("--f1-12-cabin-evidence=") ??
            throw new InvalidOperationException("Native product evidence directory required.");
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Actual native rendering required.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
            LocalCabinClient cabin = _cabin ?? throw new InvalidOperationException("Native product entry failed.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.PhysicsFrame);
            object pointer = await RunF112PointerSettingsAcceptance(() => cabin.RunF112PointerControl(checkInteraction: true));
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var tiers = new List<object>(3);
            foreach (bool far in new[] { false, true, false })
            {
                object tier = cabin.RunF112TierControl(far);
                object targets = await cabin.RunF112PointerControl();
                var visible = cabin.SetF112Reveal(true);
                await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
                object image = CaptureF112Output(System.IO.Path.Combine(directory, $"tier-{tiers.Count}.png"), _ => { });
                var hidden = cabin.SetF112Reveal(false);
                tiers.Add(new { tier, targets, image, visible, hidden });
            }
            cabin.SetPhysicsProcess(true);
            cabin.PrepareF112CentralControl();
            cabin.RunD00Acceptance(); // establish the existing authoritative pickup/equip prerequisite
            var equipment = cabin.GetPresentationSnapshot();
            cabin.RunF112ItemAcceptance(); // maintained equipped/action feature-required Detailed acceptance
            var finalItem = cabin.GetPresentationSnapshot();
            RetireCurrentSession(); ReturnToMainMenu("Native product control retired.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            RequireCleanSessionBoundary(in baseline, "F1.12-R1 native pointer/tier");
            File.WriteAllText(System.IO.Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
            { Schema = "client-f1.12-r1-native-product/v1", SourceHead = ReadLocalArgument("--f1-12-head="),
                Display = DisplayServer.GetName(), pointer, tiers, equipment, finalItem, FinalBoundary = CaptureSessionBoundary() },
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            GD.Print("F1.12-R1 NATIVE PRODUCT PASS pointer=4 tiers=3 reveal=exact retirement=clean");
            RequestApplicationExit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); RetireCurrentSession(); RequestApplicationExit(1); }
    }
    // Cold, explicit native assurance. Uses the shipping GameplayOutput, never a screenshot viewport.
    private async void RunF112RenderProbe()
    {
        string directory = ReadLocalArgument("--f1-12-cabin-evidence=") ?? "/tmp/ultrasim-f112-render-probe";
        System.IO.Directory.CreateDirectory(directory);
        void Stage(string value) => System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "stages.log"),
            $"{DateTime.UtcNow:O} {value}\n");
        int pre = 0, post = 0;
        void Pre() => pre++;
        void Post() => post++;
        RenderingServer.FramePreDraw += Pre;
        RenderingServer.FramePostDraw += Post;
        Node3D? scene = null;
        BoxMesh? probeMesh = null;
        StandardMaterial3D? probeMaterial = null;
        try
        {
            Stage($"request display={DisplayServer.GetName()} loop={RenderingServer.IsRenderLoopEnabled()} visible={GetWindow().Visible}");
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Native display required.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            Stage("process-frame");
            scene = new Node3D { Name = "F112KnownScene" };
            WorldRoot.AddChild(scene);
            var camera = new Camera3D { Position = new Vector3(0, 0, 4), Current = true };
            scene.AddChild(camera);
            probeMesh = new BoxMesh();
            probeMaterial = new StandardMaterial3D
            { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .2f, .05f) };
            var mesh = new MeshInstance3D { Mesh = probeMesh, MaterialOverride = probeMaterial };
            scene.AddChild(mesh);
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Stage($"physics-frame pre={pre} post={post} viewport={UiComposition.GameplayViewport.Size}");
            // Documented synchronous native rendering boundary, not a timing delay.
            Stage("force-draw-start");
            RenderingServer.ForceDraw();
            Stage($"force-draw-end pre={pre} post={post}");
            using Image image = UiComposition.GameplayTexture.GetImage();
            Stage($"readback empty={image.IsEmpty()} size={image.GetSize()}");
            if (image.IsEmpty() || image.SavePng(System.IO.Path.Combine(directory, "probe.png")) != Error.Ok)
                throw new InvalidOperationException("Native readback failed.");
            scene.Free();
            scene = null;
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            Stage("retired");
            RequestApplicationExit(0);
        }
        catch (Exception exception) { Stage(exception.ToString()); RequestApplicationExit(1); }
        finally
        {
            RenderingServer.FramePreDraw -= Pre;
            RenderingServer.FramePostDraw -= Post;
            if (scene is not null && GodotObject.IsInstanceValid(scene)) scene.Free();
            probeMesh?.Dispose();
            probeMaterial?.Dispose();
        }
    }

    private async void RunF112CabinPreview()
    {
        GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
        string directory = ReadLocalArgument("--f1-12-cabin-evidence=") ??
            throw new InvalidOperationException("Cabin preview needs an isolated output directory.");
        System.IO.Directory.CreateDirectory(directory);
        void Stage(string text) => System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "stages.log"),
            $"{DateTime.UtcNow:O} {text}\n");
        try
        {
            Stage("request");
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Cabin image evidence requires a real native renderer.");
            bool passiveReadyControl = OS.GetCmdlineUserArgs().Contains("--f1-12-passive-ready-control");
            bool p9Benchmark = OS.GetCmdlineUserArgs().Contains("--p9-two-room-benchmark");
            if (!passiveReadyControl)
            {
                await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
                Stage("initial-process-frame");
            }
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: false, useCabinVisualBenchmark: p9Benchmark);
            Stage("session-entered");
            LocalCabinClient cabin = _cabin!;
            if (passiveReadyControl) cabin.RunF02Acceptance(); // reproduce the original Ready-time ordering
            else if (p9Benchmark) cabin.PrepareP9CabinVisualBenchmarkCapture();
            else cabin.PrepareF112CabinCapture();
            Stage("acceptance-complete");
            if (passiveReadyControl)
            {
                _ = cabin.ApplyF112EnvironmentPreview(GodotDiagnosticTimeOfDay.Dawn, GodotDiagnosticAtmosphere.Temperate);
                for (int draw = 0; draw < 3; draw++)
                {
                    Stage($"passive-ready-post-draw-wait-{draw}");
                    Task original = OriginalDrawWait();
                    if (await Task.WhenAny(original, Task.Delay(TimeSpan.FromSeconds(8))) != original)
                        throw new TimeoutException($"Original FramePostDraw await {draw} stalled for 8 seconds.");
                    await original;
                    Stage($"passive-ready-post-draw-complete-{draw}");
                }
            }
            cabin.SetPhysicsProcess(false); // retain this exact semantic projection across lighting/camera previews
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            Stage("process-frame");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Stage("physics-frame");
            LocalCabinPresentationSnapshot before = cabin.GetPresentationSnapshot();
            string art = ReadLocalArgument("--f1-12-art=") ?? "installed";
            bool switchedToPrimitive = art == "installed";
            bool p9Extended = p9Benchmark ||
                OS.GetCmdlineUserArgs().Contains("--p9-cabin-extended-capture");
            if (p9Extended && before.PresentationGeneration.WorldMeshSelections != 11)
                throw new InvalidOperationException("P9 capture requires all eleven optional world meshes.");
            var captures = new List<object>(p9Extended ? 18 : 12);
            foreach (string view in p9Benchmark
                ? new[] { "Exterior", "Doorway", "RoomA", "InteriorDoorway", "RoomB", "Blocker", "WindowSide" }
                : new[] { "Exterior", "Doorway", "Interior", "Shelf" })
                await Capture(view, GodotDiagnosticTimeOfDay.Noon, GodotDiagnosticAtmosphere.Temperate);
            foreach (GodotDiagnosticTimeOfDay time in new[]
                { GodotDiagnosticTimeOfDay.Dawn, GodotDiagnosticTimeOfDay.Dusk, GodotDiagnosticTimeOfDay.Night })
                foreach (string view in p9Benchmark ? new[] { "RoomA", "RoomB" } : new[] { "Exterior" })
                    await Capture(view, time, GodotDiagnosticAtmosphere.Temperate);
            await Capture("Exterior", GodotDiagnosticTimeOfDay.Noon, GodotDiagnosticAtmosphere.Cold);
            if (p9Extended && !p9Benchmark)
            {
                await Capture("ExteriorHigh", GodotDiagnosticTimeOfDay.Noon,
                    GodotDiagnosticAtmosphere.Temperate);
                await Capture("ExteriorLow", GodotDiagnosticTimeOfDay.Noon,
                    GodotDiagnosticAtmosphere.Temperate);
                await Capture("WindowSide", GodotDiagnosticTimeOfDay.Noon,
                    GodotDiagnosticAtmosphere.Temperate);
                await Capture("ExteriorHigh", GodotDiagnosticTimeOfDay.Noon,
                    GodotDiagnosticAtmosphere.Temperate, GodotDiagnosticLightingMood.BrightClear);
                await Capture("Interior", GodotDiagnosticTimeOfDay.Noon,
                    GodotDiagnosticAtmosphere.Temperate, GodotDiagnosticLightingMood.DarkOppressive);
            }
            if (art == "installed")
            {
                cabin.UseF112PrimitivePreview();
                art = "primitive-same-session";
                foreach (string view in p9Benchmark
                    ? new[] { "Exterior", "RoomA", "InteriorDoorway", "RoomB", "Blocker", "WindowSide" }
                    : new[] { "Exterior", "Doorway", "Interior", "Shelf" })
                    await Capture(view, GodotDiagnosticTimeOfDay.Noon, GodotDiagnosticAtmosphere.Temperate);
            }
            LocalCabinPresentationSnapshot after = cabin.GetPresentationSnapshot();
            if (before.ApplicationSessionEpoch != after.ApplicationSessionEpoch ||
                before.ControlSessionEpoch != after.ControlSessionEpoch ||
                before.SemanticWorld.WorldSemanticHash != after.SemanticWorld.WorldSemanticHash)
                throw new InvalidOperationException("Environment preview replaced the authoritative session.");
            if (p9Benchmark && switchedToPrimitive &&
                (before.PresentationGeneration.WorldMeshSelections != 11 ||
                 after.PresentationGeneration.WorldMeshSelections != 0 ||
                 after.Renderer.ActiveInstances != before.Renderer.ActiveInstances + 3))
                throw new InvalidOperationException(
                    "Primitive did not replace the single painted window skin with four structural aperture pieces.");
            RetireCurrentSession();
            ReturnToMainMenu("F1.12 Cabin preview retired.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            RequireCleanSessionBoundary(in baseline, "F1.12 Cabin preview");
            Stage("retirement-clean");
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "evidence.json"),
                JsonSerializer.Serialize(new { Schema = p9Benchmark
                        ? "client-p9-two-room-benchmark/v1" : p9Extended
                            ? "client-p9-cabin-preview/v1" : "client-f1.12-cabin-preview/v2",
                    SourceHead = ReadLocalArgument("--f1-12-head="),
                    BenchmarkDefinition = p9Benchmark
                        ? GameCabinVisualBenchmarkWorldPack.DefinitionId : null,
                    Art = ReadLocalArgument("--f1-12-art=") ?? "installed", DisplayBackend = DisplayServer.GetName(),
                    before, after, captures,
                    P9PackageManifest = p9Extended
                        ? OfficialCabinPresentation.Manifest.ManifestHash.ToString() : null,
                    MissingAuthoredGeometry = p9Benchmark ? Array.Empty<string>() : p9Extended
                        ? new[] { "Secondary room", "Interior doorway" }
                        : new[] { "Secondary room", "Interior doorway", "Window" },
                    FinalBoundary = CaptureSessionBoundary(), HumanFeelRated = false },
                    new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            GD.Print($"F1.12 CABIN NATIVE PREVIEW PASS captures={captures.Count} retirement=clean");
            RequestApplicationExit(0);

            async Task Capture(string view, GodotDiagnosticTimeOfDay time,
                GodotDiagnosticAtmosphere atmosphere,
                GodotDiagnosticLightingMood mood = GodotDiagnosticLightingMood.Balanced)
            {
                GodotPresentationEnvironmentSnapshot environment = cabin.ApplyF112EnvironmentPreview(
                    time, atmosphere, mood);
                object truth = cabin.SetF112CabinView(view);
                Stage($"environment-applied view={view} time={time} atmosphere={atmosphere}");
                await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
                string name = $"{art}-{view}-{time}-{atmosphere}" +
                    (mood == GodotDiagnosticLightingMood.Balanced ? "" : $"-{mood}") + ".png";
                object image = CaptureF112Output(System.IO.Path.Combine(directory, name), Stage);
                captures.Add(new { art, view, time, atmosphere, mood, name, image, truth,
                    Environment = new { SunRotation = environment.SunRotationDegrees.ToString(), environment.SunEnergy,
                        environment.Shadows, Background = environment.BackgroundColor.ToHtml(),
                        Ambient = environment.AmbientColor.ToHtml(), environment.AmbientEnergy,
                        environment.Mood } });
            }
            async Task OriginalDrawWait() =>
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        catch (Exception exception)
        {
            Stage(exception.ToString());
            GD.PushError($"F1.12 Cabin preview failed: {exception}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
    }

    private async Task AwaitF112Boundary(GodotObject source, StringName signal)
    {
        var ready = new TaskCompletionSource();
        Callable callback = Callable.From(() => ready.TrySetResult());
        source.Connect(signal, callback);
        try
        {
            if (await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(8))) != ready.Task)
                throw new TimeoutException($"F1.12 native boundary {signal} did not complete within 8 seconds.");
            await ready.Task;
        }
        finally { source.Disconnect(signal, callback); }
    }

    protected object CaptureF112Output(string path, Action<string> stage)
    {
        int pre = 0, post = 0;
        void Pre() { pre++; stage("native-pre-draw"); }
        void Post() { post++; stage("native-post-draw"); }
        RenderingServer.FramePreDraw += Pre;
        RenderingServer.FramePostDraw += Post;
        try
        {
            stage("render-request");
            RenderingServer.ForceDraw(); // synchronous native boundary, includes the real GameplayOutput
            if (pre == 0 || post == 0) throw new InvalidOperationException("Native draw boundary was not observed.");
            ViewportTexture texture = UiComposition.GameplayTexture;
            stage($"texture-valid={texture.GetRid().IsValid}");
            using Image image = texture.GetImage();
            stage("readback-complete");
            if (image.IsEmpty()) throw new InvalidOperationException("Native GameplayOutput is empty.");
            byte[] pixels = image.GetData();
            byte minimum = 255, maximum = 0;
            // Alpha is excluded: an opaque black texture is not acceptable rendered evidence.
            image.Convert(Image.Format.Rgb8);
            foreach (byte value in image.GetData()) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); }
            if (maximum - minimum < 8) throw new InvalidOperationException("Native image has no meaningful color variation.");
            if (image.SavePng(path) != Error.Ok) throw new InvalidOperationException("Native PNG save failed.");
            stage("png-saved");
            return new { Width = image.GetWidth(), Height = image.GetHeight(), minimum, maximum, pre, post,
                PixelHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)),
                PngHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))) };
        }
        finally { RenderingServer.FramePreDraw -= Pre; RenderingServer.FramePostDraw -= Post; }
    }

    private async void RunF112ProductDogfood()
    {
        GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
        var lifetimes = new List<object>(3);
        var samples = new List<object>(360);
        var epochs = new HashSet<ulong>();
        string mode = ReadLocalArgument("--f1-12-art=") ?? "installed";
        int seconds = int.TryParse(ReadLocalArgument("--f1-12-seconds="), out int requested)
            ? requested : 40;
        int stableRadius = int.TryParse(ReadLocalArgument("--f1-12-stable-radius="), out int radius)
            ? radius : 2;
        try
        {
            if (seconds is < 4 or > 120 || stableRadius is < 1 or > 3 ||
                mode is not ("installed" or "absent" or "primitive") ||
                string.IsNullOrWhiteSpace(ReadLocalArgument("--f1-12-save-root=")))
                throw new InvalidOperationException("F1.12 requires bounded duration/art and an isolated save root.");
            for (int lifetime = 0; lifetime < 3; lifetime++)
            {
                RequireNode<Button>("Capability_SinglePlayer").EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                RequireNode<LineEdit>("GuestDisplayName").Text = $"F1.12 Guest {lifetime}";
                RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(3);
                LocalCabinClient cabin = _cabin ?? throw new InvalidOperationException("Product guest entry failed.");
                LocalCabinPresentationSnapshot initial = cabin.GetPresentationSnapshot();
                if (!epochs.Add(initial.ApplicationSessionEpoch))
                    throw new InvalidOperationException("Product re-entry reused retired application epoch.");
                if (mode == "installed" && !initial.OfficialPresentation.Available ||
                    mode == "absent" && (initial.OfficialPresentation.Available || initial.Actors.AnimatedActors != 0) ||
                    mode == "primitive" && (initial.Actors.AnimatedActors != 0 ||
                        initial.Actors.PrimitiveFallbackActors != initial.Actors.ActiveActors))
                    throw new InvalidOperationException("Actual installed/absent art did not match requested product.");
                GD.Print($"F1.12 START lifetime={lifetime} art={mode}");
                // Existing maintained mechanics; administrative relocations only choose probe starting points.
                cabin.RunD03Acceptance();
                await Frames(3);
                cabin.ValidateD03TooltipAfterNativeLayout();
                cabin.RunD00Acceptance();
                cabin.RunF112ProfileAcceptance();
                cabin.RunF112CabinAcceptance();
                object farSave = cabin.RunF112SaveLoadAcceptance(far: true);
                object centralSave = cabin.RunF112SaveLoadAcceptance(far: false);
                cabin.RunF112ItemAcceptance();
                cabin.RunD02Acceptance();
                cabin.PrepareF112CentralControl();
                cabin.RunD01Acceptance();
                LocalF16DerivedAcceptanceSnapshot? derived = mode == "primitive"
                    ? null : cabin.RunF16DerivedAcceptance();
                // D0.3 deliberately freezes the physics pump for its native tooltip-layout probe.
                // Return to ordinary product servicing before asynchronous Chat/Settings/soak work.
                cabin.SetPhysicsProcess(true);
                await RunChatFocusSmoke(cabin);
                ulong appBeforeSettings = cabin.GetPresentationSnapshot().ApplicationSessionEpoch;
                await RunF112SettingsAcceptance();
                if (cabin.GetPresentationSnapshot().ApplicationSessionEpoch != appBeforeSettings)
                    throw new InvalidOperationException("Product Settings replaced active Game session.");
                // Normalize source/location/window for a stable bounded trend window.
                cabin.PrepareF112CentralControl();
                cabin.RunD01Acceptance();
                cabin.SetF112StableRadius(stableRadius);
                await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                LocalCabinPresentationSnapshot stable = cabin.GetPresentationSnapshot();
                long started = Stopwatch.GetTimestamp();
                int churn = 0;
                for (int second = 0; second < seconds; second++)
                {
                    if (second % 4 == 0)
                    {
                        for (int page = 0; page < 4; page++)
                            _UnhandledInput(new InputEventKey
                            { Keycode = Key.F11, PhysicalKeycode = Key.F11, Pressed = true });
                        if (CaptureSessionBoundary().DebugPage != GodotClientDebugPage.Off)
                            throw new InvalidOperationException("Debug did not cycle back to Off.");
                        cabin.RunF17UiAcceptance();
                        churn++;
                    }
                    await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                    LocalCabinPresentationSnapshot current = cabin.GetPresentationSnapshot();
                    // Public truth continues to arrive/move during this real product window. Its
                    // nodes/resources may change legitimately; isolate stable structural ownership.
                    if (current.Renderer.ActiveNodes - current.Renderer.ActivePublicNodes !=
                            stable.Renderer.ActiveNodes - stable.Renderer.ActivePublicNodes ||
                        current.Renderer.ActiveResources - current.Renderer.ActivePublicBatchGroups !=
                            stable.Renderer.ActiveResources - stable.Renderer.ActivePublicBatchGroups ||
                        current.Renderer.ActiveRenderChunks != stable.Renderer.ActiveRenderChunks ||
                        current.Actors.ActiveActors != stable.Actors.ActiveActors || current.OpenUiViews != 0 ||
                        current.DerivedResources.LiveTextures != stable.DerivedResources.LiveTextures ||
                        current.DerivedResources.LiveMaterials != stable.DerivedResources.LiveMaterials)
                        throw new InvalidOperationException("Stable product/UI churn changed live presentation ownership: " +
                            $"nodes={stable.Renderer.ActiveNodes}/{current.Renderer.ActiveNodes}, " +
                            $"resources={stable.Renderer.ActiveResources}/{current.Renderer.ActiveResources}, " +
                            $"actors={stable.Actors.ActiveActors}/{current.Actors.ActiveActors}, ui={current.OpenUiViews}, " +
                            $"textures={stable.DerivedResources.LiveTextures}/{current.DerivedResources.LiveTextures}, " +
                            $"materials={stable.DerivedResources.LiveMaterials}/{current.DerivedResources.LiveMaterials}; " +
                            $"beforeRenderer={stable.Renderer}; afterRenderer={current.Renderer}.");
                    using Process process = Process.GetCurrentProcess();
                    samples.Add(new { lifetime, second, ElapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                        ManagedBytes = GC.GetTotalMemory(false), process.WorkingSet64,
                        Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
                        current.SemanticResidentChunks, current.RenderedChunks, current.Actors,
                        current.Renderer, current.DerivedResources, current.PresentationCache,
                        ProcessFrameMilliseconds = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d,
                        FramesPerSecond = Engine.GetFramesPerSecond(),
                        DrawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                        UiChurnCycles = churn });
                }
                lifetimes.Add(new { lifetime, Initial = initial, Final = cabin.GetPresentationSnapshot(),
                    centralSave, farSave, derived, UiChurnCycles = churn,
                    StableWindowSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds });
                // Leave with focused text + live views to attack real retirement, not already-empty state.
                cabin.PrepareF07RetirementCanary($"F1.12 Guest {lifetime}", CabinHumanBody.XBot);
                RetireCurrentSession();
                ReturnToMainMenu("F1.12 product lifetime retired.");
                await Frames(3);
                RequireCleanSessionBoundary(in baseline, $"F1.12 lifetime {lifetime}");
                GD.Print($"F1.12 LIFETIME PASS lifetime={lifetime} uiCycles={churn} retirement=clean");
            }
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: true, guestDisplayName: "F1.12 Reload Guest");
            if (!epochs.Add(_cabin!.GetPresentationSnapshot().ApplicationSessionEpoch))
                throw new InvalidOperationException("Re-entry Load reused a retired application epoch.");
            object reentryLoad = _cabin!.RunF112ReentryLoadAcceptance();
            RetireCurrentSession();
            ReturnToMainMenu("F1.12 re-entry Load retired.");
            await Frames(3);
            RequireCleanSessionBoundary(in baseline, "F1.12 re-entry Load");
            string path = ReadLocalArgument("--f1-12-evidence=") ??
                throw new InvalidOperationException("F1.12 needs an evidence output path.");
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Schema = "client-f1.12-product-dogfood/v1", SourceHead = ReadLocalArgument("--f1-12-head="),
                Edition = GetSnapshot().Edition, ArtMode = mode, DisplayBackend = DisplayServer.GetName(),
                StableRadius = stableRadius,
                Renderer = RenderingServer.GetCurrentRenderingMethod(), VegetationOptIn =
                    OS.GetCmdlineUserArgs().Contains("--f1-11-vegetation"),
                Baseline = baseline, lifetimes, reentryLoad, samples, FinalBoundary = CaptureSessionBoundary(),
                HumanFeelRated = false,
            }, new JsonSerializerOptions { WriteIndented = true,
                Converters = { new JsonStringEnumConverter() } }));
            GD.Print("F1.12 PRODUCT DOGFOOD PASS lifetimes=3 settings=product " +
                "saveLoad=central/far item=pickup/equip/drop/repickup retirement=clean");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F1.12 product dogfood failed: {exception}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
        async Task Frames(int count)
        {
            for (int index = 0; index < count; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
