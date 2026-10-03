using Godot;
using System.Diagnostics;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Client.Sound;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
#if ULTRASIM_COMPLETE
using System.Text;
using System.Text.Json;
using UltraSim.Server.ConceptDemo;
#endif

namespace UltraSim.Client.Godot;

/// <summary>
/// Shared F0.2 local-capability composition used by the Single Player and Complete executables.
/// The shared bootstrap remains capability-free; Online therefore never resolves this assembly.
/// </summary>
public abstract partial class LocalPlayClientRoot : ClientRoot
{
    private LocalCabinClient? _cabin;
    private Node? _catalogMarker;
    private string _guestAdmissionName = string.Empty;
    private GameGuestBody _guestAdmissionBody = GameGuestBody.HumanFemale;
    private string? _guestAdmissionError;
    private GodotClientSessionBoundarySnapshot _f13PrimitiveBaseline;
    private GodotClientSessionBoundarySnapshot _f19DisplayBaseline;

    protected override string SessionPresentationDiagnostics() => _cabin is null
        ? base.SessionPresentationDiagnostics() : _cabin.GetPresentationSnapshot().Renderer.ToDiagnosticText() + "\n" +
            _cabin.GetPresentationSnapshot().Actors.ToDiagnosticText();

    protected override GodotClientPerformanceContext CapturePerformanceContext() => _cabin is null
        ? base.CapturePerformanceContext()
        : _cabin.GetPerformanceContext(EffectiveDisplay.GameplayOutputResolution.Width,
            EffectiveDisplay.GameplayOutputResolution.Height);

    public override void _Ready()
    {
        base._Ready();
        if (!CanStartApplicationWork) return;
        string[] arguments = OS.GetCmdlineUserArgs();
        if (arguments.Contains("--f1-12-native-product", StringComparer.Ordinal))
        {
            RunF112NativeProduct();
            return;
        }
        if (arguments.Contains("--f1-12-memory", StringComparer.Ordinal))
        {
            RunF112MemoryControl();
            return;
        }
        if (arguments.Contains("--f1-12-render-probe", StringComparer.Ordinal))
        {
            RunF112RenderProbe();
            return;
        }
        if (arguments.Contains("--f1-12-cabin-preview", StringComparer.Ordinal))
        {
            RunF112CabinPreview();
            return;
        }
        if (arguments.Contains("--f1-12-dogfood", StringComparer.Ordinal))
        {
            RunF112ProductDogfood();
            return;
        }
        if (arguments.Contains("--spatial-space-smoke", StringComparer.Ordinal))
        {
            try
            {
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
                _cabin!.RunSpatialSpaceAcceptance();
                RetireCurrentSession();
                ReturnToMainMenu("Local spatial-space smoke passed.");
                GD.Print("SPATIAL SPACE LOCAL PASS trips=2 actor=retained world=retired session=clean");
                RequestApplicationExit(0);
            }
            catch (Exception exception)
            {
                GD.PushError($"Local spatial-space smoke failed: {exception}");
                RetireCurrentSession();
                RequestApplicationExit(1);
            }
            return;
        }
        if (ReadLocalArgument("--client-performance-layout-capture=") is { Length: > 0 } capturePath)
        {
            RunClientPerformanceLayoutCapture(capturePath);
            return;
        }
        if (ReadLocalArgument("--client-hitch-study=") is { Length: > 0 } hitchScenario)
        {
            RunClientHitchStudy(hitchScenario);
            return;
        }
        if (arguments.Contains("--p9-two-room-benchmark", StringComparer.Ordinal))
        {
            // Capture mode is handled above. Bare opt-in launch remains interactive and never
            // retargets Classic Cabin's normal menu entry or persistent save identity.
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: false, useCabinVisualBenchmark: true);
            return;
        }
        bool conceptDemoSmoke = arguments.Contains("--d0-0-smoke", StringComparer.Ordinal);
        bool residencySmoke = arguments.Contains("--d0-1-smoke", StringComparer.Ordinal);
        bool farIslandSmoke = arguments.Contains("--d0-2-smoke", StringComparer.Ordinal);
        bool signSmoke = arguments.Contains("--d0-3-smoke", StringComparer.Ordinal);
        bool fallbackLabSmoke = arguments.Contains("--d0-4-smoke", StringComparer.Ordinal);
        bool primitiveOnlySmoke = arguments.Contains("--f1-3-primitive-smoke", StringComparer.Ordinal);
        bool f16DerivedSmoke = arguments.Contains("--f1-6-derived-smoke", StringComparer.Ordinal);
        bool f17UiSmoke = arguments.Contains("--f1-7-ui-smoke", StringComparer.Ordinal);
        bool f19DisplaySmoke = arguments.Contains("--f1-9a-display-smoke", StringComparer.Ordinal);
        string? f14Mode = ReadLocalArgument("--f1-4-official-smoke=");
        bool guestAdmissionSmoke = arguments.Contains("--d0-5-smoke", StringComparer.Ordinal);
        bool f02Smoke = arguments.Contains("--f0-2-smoke", StringComparer.Ordinal);
#if ULTRASIM_COMPLETE
        bool sharedHostSmoke = arguments.Contains("--m0-0-host-smoke", StringComparer.Ordinal);
#else
        const bool sharedHostSmoke = false;
#endif
        if (arguments.Contains("--composition-smoke", StringComparer.Ordinal) ||
            !conceptDemoSmoke && !residencySmoke && !farIslandSmoke && !signSmoke &&
            !fallbackLabSmoke && !primitiveOnlySmoke && !guestAdmissionSmoke &&
            !f16DerivedSmoke && !f17UiSmoke && !f19DisplaySmoke &&
            f14Mode is null && !f02Smoke && !sharedHostSmoke)
            return;
        try
        {
#if ULTRASIM_COMPLETE
            if (sharedHostSmoke)
            {
                RunSharedHostSmoke();
                return;
            }
#endif
            if (guestAdmissionSmoke)
            {
                RunGuestAdmissionSmoke();
                return;
            }
            if (signSmoke || fallbackLabSmoke)
            {
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
                RunPresentationSmokeAfterNativeFrames(fallbackLabSmoke);
                return;
            }
            if (primitiveOnlySmoke)
            {
                _f13PrimitiveBaseline = CaptureSessionBoundary();
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true, primitiveOnlyPresentation: true);
                RunF13PrimitiveSmokeAfterNativeFrames();
                return;
            }
            if (f16DerivedSmoke)
            {
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
                _cabin!.RunD00Acceptance();
                LocalF16DerivedAcceptanceSnapshot evidence =
                    _cabin.RunF16DerivedAcceptance();
                GD.Print(
                    $"F1.6 DERIVED LOCAL PASS semantic={evidence.SemanticHash} " +
                    $"fingerprints={evidence.LowerPresentationFingerprint}/" +
                    $"{evidence.UserPresentationFingerprint}/" +
                    $"{evidence.RestoredPresentationFingerprint} " +
                    $"keys={evidence.LowerDerivedKey}/{evidence.UserDerivedKey}/" +
                    $"{evidence.RestoredDerivedKey} " +
                    $"builds={evidence.Before.TextureBuilds}/" +
                    $"{evidence.AfterUser.TextureBuilds}/" +
                    $"{evidence.AfterRestore.TextureBuilds}/" +
                    $"{evidence.AfterIneffective.TextureBuilds} " +
                    $"diskHits={evidence.AfterRestore.DiskTextureHits}");
                RetireCurrentSession();
                ReturnToMainMenu("F1.6 derived Local smoke passed.");
                RequestApplicationExit(0);
                return;
            }
            if (f17UiSmoke)
            {
                GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
                LocalF17UiAcceptanceSnapshot evidence = _cabin!.RunF17UiAcceptance();
                LocalCabinPresentationSnapshot beforeDisplay = _cabin.GetPresentationSnapshot();
                SubViewport retainedOutput = UiComposition.GameplayViewport;
                Node retainedWorld = WorldRoot;
                ResolvedClientDisplayConfiguration originalDisplay = EffectiveDisplay;
                ApplyDisplay(ClientDisplayResolver.Resolve(
                    ClientDisplaySettings.Default.WithAdvancedFromSimple() with
                    {
                        AdvancedApplication = ClientPixelSize.Create(2560, 1440),
                        AdvancedFrameMode = ClientGameplayFrameMode.Custom,
                        AdvancedFrameSize = ClientPixelSize.Create(1920, 1080),
                        AdvancedOutput = ClientPixelSize.Create(1280, 720),
                        AdvancedInternalScalePercent = 50,
                    }));
                LocalCabinPresentationSnapshot afterDisplay = _cabin.GetPresentationSnapshot();
                if (!ReferenceEquals(retainedOutput, UiComposition.GameplayViewport) ||
                    !ReferenceEquals(retainedWorld.GetParent(), retainedOutput) ||
                    beforeDisplay.ApplicationSessionEpoch != afterDisplay.ApplicationSessionEpoch ||
                    beforeDisplay.ControlSessionEpoch != afterDisplay.ControlSessionEpoch ||
                    beforeDisplay.ContentFingerprint != afterDisplay.ContentFingerprint ||
                    beforeDisplay.SemanticResidentChunks != afterDisplay.SemanticResidentChunks ||
                    beforeDisplay.RenderedChunks != afterDisplay.RenderedChunks ||
                    beforeDisplay.Actors.ActiveActors != afterDisplay.Actors.ActiveActors ||
                    UiComposition.GetSnapshot().Workspace.SelectedOutputShape !=
                        ClientPixelSize.Create(1280, 720))
                    throw new InvalidOperationException(
                        "A presentation-only display change replaced Local semantic/session ownership.");
                ApplyDisplay(originalDisplay);
                GD.Print("F1.9A LOCAL ACTIVE DISPLAY PASS session=retained " +
                    "semantic=retained viewport=retained output=1280x720 internal3D=640x360");
                GD.Print(
                    $"F1.7 LOCAL UI PASS views={evidence.MaximumOpenSemanticViews} " +
                    $"windows={evidence.MaximumOpenNativeWindows} " +
                    $"cascade={evidence.InventoryInitialPosition}/{evidence.MonitorInitialPosition} " +
                    $"dragged={evidence.DraggedPosition} context=" +
                    $"{evidence.ContextSelectInvoked}/{evidence.ContextInteractInvoked}");
                RetireCurrentSession();
                ReturnToMainMenu("F1.7 Local UI smoke passed.");
                RequireCleanSessionBoundary(in baseline, "F1.7 Local UI retirement");
                RequestApplicationExit(0);
                return;
            }
            if (f19DisplaySmoke)
            {
                _f19DisplayBaseline = CaptureSessionBoundary();
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
                RunF19DisplaySmokeAfterNativeFrames();
                return;
            }
            if (f14Mode is not null)
            {
                bool expectOfficial = f14Mode switch
                {
                    "installed" => true,
                    "absent" => false,
                    _ => throw new InvalidOperationException(
                        "--f1-4-official-smoke must be installed or absent."),
                };
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
                _cabin!.RunD00Acceptance();
                LocalCabinPresentationSnapshot evidence = _cabin.GetPresentationSnapshot();
                if (expectOfficial)
                {
                    if (!evidence.OfficialPresentation.Available ||
                        !evidence.PresentationGeneration.LocalLayerPresent ||
                        evidence.PresentationGeneration.LocalSelections != 2 ||
                        evidence.Actors.AnimatedActors == 0 ||
                        evidence.ActorResources.RealizedResources == 0)
                        throw new InvalidOperationException(
                            $"Official Local presentation did not realize: {evidence}.");
                }
                else if (evidence.OfficialPresentation.Available ||
                    evidence.PresentationGeneration.LocalLayerPresent ||
                    evidence.Actors.AnimatedActors != 0 ||
                    evidence.Actors.PrimitiveFallbackActors != evidence.Actors.ActiveActors)
                    throw new InvalidOperationException(
                        $"The physically absent Official source did not use Primitive: {evidence}.");
                GD.Print(
                    $"F1.4 OFFICIAL LOCAL PASS mode={f14Mode} " +
                    $"manifest={OfficialLocalPresentation.Manifest.ManifestHash} " +
                    $"plan={OfficialLocalPresentation.Plan.PlanHash} " +
                    $"actors={evidence.Actors.ActiveActors} " +
                    $"animated={evidence.Actors.AnimatedActors} " +
                    $"primitive={evidence.Actors.PrimitiveFallbackActors} " +
                    $"localSelections={evidence.PresentationGeneration.LocalSelections} " +
                    $"blobs={evidence.PresentationCache.BlobCount} " +
                    $"livePins={evidence.PresentationCache.LiveBlobPins}/" +
                    $"{evidence.PresentationCache.LiveCatalogPins}");
                RetireCurrentSession();
                ReturnToMainMenu($"F1.4 Official Local {f14Mode} smoke passed.");
                RequestApplicationExit(0);
                return;
            }
            if (conceptDemoSmoke || residencySmoke || farIslandSmoke)
            {
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
                LocalCabinPresentationSnapshot evidence;
                if (farIslandSmoke)
                    evidence = _cabin!.RunD02Acceptance();
                else
                {
                    _cabin!.RunD00Acceptance();
                    if (conceptDemoSmoke) AssertP8ConceptDemoAudio();
                    evidence = residencySmoke
                        ? _cabin.RunD01Acceptance()
                        : _cabin.GetPresentationSnapshot();
                }
                if (evidence.AuthoredChunks != UltraSim.Game.Composition.GamePlayableWorld.ConceptChunkCount ||
                    evidence.SemanticResidentChunks >= evidence.AuthoredChunks ||
                    evidence.RenderedChunks >= evidence.SemanticResidentChunks)
                    throw new InvalidOperationException(
                        "The Concept Demo did not preserve authored > resident > rendered structure.");
                GD.Print(
                    $"{(farIslandSmoke ? "D0.2 FAR ISLAND" :
                        residencySmoke ? "D0.1 RESIDENCY" : "D0.0 CONCEPT DEMO")} PASS " +
                    $"player={evidence.PlayerPosition} authored/resident/rendered=" +
                    $"{evidence.AuthoredChunks}/{evidence.SemanticResidentChunks}/" +
                    $"{evidence.RenderedChunks} radius={evidence.RenderDistanceChunks} " +
                    $"authorityTicks={evidence.AuthorityTicks} content={evidence.ContentFingerprint}");
                RetireCurrentSession();
                ReturnToMainMenu(farIslandSmoke
                    ? "Concept Demo D0.2 Far Island smoke passed."
                    : residencySmoke
                        ? "Concept Demo D0.1 residency smoke passed."
                        : "Concept Demo D0.0 smoke passed.");
                RequestApplicationExit(0);
                return;
            }

            CabinContentVariant variant = ReadVariant();
            CabinHumanBody human = ReadHuman();
            StartLocalCabin(variant, human, useConceptDemoWorld: false);
            LocalCabinPresentationSnapshot first = _cabin!.GetPresentationSnapshot();
            ValidateEffectiveSettings(first);
            _cabin.RunF02Acceptance();
            if (AudioAdapter is not GodotClientAudioAdapter audio ||
                audio.GetCueRequestsForNativeTest(SoundCueId.DoorWoodOpen) == 0 ||
                audio.GetCueRequestsForNativeTest(SoundCueId.Jump) == 0)
                throw new InvalidOperationException(
                    "Accepted Cabin door and jump transitions did not reach the Sound Engine.");

            RetireCurrentSession();
            ReturnToMainMenu("First local session retired.");
            if (WorldRoot.HasNode("LocalCabinSession") ||
                ContentCatalogRoot.GetChildCount() != 0)
                throw new InvalidOperationException("Local session presentation did not retire cleanly.");

            StartLocalCabin(variant, human, useConceptDemoWorld: false);
            LocalCabinPresentationSnapshot second = _cabin!.GetPresentationSnapshot();
            ValidateEffectiveSettings(second);
            if (!second.IsInitialized || first.ApplicationSessionEpoch == second.ApplicationSessionEpoch ||
                first.ControlSessionEpoch == second.ControlSessionEpoch ||
                first.ContentFingerprint != second.ContentFingerprint ||
                first.ContentGeneration == second.ContentGeneration ||
                second.PlayerPosition != UltraSim.Game.Composition.GamePlayableWorld.PlayerStart ||
                second.DoorIsOpen || !second.BreadIsInBackpack || second.OpenUiViews != 0)
                throw new InvalidOperationException("Repeated local entry reused retired session/content lineage.");

            GD.Print(
                $"F0.2 SINGLE PLAYER PASS variant={variant} human={human} " +
                $"fingerprint={second.ContentFingerprint} meshes={second.HumanMeshInstances} " +
                $"session={first.ApplicationSessionEpoch}->{second.ApplicationSessionEpoch} " +
                $"authorityTicks={first.AuthorityTicks}/{second.AuthorityTicks}");
            RetireCurrentSession();
            ReturnToMainMenu("F0.2 lifecycle smoke passed.");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F0.2 local composition failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            RequestApplicationExit(1);
        }
    }

    private async void RunClientPerformanceLayoutCapture(string path)
    {
        try
        {
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: true);
            UiComposition.CycleDebug(page => page.ToString());
            if (UiComposition.DebugPage != GodotClientDebugPage.Performance ||
                UiComposition.DebugHost.GetNodeOrNull<GodotClientPerformanceHud>("DebugPerformance") is null ||
                UiComposition.DebugHost.GetNodeOrNull<Button>("DemoHelpToggle") is null)
                throw new InvalidOperationException("The exported Local demo Help/Performance layout did not open.");
            for (int frame = 0; frame < 90; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string target = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SaveLayout(target);
            UiComposition.DebugHost.GetNode<Button>("DemoHelpToggle")
                .EmitSignal(Button.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            PanelContainer help = UiComposition.DebugHost.GetNode<PanelContainer>("DemoHelpPanel");
            GodotClientPerformanceHud performance = UiComposition.DebugHost
                .GetNode<GodotClientPerformanceHud>("DebugPerformance");
            if (!help.Visible || help.GetGlobalRect().Intersects(performance.GetGlobalRect()))
                throw new InvalidOperationException("The exported Help and Performance panels overlap.");
            string helpPath = Path.ChangeExtension(target, ".help.png");
            SaveLayout(helpPath);
            GD.Print($"CLIENT PERFORMANCE LAYOUT PASS 1920x1080 image={target} help={helpPath}");
            RetireCurrentSession();
            RequestApplicationExit();
        }
        catch (Exception error)
        {
            GD.PushError($"CLIENT PERFORMANCE LAYOUT FAIL: {error}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }

        void SaveLayout(string destination)
        {
            RenderingServer.ForceDraw();
            using ViewportTexture texture = GetViewport().GetTexture();
            using Image image = texture.GetImage();
            if (image.GetWidth() != 1920 || image.GetHeight() != 1080)
                throw new InvalidOperationException(
                    $"Expected a 1920×1080 exported layout, got {image.GetWidth()}×{image.GetHeight()}.");
            if (image.SavePng(destination) != Error.Ok)
                throw new InvalidOperationException("The exported layout PNG could not be saved.");
        }
    }

    private void AssertP8ConceptDemoAudio()
    {
        if (AudioAdapter is not GodotClientAudioAdapter audio ||
            audio.GetCueRequestsForNativeTest(SoundCueId.Pickup) == 0 ||
            audio.GetCueRequestsForNativeTest(SoundCueId.Equip) == 0 ||
            audio.GetCueRequestsForNativeTest(SoundCueId.Jump) == 0 ||
            audio.GetCueRequestsForNativeTest(SoundCueId.FootstepGrass) == 0)
            throw new InvalidOperationException(
                "Accepted pickup/equip/jump or presented locomotion missed native sound cues.");
    }

    private async void RunF19DisplaySmokeAfterNativeFrames()
    {
        // The active session intentionally already exists at this point; capture stable semantic
        // identity separately and require it to survive every native display resize.
        LocalCabinPresentationSnapshot initial = _cabin!.GetPresentationSnapshot();
        SubViewport retainedViewport = UiComposition.GameplayViewport;
        ResolvedClientDisplayConfiguration originalDisplay = EffectiveDisplay;
        try
        {
            var cases = new (string Name, ClientDisplaySettings Settings)[]
            {
                ("simple1920_100", ClientDisplaySettings.Default),
                ("output1920_internal50", ClientDisplaySettings.Default.WithAdvancedFromSimple() with
                { AdvancedInternalScalePercent = 50 }),
                ("output1280_internal100", ClientDisplaySettings.Default.WithAdvancedFromSimple() with
                { AdvancedOutput = ClientPixelSize.Create(1280, 720) }),
            };
            foreach ((string name, ClientDisplaySettings settings) in cases)
            {
                long started = Stopwatch.GetTimestamp();
                ResolvedClientDisplayConfiguration display = ClientDisplayResolver.Resolve(settings);
                ApplyDisplay(display);
                long rebuildMicroseconds = (long)(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                for (int frame = 0; frame < 5; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                double processMillisecondsTotal = 0;
                double rendererMicrosecondsTotal = 0;
                for (int frame = 0; frame < 30; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    processMillisecondsTotal += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1_000d;
                    rendererMicrosecondsTotal += _cabin!.GetPresentationSnapshot().Renderer.LastFrameMicroseconds;
                }
                LocalCabinPresentationSnapshot current = _cabin!.GetPresentationSnapshot();
                if (!ReferenceEquals(retainedViewport, UiComposition.GameplayViewport) ||
                    !ReferenceEquals(WorldRoot.GetParent(), retainedViewport) ||
                    current.ApplicationSessionEpoch != initial.ApplicationSessionEpoch ||
                    current.ControlSessionEpoch != initial.ControlSessionEpoch ||
                    current.ContentFingerprint != initial.ContentFingerprint ||
                    current.SemanticResidentChunks != initial.SemanticResidentChunks ||
                    current.Actors.ActiveActors != initial.Actors.ActiveActors ||
                    retainedViewport.Size != new Vector2I(
                        display.GameplayOutputResolution.Width, display.GameplayOutputResolution.Height))
                    throw new InvalidOperationException($"Display case {name} replaced semantic ownership.");
                long rgbaLowerBound = 4L * (
                    (long)display.GameplayOutputResolution.Width * display.GameplayOutputResolution.Height +
                    (long)display.InternalRenderResolution.Width * display.InternalRenderResolution.Height);
                GD.Print($"F1.9A NATIVE PERF case={name} app={display.ApplicationResolution.Width}x" +
                    $"{display.ApplicationResolution.Height} frame={display.GameplayFrame.Frame.Width}x" +
                    $"{display.GameplayFrame.Frame.Height} output={display.GameplayOutputResolution.Width}x" +
                    $"{display.GameplayOutputResolution.Height} internal3D=" +
                    $"{display.InternalRenderResolution.Width}x{display.InternalRenderResolution.Height} " +
                    $"rebuild_us={rebuildMicroseconds} engine_process_mean_ms={processMillisecondsTotal / 30:0.###} " +
                    $"renderer_frame_mean_us={rendererMicrosecondsTotal / 30:0.###} " +
                    $"rgba_lower_bound_bytes={rgbaLowerBound} renderer_nodes={current.Renderer.ActiveNodes} " +
                    $"renderer_resources={current.Renderer.ActiveResources} " +
                    $"gpu_bytes={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed):0}");
            }
            ApplyDisplay(originalDisplay);
            RetireCurrentSession();
            ReturnToMainMenu("F1.9A display smoke passed.");
            RequireCleanSessionBoundary(in _f19DisplayBaseline, "F1.9A Local display retirement");
            if (UiComposition.GetSnapshot().Workspace.SubViewportCount != 1 ||
                EffectiveDisplay != originalDisplay)
                throw new InvalidOperationException("The F1.9A display smoke leaked native output ownership.");
            GD.Print("F1.9A LOCAL DISPLAY SMOKE PASS cases=3 session=retained retirement=clean");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F1.9A native display smoke failed: {exception}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
    }

    private async void RunPresentationSmokeAfterNativeFrames(bool fallbackLab)
    {
        try
        {
            // MultiMesh render bounds are committed by RenderingServer frames, not by semantic
            // construction in _Ready. Cross that native boundary before claiming visible output.
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            LocalCabinPresentationSnapshot evidence = fallbackLab
                ? _cabin!.RunD04Acceptance()
                : _cabin!.RunD03Acceptance();
            if (!fallbackLab)
            {
                // Let Godot's deferred Container/Label layout settle before accepting the cached
                // hover lifecycle. This catches late auto-wrap growth that synchronous semantic
                // assertions cannot observe.
                for (int frame = 0; frame < 3; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _cabin!.ValidateD03TooltipAfterNativeLayout();
            }
            if (evidence.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
                evidence.SemanticResidentChunks >= evidence.AuthoredChunks ||
                evidence.RenderedChunks >= evidence.SemanticResidentChunks)
                throw new InvalidOperationException(
                    "The Concept Demo did not preserve authored > resident > rendered structure.");
            GD.Print(
                (fallbackLab ? "D0.4 FALLBACK LAB PASS " : "D0.3 INFORMATIONAL SIGN PASS ") +
                $"player={evidence.PlayerPosition} authored/resident/rendered=" +
                $"{evidence.AuthoredChunks}/{evidence.SemanticResidentChunks}/" +
                $"{evidence.RenderedChunks} radius={evidence.RenderDistanceChunks} " +
                $"authorityTicks={evidence.AuthorityTicks} content={evidence.ContentFingerprint}");
            RetireCurrentSession();
            ReturnToMainMenu(fallbackLab
                ? "Concept Demo D0.4 Fallback Lab smoke passed."
                : "Concept Demo D0.3 informational-sign smoke passed.");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"D0.{(fallbackLab ? 4 : 3)} native presentation failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            RequestApplicationExit(1);
        }
    }

    private async void RunF13PrimitiveSmokeAfterNativeFrames()
    {
        try
        {
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            LocalCabinPresentationSnapshot evidence = _cabin!.RunF13PrimitiveOnlyAcceptance();
            (int frozen, int multi, int publicRecords) = _cabin.GetF13PrimitiveCensus();
            GD.Print("F1.3 PRIMITIVE ONLY PASS " +
                $"semantic={evidence.ContentFingerprint} " +
                $"chunks={evidence.SemanticResidentChunks}/{evidence.RenderedChunks} " +
                $"actors={evidence.Actors.ActiveActors}/" +
                $"{evidence.Actors.PrimitiveFallbackActors} " +
                $"static/public/fallback-groups=" +
                $"{evidence.Renderer.ActiveStaticBatchGroups}/" +
                $"{evidence.Renderer.ActivePublicBatchGroups}/" +
                $"{evidence.Renderer.ActiveFallbackGroups} " +
                $"frozen/multi/public-records={frozen}/{multi}/{publicRecords} " +
                $"nodes/resources={evidence.Renderer.ActiveNodes}/" +
                $"{evidence.Renderer.ActiveResources}");
            RetireCurrentSession();
            ReturnToMainMenu("F1.3 primitive-only Concept Demo passed.");
            RequireCleanSessionBoundary(_f13PrimitiveBaseline, "F1.3 primitive-only retirement");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F1.3 primitive-only presentation failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            RequestApplicationExit(1);
        }
    }

#if ULTRASIM_COMPLETE
    private async void RunSharedHostSmoke()
    {
        try
        {
            int gameplayPort = ReadRequiredPort("--m0-0-gameplay-port=");
            int contentPort = ReadRequiredPort("--m0-0-content-port=");
            string readyPath = ReadRequiredArgument("--m0-0-ready=");
            var options = new ConceptDemoHostedServerOptions(
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, gameplayPort),
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, contentPort),
                MaximumParticipants: 8,
                OfficialContentRoot: OfficialContentRoot());
            StartLocalCabin(
                CabinContentVariant.Default,
                CabinHumanBody.XBot,
                useConceptDemoWorld: true,
                guestDisplayName: "Host Alice",
                serverExtensionFactory: new ConceptDemoHostedServerExtensionFactory(options));
            ulong generation = _cabin!.GetHostingSnapshot().ServerGenerationId;
            if (generation == 0 || !_cabin.TrySetHostingEnabled(enabled: true))
                throw new InvalidOperationException(
                    "The Complete host could not queue its hosting transition.");

            LocalPlayableServerExtensionSnapshot hosted = default;
            ulong hostingDeadline = checked(Time.GetTicksMsec() + 30_000UL);
            while (Time.GetTicksMsec() < hostingDeadline)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                hosted = _cabin.GetHostingSnapshot();
                if (hosted.HostingEnabled) break;
                if (hosted.Detail?.Contains("could not", StringComparison.OrdinalIgnoreCase) == true)
                    throw new InvalidOperationException(hosted.Detail);
            }
            if (!hosted.HostingEnabled || hosted.ServerGenerationId != generation ||
                string.IsNullOrWhiteSpace(hosted.Endpoint) ||
                string.IsNullOrWhiteSpace(hosted.DeploymentFingerprint))
                throw new InvalidOperationException(
                    $"The Complete host did not preserve and publish its internal server: {hosted}.");
            WriteSharedHostReady(readyPath, hosted);
            GD.Print(
                $"M0.0 COMPLETE HOST READY generation={generation} endpoint={hosted.Endpoint} " +
                $"fingerprint={hosted.DeploymentFingerprint} host_path=packet-free");

            int participantHighWater = 0;
            bool observedTwoRemotes = false;
            ulong participantsDeadline = checked(Time.GetTicksMsec() + 120_000UL);
            while (Time.GetTicksMsec() < participantsDeadline)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                LocalPlayableServerExtensionSnapshot current = _cabin.GetHostingSnapshot();
                if (!current.HostingEnabled || current.ServerGenerationId != generation)
                    throw new InvalidOperationException(
                        $"The hosted adapter replaced or lost the internal server: {current}.");
                if (current.RemoteParticipants > participantHighWater)
                {
                    participantHighWater = current.RemoteParticipants;
                    GD.Print(
                        $"M0.0 COMPLETE HOST REMOTES generation={generation} " +
                        $"remoteParticipants={participantHighWater}");
                }
                observedTwoRemotes |= current.RemoteParticipants >= 2;
                if (!observedTwoRemotes || current.RemoteParticipants != 0) continue;

                if (!_cabin.TrySetHostingEnabled(enabled: false))
                    throw new InvalidOperationException(
                        "The Complete host could not queue its stop-hosting transition.");
                ulong stopDeadline = checked(Time.GetTicksMsec() + 30_000UL);
                while (Time.GetTicksMsec() < stopDeadline)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    current = _cabin.GetHostingSnapshot();
                    if (!current.HostingEnabled) break;
                }
                if (current.HostingEnabled || current.ServerGenerationId != generation)
                    throw new InvalidOperationException(
                        "Stopping remote hosting did not preserve the internal server generation.");
                GD.Print(
                    $"M0.0 COMPLETE HOST PASS generation={generation} " +
                    "localPath=packet-free remotePaths=2 highWaterParticipants=3 " +
                    "remoteDetach=clean localWorldSurvived=true");
                RetireCurrentSession();
                ReturnToMainMenu("M0.0 embedded shared-authority smoke passed.");
                RequestApplicationExit(0);
                return;
            }
            throw new TimeoutException(
                "The Complete host did not observe two simultaneous remote participants and their clean detach.");
        }
        catch (Exception exception)
        {
            GD.PushError($"M0.0 Complete hosted smoke failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            RequestApplicationExit(1);
        }
    }

    private static void WriteSharedHostReady(
        string path,
        in LocalPlayableServerExtensionSnapshot hosted)
    {
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The M0.0 ready-file path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = full + $".{System.Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(new
                {
                    hosted.Endpoint,
                    Fingerprint = hosted.DeploymentFingerprint,
                    Generation = hosted.ServerGenerationId,
                }) + System.Environment.NewLine,
                new UTF8Encoding(false, true));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int ReadRequiredPort(string prefix)
    {
        string value = ReadRequiredArgument(prefix);
        if (!int.TryParse(value, out int port) || port is <= 0 or > 65_535)
            throw new InvalidOperationException($"M0.0 requires a valid port for '{prefix}'.");
        return port;
    }

    private static string ReadRequiredArgument(string prefix) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(
            argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..]
        is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"M0.0 requires '{prefix}<value>'.");
#endif

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_cabin is null) return;
        ClientShellSnapshot shell = ShellSnapshot();
        _cabin.SetGameplayInputAllowed(shell.Focus.GameplayInputAllowed);
    }

    public override void _ExitTree() => base._ExitTree();

    protected override void BeginCapability(ClientCapability capability)
    {
        if (capability != ClientCapability.SinglePlayer)
        {
            base.BeginCapability(capability);
            return;
        }
        _guestAdmissionName = string.Empty;
        _guestAdmissionBody = GameGuestBody.HumanFemale;
        _guestAdmissionError = null;
        BeginAdmission("Choose a temporary guest identity for this demo session.");
    }

    protected override void BuildCapabilityAdmissionSurface(in ClientShellSnapshot snapshot)
    {
        VBoxContainer column = CreateCenteredColumn(440);
        column.AddChild(Title("Enter Demo"));
        column.AddChild(CenteredLabel(
            "This name and body exist only for the current session. No account or saved character is created."));
        column.AddChild(new Label { Text = "Name" });
        var name = new LineEdit
        {
            Name = "GuestDisplayName",
            Text = _guestAdmissionName,
            PlaceholderText = "1–24 characters",
            MaxLength = GameGuestAdmissionPolicy.MaximumDisplayNameUtf8Bytes,
            SelectAllOnFocus = false,
        };
        column.AddChild(name);
        column.AddChild(new Label { Text = "Body" });
        var body = new OptionButton { Name = "GuestBody" };
        body.AddItem("X / Human Female", (int)GameGuestBody.HumanFemale);
        body.AddItem("Y / Human Male", (int)GameGuestBody.HumanMale);
        body.Select(_guestAdmissionBody == GameGuestBody.HumanMale ? 1 : 0);
        column.AddChild(body);
        if (!string.IsNullOrWhiteSpace(_guestAdmissionError))
        {
            column.AddChild(new Label
            {
                Name = "GuestAdmissionError",
                Text = _guestAdmissionError,
                Modulate = new Color(1f, 0.48f, 0.42f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
        Button join = MenuButton("Join");
        join.Name = "GuestJoin";
        ConnectDeferred(join, () => SubmitGuestAdmission(name.Text, (GameGuestBody)body.GetSelectedId()));
        name.TextSubmitted += _ => join.EmitSignal(BaseButton.SignalName.Pressed);
        column.AddChild(join);
        Button back = MenuButton("Back");
        back.Name = "GuestBack";
        ConnectDeferred(back, () => ReturnToMainMenu());
        column.AddChild(back);
        name.GrabFocus();
    }

    protected override void RetireCurrentSession()
    {
        if (_cabin is not null)
        {
            _cabin.Shutdown();
            WorldRoot.RemoveChild(_cabin);
            _cabin.Free();
            _cabin = null;
        }
        if (_catalogMarker is not null)
        {
            ContentCatalogRoot.RemoveChild(_catalogMarker);
            _catalogMarker.Free();
            _catalogMarker = null;
        }
    }

    protected override void OnEffectiveSettingsChanged(ResolvedClientSettings settings)
    {
        base.OnEffectiveSettingsChanged(settings);
        _cabin?.ApplySettings(settings);
    }

    protected LocalCabinPresentationSnapshot StartF07LocalSession(
        string displayName,
        CabinHumanBody body)
    {
        StartLocalCabin(
            CabinContentVariant.Default,
            body,
            useConceptDemoWorld: true,
            displayName);
        return _cabin!.GetPresentationSnapshot();
    }

    protected LocalCabinLifecycleResidueSnapshot PrepareF07LocalRetirement(
        string displayName,
        CabinHumanBody body) =>
        _cabin?.PrepareF07RetirementCanary(displayName, body) ??
        throw new InvalidOperationException("No local session is active for the F0.7 canary.");

    protected override void BuildAdditionalInSessionOptions(VBoxContainer column)
    {
#if ULTRASIM_COMPLETE
        if (_cabin is null) return;
        LocalPlayableServerExtensionSnapshot hosting = _cabin.GetHostingSnapshot();
        if (!hosting.Available) return;
        string label = hosting.HostingEnabled ? "Stop Hosting" : "Enable Hosting";
        Button hostingButton = MenuButton(label);
        hostingButton.Name = hosting.HostingEnabled ? "StopHosting" : "EnableHosting";
        ConnectDeferred(hostingButton, () =>
        {
            _cabin?.TrySetHostingEnabled(!hosting.HostingEnabled);
            ResumeCurrentSession();
        });
        column.AddChild(hostingButton);
        string detail = hosting.HostingEnabled
            ? $"Listening at {hosting.Endpoint}\nDeployment fingerprint: {hosting.DeploymentFingerprint}\n" +
              $"Remote participants: {hosting.RemoteParticipants}"
            : hosting.Detail ?? "The internal server is local-only.";
        column.AddChild(CenteredLabel(detail));
#endif
    }

    private void StartLocalCabin(
        CabinContentVariant variant,
        CabinHumanBody human,
        bool useConceptDemoWorld,
        string? guestDisplayName = null,
        ILocalPlayableServerExtensionFactory? serverExtensionFactory = null,
        bool primitiveOnlyPresentation = false,
        bool useCabinVisualBenchmark = false)
    {
        bool measureEntry = ReadLocalArgument("--client-hitch-study=") == "cold";
        long entryPhase = measureEntry ? Stopwatch.GetTimestamp() : 0;
        if (_cabin is not null)
            throw new InvalidOperationException("A local Cabin session is already active.");
        if (useCabinVisualBenchmark && useConceptDemoWorld)
            throw new ArgumentException("The visual benchmark and Concept Demo are distinct world roots.");
        BeginSessionLoading("Starting local Cabin authority…");
        CabinContentOracle oracle = CabinContentOracle.Create();
        if (measureEntry) RecordEntryPhase("shell and content oracle");
        CabinResolvedContent resolved = variant switch
        {
            CabinContentVariant.Default => oracle.Default,
            CabinContentVariant.Replace => oracle.Replace,
            CabinContentVariant.Add => oracle.Add,
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        _catalogMarker = new Node { Name = $"CabinCatalog_{variant}_{resolved.Catalog.Fingerprint}" };
        ContentCatalogRoot.AddChild(_catalogMarker);

        var cabin = new LocalCabinClient { Name = "LocalCabinSession", ExitRequested = RequestApplicationExit };
        GodotSessionUiLease sessionUi = BeginSessionUi("LocalCabinSession");
#if ULTRASIM_COMPLETE
        ILocalPlayableServerExtensionFactory? extensionFactory = serverExtensionFactory ??
            (useConceptDemoWorld ? new ConceptDemoHostedServerExtensionFactory(
                ConceptDemoHostedServerOptions.LocalMachineDefault with
                {
                    OfficialContentRoot = OfficialContentRoot(),
                }) : null);
#else
        ILocalPlayableServerExtensionFactory? extensionFactory = serverExtensionFactory;
#endif
        cabin.Configure(
            oracle,
            resolved,
            ReadLocalArgument("--f1-12-save-root=") ?? ProjectSettings.GlobalizePath(useCabinVisualBenchmark
                ? "user://single-player/cabin-visual-benchmark-v2-saves"
                : useConceptDemoWorld
                    ? "user://single-player/concept-demo-d0-0-saves"
                    : "user://single-player/cabin-saves"),
            sessionUi,
            human,
            EffectiveSettings,
            useConceptDemoWorld,
            guestDisplayName,
            extensionFactory,
            primitiveOnlyPresentation || ReadLocalArgument("--f1-12-art=") == "primitive",
            PresentationCacheRoot(),
            OfficialContentRoot(),
            AudioAdapter,
            useCabinVisualBenchmark);
        if (measureEntry) RecordEntryPhase("configure local session");
        _cabin = cabin;
        WorldRoot.AddChild(cabin);
        if (measureEntry) RecordEntryPhase("native child Ready");
        LocalCabinPresentationSnapshot snapshot = cabin.GetPresentationSnapshot();
        if (!snapshot.IsInitialized)
            throw new InvalidOperationException(snapshot.InitializationError ??
                "The local Cabin consumer did not initialize.");
        CommitSession(useCabinVisualBenchmark
            ? $"Cabin Visual Benchmark v1 — {snapshot.DisplayName} — {human}"
            : useConceptDemoWorld
                ? $"Concept Demo — {snapshot.DisplayName} — {human}"
                : $"Cabin — {variant} — {human}");
        if (measureEntry) RecordEntryPhase("commit shell session");

        void RecordEntryPhase(string name)
        {
            long now = Stopwatch.GetTimestamp();
            GD.Print($"CLIENT HITCH ENTRY PHASE {name} ms=" +
                Stopwatch.GetElapsedTime(entryPhase, now).TotalMilliseconds.ToString("0.000"));
            entryPhase = now;
        }
    }

    private void SubmitGuestAdmission(string name, GameGuestBody body)
    {
        _guestAdmissionName = name;
        _guestAdmissionBody = body;
        var request = new GameGuestAdmissionRequest(body, name);
        if (!GameGuestAdmissionPolicy.TryNormalize(
                request, out GameGuestIdentity identity,
                out _, out string? message))
        {
            _guestAdmissionError = message;
            BeginAdmission(message ?? "The guest identity was rejected.");
            return;
        }
        try
        {
            _guestAdmissionError = null;
            StartLocalCabin(
                CabinContentVariant.Default,
                identity.Body == GameGuestBody.HumanFemale
                    ? CabinHumanBody.XBot
                    : CabinHumanBody.YBot,
                useConceptDemoWorld: true,
                identity.DisplayName);
        }
        catch (Exception exception)
        {
            RetireCurrentSession();
            FailSession(exception.Message);
        }
    }

    private async void RunGuestAdmissionSmoke()
    {
        try
        {
            RequireNode<Button>("Capability_SinglePlayer").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.Admission || _cabin is not null)
                throw new InvalidOperationException("Single Player did not stop at guest admission.");

            RequireNode<LineEdit>("GuestDisplayName").Text = "\t";
            RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.Admission || _cabin is not null ||
                FindChild("GuestAdmissionError", recursive: true, owned: false) is not Label)
                throw new InvalidOperationException(
                    "Rejected guest input advanced or failed to produce bounded feedback.");

            RequireNode<LineEdit>("GuestDisplayName").Text = "  Demo Éowyn  ";
            OptionButton body = RequireNode<OptionButton>("GuestBody");
            body.Select(1);
            RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.InSession || _cabin is null)
                throw new InvalidOperationException("A valid guest did not enter the local world.");
            LocalCabinPresentationSnapshot evidence = _cabin.RunD05Acceptance(
                "Demo Éowyn", CabinHumanBody.YBot);
            await RunChatFocusSmoke(_cabin);
            GD.Print(
                $"D0.5 GUEST ADMISSION PASS name={evidence.DisplayName} body={evidence.HumanBody} " +
                $"player={evidence.PlayerPosition} authorityTicks={evidence.AuthorityTicks} " +
                "chatFocus=pass");
            RetireCurrentSession();
            ReturnToMainMenu("Concept Demo D0.5 guest admission smoke passed.");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"D0.5 guest admission failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            RequestApplicationExit(1);
        }
    }

    private async Task RunChatFocusSmoke(LocalCabinClient cabin)
    {
        LineEdit chat = RequireNode<LineEdit>("ChatInput");
        LocalChatInputSnapshot before = cabin.GetChatInputSnapshot();
        chat.Text = "keyboard submit canary";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (cabin.GetChatInputSnapshot() is not { HasFocus: true, GameplayInputAllowed: false })
            throw new InvalidOperationException(
                "Chat focus did not become the shell's exclusive text-input owner.");

        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Enter,
            PhysicalKeycode = Key.Enter,
            Pressed = true,
        });
        LocalChatInputSnapshot submitted = default;
        for (int frame = 0; frame < 240; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            submitted = cabin.GetChatInputSnapshot();
            if (submitted.AppliedOccurrences == before.AppliedOccurrences + 1) break;
        }
        if (submitted.HasFocus || submitted.Draft.Length != 0 ||
            submitted.AppliedOccurrences != before.AppliedOccurrences + 1)
            throw new InvalidOperationException(
                $"Enter did not submit and retire the focused Chat draft exactly once. Before={before}; After={submitted}.");

        chat.Text = "preserved draft";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Escape,
            PhysicalKeycode = Key.Escape,
            Pressed = true,
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        LocalChatInputSnapshot escaped = cabin.GetChatInputSnapshot();
        if (escaped.HasFocus || escaped.Draft != "preserved draft" ||
            ShellSnapshot().Screen != ClientShellScreen.InSession)
            throw new InvalidOperationException(
                "Escape did not release Chat focus locally while preserving the draft.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!cabin.GetChatInputSnapshot().GameplayInputAllowed)
            throw new InvalidOperationException(
                "Gameplay input did not resume after Chat released focus.");

        chat.Text = "click-away draft";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Rect2 chatBounds = chat.GetGlobalRect();
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(chatBounds.Position.X, chatBounds.Position.Y - 32),
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        LocalChatInputSnapshot clickedAway = cabin.GetChatInputSnapshot();
        if (clickedAway.HasFocus || clickedAway.Draft != "click-away draft" ||
            ShellSnapshot().Screen != ClientShellScreen.InSession)
            throw new InvalidOperationException(
                "Clicking outside Chat did not release focus while preserving the draft.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!cabin.GetChatInputSnapshot().GameplayInputAllowed)
            throw new InvalidOperationException(
                "Gameplay input did not resume after clicking away from Chat.");

        chat.Text = string.Empty;
    }

    private T RequireNode<T>(string name) where T : Node =>
        FindChild(name, recursive: true, owned: false) as T ??
        throw new InvalidOperationException($"Required guest-admission node '{name}' was missing.");

    private static CabinContentVariant ReadVariant()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--cabin-content=", StringComparison.Ordinal)) continue;
            return argument["--cabin-content=".Length..] switch
            {
                "default" => CabinContentVariant.Default,
                "replace" => CabinContentVariant.Replace,
                "add" => CabinContentVariant.Add,
                _ => throw new InvalidOperationException($"Unknown Cabin content argument '{argument}'."),
            };
        }
        return CabinContentVariant.Default;
    }

    private static CabinHumanBody ReadHuman()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--human=", StringComparison.Ordinal)) continue;
            return argument["--human=".Length..] switch
            {
                "x" => CabinHumanBody.XBot,
                "y" => CabinHumanBody.YBot,
                _ => throw new InvalidOperationException($"Unknown human calibration argument '{argument}'."),
            };
        }
        return CabinHumanBody.XBot;
    }

    private static string PresentationCacheRoot() =>
        ReadLocalArgument("--content-cache=") ??
        ReadLocalArgument("--a0-8-cache=") ??
        ProjectSettings.GlobalizePath("user://content-cache");

    private static string? OfficialContentRoot()
    {
        if (OS.GetCmdlineUserArgs().Contains(
                "--official-content-absent", StringComparer.Ordinal))
            return null;
        string? configured = ReadLocalArgument("--official-content-root=");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        string executable = OS.GetExecutablePath();
        string installed = Path.Combine(
            Path.GetDirectoryName(executable)!, "Content", "Official");
        if (Directory.Exists(installed)) return installed;
        string development = Path.GetFullPath(Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "OfficialContent"));
        return Directory.Exists(development) ? development : installed;
    }

    private static string? ReadLocalArgument(string prefix) => OS.GetCmdlineUserArgs()
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?
        [prefix.Length..];

    private void ValidateEffectiveSettings(in LocalCabinPresentationSnapshot snapshot)
    {
        if (snapshot.ControlGrammar != EffectiveSettings.ControlGrammar ||
            snapshot.CameraProfile != EffectiveSettings.CameraProfile ||
            snapshot.Camera.Rig != EffectiveSettings.CameraRig ||
            snapshot.Camera.TargetFrameProjection.Projection !=
                EffectiveSettings.CameraRig.Projection ||
            !Mathf.IsEqualApprox(snapshot.CameraDistance, EffectiveSettings.CameraDistance) ||
            !Mathf.IsEqualApprox(snapshot.CameraFovDegrees, EffectiveSettings.CameraFovDegrees) ||
            snapshot.RenderDistanceChunks != Math.Min(
                EffectiveSettings.RenderDistanceChunks, GamePlayableWorld.MaximumChunk) ||
            !EffectiveSettings.TryGetBinding(
                ClientSemanticAction.Jump, out ClientPhysicalInput jump) ||
            jump.Kind != ClientPhysicalInputKind.Keyboard || jump.Code != "SPACE")
            throw new InvalidOperationException(
                "The local Cabin consumer did not apply the persistent shell settings.");
    }
}
