using Godot;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.UI;
using UltraSim.Server.Runtime;
using UltraSim.UI;

/// <summary>
/// Native Godot acceptance for the exact S0.8 game UI definitions. Server/game
/// dependencies remain test-only and do not enter the production Godot wrapper.
/// </summary>
public partial class S08HeadlessAcceptance : Control
{
    public override async void _Ready()
    {
        try
        {
            RunAcceptance();
            RunNativeAdapterBoundaryAcceptance();
            RunOutsidePointerPolicyAcceptance();
            RunF17WorkspaceAndGumpAcceptance();
            RunF19DisplayCompositionAcceptance();
            RunF18CameraRayAcceptance();
            await RunRemoteInventoryAcceptance();
            GD.Print("S0.8 / I0.11 GODOT ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"S0.8 GODOT ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunF19DisplayCompositionAcceptance()
    {
        var cases = new (ClientDisplaySettings Settings, ClientPixelSize Application,
            ClientPixelRect Frame, ClientPixelSize Output, ClientPixelSize Internal)[]
        {
            (ClientDisplaySettings.Default, Size(1920, 1080),
                new ClientPixelRect(0, 0, 1920, 1080), Size(1920, 1080), Size(1920, 1080)),
            (ClientDisplaySettings.Default with
            {
                SimpleAspect = ClientDisplayAspectPreset.FourThree,
                SimpleResolution = Size(1600, 1200),
            }, Size(1600, 1200), new ClientPixelRect(0, 0, 1600, 1200),
                Size(1600, 1200), Size(1600, 1200)),
            (ClientDisplaySettings.Default.WithAdvancedFromSimple() with
            {
                AdvancedApplication = Size(2560, 1440),
                AdvancedFrameMode = ClientGameplayFrameMode.Custom,
                AdvancedFrameSize = Size(1920, 1080),
                AdvancedOutput = Size(1280, 720),
            }, Size(2560, 1440), new ClientPixelRect(320, 180, 1920, 1080),
                Size(1280, 720), Size(1280, 720)),
            (ClientDisplaySettings.Default.WithAdvancedFromSimple() with
            {
                AdvancedApplication = Size(2560, 1440),
                AdvancedFrameMode = ClientGameplayFrameMode.Custom,
                AdvancedFrameSize = Size(1920, 1080),
                AdvancedOutput = Size(1920, 1080),
                AdvancedInternalScalePercent = 50,
            }, Size(2560, 1440), new ClientPixelRect(320, 180, 1920, 1080),
                Size(1920, 1080), Size(960, 540)),
            (ClientDisplaySettings.Default.WithAdvancedFromSimple() with
            {
                AdvancedApplication = Size(1500, 920),
                AdvancedOutput = Size(1500, 920),
                AdvancedInternalScalePercent = 75,
            }, Size(1500, 920), new ClientPixelRect(0, 0, 1500, 920),
                Size(1500, 920), Size(1125, 690)),
            (ClientDisplaySettings.Default.WithAdvancedFromSimple() with
            {
                AdvancedApplication = Size(1500, 920),
                AdvancedFrameMode = ClientGameplayFrameMode.MaxFit,
                AdvancedOutput = Size(1280, 720),
                AdvancedInternalScalePercent = 75,
            }, Size(1500, 920), new ClientPixelRect(6, 41, 1488, 837),
                Size(1280, 720), Size(960, 540)),
        };
        var owner = new Control { Name = "F19DisplayAcceptance" };
        AddChild(owner);
        GodotClientUiComposition? composition = null;
        Node3D? worldCanary = null;
        Camera3D? camera = null;
        GodotGameplayCameraRig? cameraRig = null;
        ViewportTexture? retainedOutputTexture = null;
        try
        {
            for (int index = 0; index < cases.Length; index++)
            {
                var item = cases[index];
                ResolvedClientDisplayConfiguration display = ClientDisplayResolver.Resolve(item.Settings);
                if (composition is null)
                {
                    composition = new GodotClientUiComposition(owner, display);
                    retainedOutputTexture = composition.GameplayTexture;
                    worldCanary = new Node3D { Name = "DisplayOwnedWorldCanary" };
                    composition.GameplayViewport.AddChild(worldCanary);
                    camera = new Camera3D
                    {
                        Name = "DisplayOutputCamera", Current = true, Near = 0.1f, Far = 500f,
                    };
                    worldCanary.AddChild(camera);
                    cameraRig = new GodotGameplayCameraRig(camera,
                        ClientStockCameraRigs.Orbit, display.GameplayFrame);
                }
                else composition.ApplyDisplay(display);
                cameraRig!.ApplyFrame(display.GameplayFrame);
                GodotApplicationWorkspaceSnapshot snapshot = composition.GetSnapshot().Workspace;
                Expect(snapshot.ApplicationSize == item.Application &&
                    snapshot.GameplayFrame == item.Frame &&
                    snapshot.SelectedOutputShape == item.Output &&
                    snapshot.Internal3DSize == item.Internal &&
                    snapshot.SubViewportCount == 1 &&
                    composition.GameplayViewport.Size ==
                        new Vector2I(item.Output.Width, item.Output.Height) &&
                    ReferenceEquals(worldCanary?.GetParent(), composition.GameplayViewport) &&
                    composition.GameplayViewport.GetChildCount() == 1 &&
                    Math.Abs(composition.GameplayViewport.Scaling3DScale -
                        display.InternalRenderScalePercent / 100f) < 0.0001f,
                    $"The F1.9 native display layers diverged for case {index}: {snapshot}.");
                Vector2 applicationCenter = new(item.Frame.X + item.Frame.Width / 2f,
                    item.Frame.Y + item.Frame.Height / 2f);
                Expect(composition.TryToOutput(applicationCenter, out Vector2 outputCenter) &&
                    outputCenter.DistanceTo(new Vector2(item.Output.Width / 2f,
                        item.Output.Height / 2f)) < 1.5f,
                    $"F1.9 pointer mapping diverged for case {index}.");
                Vector3 nominated = camera!.ProjectPosition(outputCenter, 15f);
                Expect(camera.UnprojectPosition(nominated).DistanceTo(outputCenter) < 0.1f,
                    $"F1.9 output-viewport native ray diverged for case {index}.");
                ClientCanonicalViewProjection crop = ClientCanonicalViewProjection.ForFrame(
                    ClientStockCameraRigs.Orbit, display.GameplayFrame);
                Expect(crop.TargetHorizontalExtent <= crop.CanonicalHorizontalExtent + 0.00001 &&
                    crop.TargetVerticalExtent <= crop.CanonicalVerticalExtent + 0.00001,
                    "Display resolution expanded the canonical camera envelope.");
            }
            GD.Print("F1.9A DISPLAY COMPOSITION PASS cases=6 subviewport=1 " +
                "output=independent internal3d=scaled pointer=frame-to-output");
        }
        finally
        {
            composition?.Dispose();
            Expect(retainedOutputTexture is null || retainedOutputTexture.NativeInstance == IntPtr.Zero,
                "The Gameplay Output texture wrapper was left to finalize after native teardown.");
            RemoveChild(owner);
            owner.Free();
        }

        static ClientPixelSize Size(int width, int height) => ClientPixelSize.Create(width, height);
    }

    private void RunF18CameraRayAcceptance()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        ClientPixelSize application = ClientPixelSize.Create(
            Math.Max(1, (int)viewportSize.X), Math.Max(1, (int)viewportSize.Y));
        var outputViewport = new SubViewport
        {
            Name = "F19GameplayOutputAcceptance",
            Size = new Vector2I(1920, 1080),
        };
        AddChild(outputViewport);
        var camera = new Camera3D
        {
            Name = "F18CameraAcceptance",
            Current = true,
            Near = 0.1f,
            Far = 500f,
        };
        outputViewport.AddChild(camera);
        try
        {
            ClientCameraRig orbit = ClientStockCameraRigs.Orbit;
            GameplayFrameLayout full = GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(1920, 1080));
            var controller = new GodotGameplayCameraRig(camera, orbit, full);
            AssertRays(controller, full);
            Vector3 fullCenter = camera.ProjectRayNormal(Center(full));
            Vector3 stockPosition = camera.Position;
            float yaw = controller.YawRadians;
            float pitch = controller.PitchRadians;
            float distance = controller.DistanceMeters;
            ResolvedClientSettings defaults = ClientSettingsResolver.Resolve(
                ClientSettingsDocument.Empty);
            var named = new ClientNamedControlProfile("My Orbit", defaults.ControlGrammar,
                defaults.CameraRig, defaults.Bindings);
            controller.ApplyRig(named.CameraRig, full);
            Expect(camera.Position.DistanceTo(stockPosition) < 0.0001f &&
                camera.ProjectRayNormal(Center(full)).DistanceTo(fullCenter) < 0.0001f,
                "A user-named stock-equivalent rig diverged in native Godot realization.");
            controller.ApplyRig(ClientStockCameraRigs.ThirdPerson, full);
            Expect(Math.Abs(camera.Fov - controller.TargetFrameProjection
                    .TargetVerticalFovDegrees) < 0.01d &&
                Math.Abs(controller.TargetFrameProjection.TargetVerticalFovDegrees - 76d) < 0.01d,
                "A same-frame perspective rig switch retained the prior native lens.");
            controller.ApplyRig(orbit, full);
            // Pointer nomination uses the same native projection as the visible frame.
            Vector2 nominationPixel = new(full.SelectedOutput.Width * 0.75f,
                full.SelectedOutput.Height * 0.5f);
            ClientPixelPoint applicationNomination = new(
                full.Frame.X + full.Frame.Width * 3 / 4,
                full.Frame.Y + full.Frame.Height / 2);
            Vector3 nominationPoint = camera.ProjectPosition(nominationPixel, 15f);
            Expect(camera.UnprojectPosition(nominationPoint).DistanceTo(nominationPixel) < 0.1f &&
                full.TryToOutput(applicationNomination, out ClientPixelPoint mapped) &&
                Math.Abs(mapped.X - nominationPixel.X) <= full.SelectedOutput.Width / full.Frame.Width + 1,
                "Inside-frame pointer rays did not nominate the expected projected world point.");
            var drag = new InputEventMouseMotion { ButtonMask = MouseButtonMask.Middle };
            var idleMotion = new InputEventMouseMotion { ButtonMask = 0 };
            var wheel = new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp };
            Expect(GodotWorldInput.Matches(drag, defaults, ClientSemanticAction.CameraLook) &&
                !GodotWorldInput.Matches(idleMotion, defaults, ClientSemanticAction.CameraLook) &&
                GodotWorldInput.Matches(wheel, defaults, ClientSemanticAction.CameraZoomIn),
                "Native camera gestures did not resolve through semantic bindings.");

            GameplayFrameLayout fourThree = GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(800, 600));
            outputViewport.Size = new Vector2I(800, 600);
            controller.ApplyFrame(fourThree);
            AssertRays(controller, fourThree);
            Expect(controller.YawRadians == yaw && controller.PitchRadians == pitch &&
                controller.DistanceMeters == distance &&
                camera.ProjectRayNormal(Center(fourThree)).DistanceTo(fullCenter) < 0.002f,
                "Changing frame aspect changed the camera pose or center ray.");
            ClientCanonicalViewProjection cropped = controller.TargetFrameProjection;
            Expect(cropped.TargetHorizontalExtent < cropped.CanonicalHorizontalExtent &&
                Math.Abs(cropped.TargetVerticalExtent - cropped.CanonicalVerticalExtent) < 0.00001,
                "The 4:3 frame expanded or failed to crop the canonical camera view.");

            outputViewport.Size = new Vector2I(1600, 1200);
            controller.ApplyFrame(GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(1600, 1200)));
            AssertRays(controller, GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(1600, 1200)));
            outputViewport.Size = new Vector2I(600, 800);
            controller.ApplyFrame(GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(600, 800)));
            AssertRays(controller, GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(600, 800)));
            outputViewport.Size = new Vector2I(2560, 1080);
            controller.ApplyFrame(GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(2560, 1080)));
            AssertRays(controller, GameplayFrameLayout.MaxFit(application,
                ClientPixelSize.Create(2560, 1080)));
            outputViewport.Size = new Vector2I(1920, 1080);
            controller.ApplyFrame(full);
            AssertRays(controller, full);

            controller.ApplyRig(ClientStockCameraRigs.Independent, full);
            float lockedYaw = controller.YawRadians;
            float lockedPitch = controller.PitchRadians;
            float lockedDistance = controller.DistanceMeters;
            controller.Look(new Vector2(100, 100), 1f, invertY: false);
            controller.Zoom(inward: true);
            Expect(controller.YawRadians == lockedYaw && controller.PitchRadians == lockedPitch &&
                controller.DistanceMeters == lockedDistance,
                "A locked stock camera accepted pointer orbit or zoom.");

            ClientCameraRig orthographic = orbit with
            {
                Projection = ClientCameraProjection.Orthographic,
                CanonicalOrthographicSizeMillimeters = 20_000,
            };
            outputViewport.Size = new Vector2I(800, 600);
            controller.ApplyRig(orthographic, fourThree);
            Expect(camera.Projection == Camera3D.ProjectionType.Orthogonal &&
                Math.Abs(camera.Size - controller.TargetFrameProjection
                    .TargetVerticalExtent) < 0.001,
                "The orthographic rig did not realize its output-viewport native size.");
            Vector3 centerRay = camera.ProjectRayNormal(Center(fourThree));
            Vector3 edgeRay = camera.ProjectRayNormal(new Vector2(
                0, 0));
            Expect(centerRay.DistanceTo(edgeRay) < 0.001f,
                "Orthographic rays were not parallel.");
            float beforeZoomSize = camera.Size;
            float beforeZoomDistance = controller.DistanceMeters;
            controller.Zoom(inward: true);
            Expect(camera.Size < beforeZoomSize &&
                Math.Abs(camera.Size - beforeZoomSize *
                    controller.DistanceMeters / beforeZoomDistance) < 0.001f,
                "An admitted orthographic stepped zoom did not magnify the view.");
            controller.Zoom(inward: false);
            Expect(Math.Abs(camera.Size - beforeZoomSize) < 0.001f,
                "Orthographic zoom-out did not restore the accepted profile lens.");
            ClientCameraRig elevatedPivot = orbit with { PivotHeightMillimeters = 2_300 };
            controller.ApplyRig(elevatedPivot, fourThree);
            controller.UpdatePose();
            Expect(Math.Abs(controller.PivotMeters - 2.3f) < 0.001f,
                "The accepted profile pivot did not own the realized camera target.");
            controller.ApplyRig(orbit, fourThree);
            Expect(camera.Projection == Camera3D.ProjectionType.Perspective,
                "A same-frame orthographic-to-perspective switch retained the old projection.");
            GD.Print($"F1.8 CAMERA RAYS PASS app={application.Width}x{application.Height} " +
                "aspects=16:9,4:3,3:4,64:27 orthographic=pass locked=pass");
        }
        finally
        {
            RemoveChild(outputViewport);
            outputViewport.Free();
        }

        static Vector2 Center(in GameplayFrameLayout layout) => new(
            layout.SelectedOutput.Width / 2f,
            layout.SelectedOutput.Height / 2f);

        static void AssertRays(GodotGameplayCameraRig controller, in GameplayFrameLayout layout)
        {
            Camera3D camera = controller.Camera;
            ClientCanonicalViewProjection desired = controller.TargetFrameProjection;
            float centerX = layout.SelectedOutput.Width / 2f;
            float centerY = layout.SelectedOutput.Height / 2f;
            foreach (Vector2 nominated in new[]
                     {
                         new Vector2(centerX, centerY),
                         new Vector2(layout.SelectedOutput.Width * 0.1f, centerY),
                         new Vector2(layout.SelectedOutput.Width * 0.9f, centerY),
                         new Vector2(centerX, layout.SelectedOutput.Height * 0.1f),
                         new Vector2(centerX, layout.SelectedOutput.Height * 0.9f),
                     })
            {
                Vector3 world = camera.ProjectPosition(nominated, 15f);
                if (camera.UnprojectPosition(world).DistanceTo(nominated) > 0.1f)
                    throw new InvalidOperationException(
                        $"Inside-frame pointer nomination failed at {nominated}.");
            }
            double left = Tangent(camera, new Vector2(0, centerY), horizontal: true);
            double right = Tangent(camera, new Vector2(layout.SelectedOutput.Width, centerY), horizontal: true);
            double top = Tangent(camera, new Vector2(centerX, 0), horizontal: false);
            double bottom = Tangent(camera, new Vector2(centerX, layout.SelectedOutput.Height), horizontal: false);
            double onePixelVertical = 2d * desired.TargetVerticalExtent /
                layout.SelectedOutput.Height;
            double onePixelHorizontal = 2d * desired.TargetHorizontalExtent /
                layout.SelectedOutput.Width;
            if (Math.Abs(Math.Abs(left) - desired.TargetHorizontalExtent) > onePixelHorizontal + 0.005 ||
                Math.Abs(Math.Abs(right) - desired.TargetHorizontalExtent) > onePixelHorizontal + 0.005 ||
                Math.Abs(Math.Abs(top) - desired.TargetVerticalExtent) > onePixelVertical + 0.005 ||
                Math.Abs(Math.Abs(bottom) - desired.TargetVerticalExtent) > onePixelVertical + 0.005)
                throw new InvalidOperationException(
                    $"Frame rays disagree with the target crop: frame={layout.Frame} " +
                    $"observed={left:0.000}/{right:0.000}/{top:0.000}/{bottom:0.000} " +
                    $"target={desired.TargetHorizontalExtent:0.000}/{desired.TargetVerticalExtent:0.000}.");
        }

        static double Tangent(Camera3D camera, Vector2 pixel, bool horizontal)
        {
            Vector3 world = camera.ProjectRayNormal(pixel);
            Vector3 local = camera.GlobalTransform.Basis.Inverse() * world;
            return (horizontal ? local.X : local.Y) / -local.Z;
        }
    }

    private void RunF17WorkspaceAndGumpAcceptance()
    {
        var owner = new Control { Name = "F17WorkspaceAcceptance" };
        owner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(owner);
        var composition = new GodotClientUiComposition(
            owner, ClientPixelSize.Create(1920, 1080));
        GodotClientUiCompositionSnapshot initial = composition.GetSnapshot();
        Expect(initial.Workspace.SelectedOutputShape == ClientPixelSize.Create(1920, 1080) &&
            initial.Workspace.GameplayFrame.Width > 0 &&
            initial.Workspace.UnusedArea > 0 &&
            !composition.ContainsWorldPointer(new Vector2(0, 0)) &&
            composition.ContainsWorldPointer(new Vector2(
                initial.Workspace.ApplicationSize.Width / 2f,
                initial.Workspace.ApplicationSize.Height / 2f)) &&
            initial.WorldHostChildren == 0 && initial.HudHostChildren == 0 &&
            initial.Gumps.OpenWindows == 0,
            $"The F1.7 persistent category/workspace baseline was not clean: {initial}.");

        using GodotSessionUiLease session = composition.BeginSession("F17Test");
        session.WorldHost.AddChild(new Label { Name = "WorldCanary" });
        session.HudHost.AddChild(new Label { Name = "HudCanary" });
        session.ContextMenu.Show("Disclosed target", new Vector2(12, 12), () => { }, () => { });
        Expect(session.ContextMenu.Visible,
            "The F1.7 Context layer could not present a disclosed target menu.");
        composition.SetShellModalVisible(true);
        Expect(!session.ContextMenu.Visible && !session.ContextHost.Visible,
            "A shell modal did not retire/suppress transient Context presentation.");
        composition.SetShellModalVisible(false);

        CompiledUiDefinition firstDefinition = SmallWindow("f17-first", 250, 160);
        CompiledUiDefinition secondDefinition = SmallWindow("f17-second", 270, 170);
        var renderer = new GodotUiRenderer(session.Gumps);
        using var client = new UiClientRuntime(renderer, new UiClientLimits(4, 4));
        UiViewInstanceId first = client.OpenView(new UiOpenView(
            CacheKey(firstDefinition), firstDefinition, new UiModel(firstDefinition)));
        UiViewInstanceId second = client.OpenView(new UiOpenView(
            CacheKey(secondDefinition), secondDefinition, new UiModel(secondDefinition)));
        GodotGumpWindowManagerSnapshot two = session.Gumps.GetSnapshot();
        GodotUiRenderHandle firstHandle = session.Gumps.HandleForAcceptance(first);
        GodotUiRenderHandle secondHandle = session.Gumps.HandleForAcceptance(second);
        Vector2 firstPosition = session.Gumps.GetWindowPosition(firstHandle);
        Vector2 secondPosition = session.Gumps.GetWindowPosition(secondHandle);
        Expect(two.OpenWindows == 2 && two.NativeChildren == 2 && firstPosition != secondPosition,
            "Two modeless gameplay gumps did not coexist with deterministic cascade placement.");
        session.ContextMenu.Show(
            "Transient while gameplay is active", new Vector2(16, 16), () => { }, () => { });
        composition.SetShellModalVisible(true);
        Expect(session.Gumps.GetSnapshot().OpenWindows == 2 &&
            !session.ContextMenu.Visible && !session.ContextHost.Visible,
            "A shell modal destroyed modeless gumps or retained transient Context state.");
        composition.SetShellModalVisible(false);
        Expect(session.Gumps.GetSnapshot().OpenWindows == 2 && session.ContextHost.Visible,
            "Resuming from a shell modal did not preserve gameplay gumps/context ownership.");
        ulong zBefore = two.FrontSequence;
        Expect(session.Gumps.BringToFront(firstHandle) &&
            session.Gumps.GetSnapshot().FrontSequence > zBefore &&
            ReferenceEquals(session.Gumps.HandleAt(1), firstHandle),
            "The first gump could not return to front within the Menus category.");
        Expect(session.Gumps.DragForAcceptance(firstHandle, new Vector2(-2_000, 20_000)) &&
            session.Gumps.GetWindowPosition(firstHandle).X >= 8 &&
            session.Gumps.GetWindowPosition(firstHandle).Y >= 8,
            "Native gump drag did not clamp to the application window.");
        composition.UpdateApplicationSizeForAcceptance(ClientPixelSize.Create(600, 800));
        GodotApplicationWorkspaceSnapshot resized = composition.GetSnapshot().Workspace;
        ClientPixelRect expectedResized = GameplayFrameLayout.MaxFit(
            ClientPixelSize.Create(600, 800), ClientPixelSize.Create(1920, 1080)).Frame;
        Expect(resized.GameplayFrame == expectedResized &&
            session.Gumps.GetWindowPosition(firstHandle).X >= 8 &&
            session.WorldHost.Size == new Vector2(expectedResized.Width, expectedResized.Height),
            "Application resize did not recompute the frame/World host and clamp gumps.");
        client.CloseView(new UiCloseView(first));
        Expect(client.OpenViewCount == 1 && session.Gumps.GetSnapshot().OpenWindows == 1 &&
            ReferenceEquals(session.Gumps.HandleAt(0), secondHandle),
            "Closing one gump disturbed the other modeless view.");
        client.CloseView(new UiCloseView(second));
        Expect(client.OpenViewCount == 0 && session.Gumps.GetSnapshot().OpenWindows == 0,
            "Closed gumps remained in the native manager.");

        var intervals = new ClientFrameIntervalSampler(1_000_000);
        intervals.Observe(0, true);
        intervals.Observe(16_667, true);
        composition.ConfigurePerformance(intervals,
            () => default, "Test");
        composition.CycleDebug(page => page.ToString());
        GodotClientPerformanceHud? performance =
            composition.DebugHost.GetNodeOrNull<GodotClientPerformanceHud>("DebugPerformance");
        Expect(composition.DebugPage == GodotClientDebugPage.Performance &&
            performance is not null &&
            composition.DebugHost.GetChildCount() == 1,
            "The bounded Performance page did not own one native HUD graph.");
        Expect(composition.DebugHost.GetNodeOrNull<Control>(
            "DebugPerformance/PerformanceBody/FrameIntervalGraph") is not null,
            "The Performance HUD did not realize its one custom graph Control.");
        Button captureButton = composition.DebugHost.GetNode<Button>(
            "DebugPerformance/PerformanceBody/PerformanceActions/CapturePerformance30Seconds");
        captureButton.EmitSignal(Button.SignalName.Pressed);
        intervals.Observe(30_016_667, true);
        composition.UpdatePerformanceHud(System.Diagnostics.Stopwatch.GetTimestamp());
        string capturePath = Path.GetTempFileName();
        try
        {
            Expect(performance!.SaveDiagnosticSnapshot(capturePath) == 1,
                "The native Performance snapshot did not export the observed frame.");
            using JsonDocument capture = JsonDocument.Parse(File.ReadAllText(capturePath));
            Expect(capture.RootElement.GetProperty("Format").GetString() ==
                    "UltraSimClientFrameInterval/v1" &&
                capture.RootElement.GetProperty("Samples")[0].GetUInt32() == 30_000_000 &&
                capture.RootElement.GetProperty("CaptureComplete").GetBoolean() &&
                capture.RootElement.GetProperty("IntervalUnit").GetString() == "microseconds",
                "The native Performance capture lost exact raw interval units/content.");
        }
        finally { File.Delete(capturePath); }
        composition.DebugHost.GetNode<Button>(
            "DebugPerformance/PerformanceBody/PerformanceActions/ResetPerformance")
            .EmitSignal(Button.SignalName.Pressed);
        Expect(intervals.Count == 0, "Reset did not clear the finite Performance sample ring.");
        composition.CycleDebug(page => page.ToString());
        composition.CycleDebug(page => page.ToString());
        composition.CycleDebug(page => page.ToString());
        composition.CycleDebug(page => page.ToString());
        Expect(composition.DebugPage == GodotClientDebugPage.Off &&
            composition.GetSnapshot().DebugHostChildren == 0,
            "The finite Debug cycle retained hidden pages after Off.");
        session.Dispose();
        GodotClientUiCompositionSnapshot retired = composition.GetSnapshot();
        Expect(!retired.SessionActive && retired.WorldHostChildren == 0 &&
            retired.HudHostChildren == 0 && retired.ContextHostChildren == 0 &&
            retired.Gumps.OpenWindows == 0,
            "F1.7 session retirement did not restore the persistent category baseline.");
        composition.Dispose();
        owner.QueueFree();

        static CompiledUiDefinition SmallWindow(string key, float width, float height)
        {
            var source = new UiDefinitionBuilder(key);
            source.AddCanvas(canvas => canvas.AddPanel(
                "surface",
                "surface",
                UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, width, height))));
            return UiDefinitionCompiler.Compile(source);
        }
    }

    private void RunNativeAdapterBoundaryAcceptance()
    {
        var toggleSource = new UiDefinitionBuilder("native-toggle-reset");
        toggleSource.DeclareSlot("enabled", UiSlotValueKind.Boolean);
        toggleSource.DeclareAction("toggle", inputKind: UiValueKind.Boolean);
        toggleSource.AddCanvas(canvas => canvas.AddToggle(
            "toggle",
            "enabled",
            "toggle",
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 120, 30))));
        CompiledUiDefinition toggleDefinition = UiDefinitionCompiler.Compile(toggleSource);
        UiSlotId enabled = toggleDefinition.Bindings.RequireSlot("enabled");
        var initial = new UiModel(toggleDefinition);
        initial.SetBoolean(enabled, true);
        int submittedActions = 0;
        using (var toggleClient = new UiClientRuntime(new GodotUiRenderer(this)))
        {
            var toggleLink = new LocalSemanticLink(toggleClient);
            toggleLink.ActionSubmitted += _ => submittedActions++;
            UiViewInstanceId view = toggleLink.OpenView(new UiOpenView(
                CacheKey(toggleDefinition), toggleDefinition, initial));
            CheckBox toggle = Find<CheckBox>(GetNode<Control>($"UiView_{view.Value}"), "Element_toggle");
            Expect(toggle.ButtonPressed, "The native Boolean fixture did not begin pressed.");
            toggleLink.UpdateView(new UiUpdateView(view, new UiModelDelta(
                toggleDefinition.Handle,
                sequence: 1,
                scalarSlots: [new UiScalarSlotPatch(enabled, UiValue.Unset)])));
            Expect(!toggle.ButtonPressed,
                "A true-to-Unset Boolean update retained stale pressed state in Godot.");
            Expect(submittedActions == 0,
                "The programmatic Boolean reset emitted client action intent.");
        }

        var oversizedSource = new UiDefinitionBuilder("native-flow-range");
        oversizedSource.AddCanvas(canvas => canvas.AddVertical(
            "oversized",
            _ => { },
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100)),
            new UiFlowOptions(spacing: float.MaxValue)));
        CompiledUiDefinition oversized = UiDefinitionCompiler.Compile(oversizedSource);
        var oversizedModel = new UiModel(oversized);
        using var oversizedClient = new UiClientRuntime(new GodotUiRenderer(this));
        UiClientRuntimeFaultedException rejected = ExpectThrows<UiClientRuntimeFaultedException>(() =>
            oversizedClient.OpenView(new UiOpenView(
                CacheKey(oversized), oversized, oversizedModel, new UiViewInstanceId(9_001))));
        Expect(rejected.InnerException is InvalidDataException,
            "Oversized native flow constants were not rejected by adapter admission.");
        Expect(GetNodeOrNull<Control>("UiView_9001") is null,
            "Rejected native UI admission retained a partial control root.");
    }

    private void RunOutsidePointerPolicyAcceptance()
    {
        Expect(InventoryUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.PassThrough,
            "Inventory did not retain the default modeless outside-input policy.");
        Expect(CharacterSelectUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.Block,
            "CharacterSelect did not declare its modal input policy.");
        Expect(InformationalSignUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.Close &&
            InformationalSignUi.Definition.OutsideClickAction == InformationalSignUi.Close,
            "InformationalSign did not declare its semantic outside-close action.");

        var model = new UiModel(InformationalSignUi.Definition);
        model.SetText(InformationalSignUi.Title, "Sign");
        model.SetText(InformationalSignUi.Body, "Body");
        model.SetText(InformationalSignUi.CloseLabel, "Close");
        var renderer = new GodotUiRenderer(this);
        using var client = new UiClientRuntime(renderer);
        var link = new LocalSemanticLink(client);
        UiActionRequest submitted = default;
        int submissions = 0;
        link.ActionSubmitted += request =>
        {
            submitted = request;
            submissions++;
        };
        UiViewInstanceId view = link.OpenView(new UiOpenView(
            CacheKey(InformationalSignUi.Definition),
            InformationalSignUi.Definition,
            model));
        Control root = GetNode<Control>($"UiView_{view.Value}");
        Panel surface = Find<Panel>(root, "Element_background");
        Vector2 inside = surface.GetGlobalRect().GetCenter();
        Vector2 outside = surface.GetGlobalRect().End + new Vector2(8, 8);
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = inside,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.UiSurface && submissions == 0,
            "Pointer input over a compiled UI surface leaked through to gameplay.");
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = outside,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.OutsideCloseSubmitted &&
            submissions == 1 && submitted.Instance == view &&
            submitted.Action == InformationalSignUi.Close,
            "An outside sign click did not submit the ordinary semantic Close action.");
    }

    private static UiDefinitionCacheKey CacheKey(CompiledUiDefinition definition) => new(
        "native-test",
        "adapter-boundary",
        protocolVersion: 1,
        definition.Key,
        definition.Version,
        definition.Hash);

    private static TException ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private void RunAcceptance()
    {
        var composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 8));
        using var runtime = new ServerRuntime(
            composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    64,
                    new PersistenceIdentityOptions(new PersistentWorldId(0x80_0080))),
                WorldClockOptions.Default));
        runtime.Initialize();
        runtime.Start();
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, Fixture>(
            (world, generationId, game) =>
            {
                Expect(world.TryInstantiate(game.Content.Character, out Entity alice),
                    "The native acceptance could not create Alice.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.Backpack, alice, 8, out Entity backpack),
                    "The native acceptance could not create Backpack.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.Bread, 5, out Entity bread),
                    "The native acceptance could not create Bread.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.IronSword, 1, out Entity sword),
                    "The native acceptance could not create Iron Sword.");
                PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
                _ = world.GetOrAssignPersistentId(backpack);
                _ = world.GetOrAssignPersistentId(bread);
                _ = world.GetOrAssignPersistentId(sword);
                Expect(world.TryGetPrefabProvenance(alice, out PrefabKey prefab),
                    "The native Alice fixture has no prefab provenance.");
                var account = new AccountId(1);
                game.Accounting.AddAccount(new AccountRecord(account));
                game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-godot"), account);
                game.Accounting.AddCharacter(new CharacterRecord(
                    new CharacterId(1),
                    account,
                    0,
                    "Alice",
                    prefab,
                    new PersistentEntityKey(world.PersistentWorldId, aliceId)));
                return new Fixture(alice, backpack);
            },
            out Fixture fixture),
            "The native acceptance fixture did not run.");

        var renderer = new GodotUiRenderer(this);
        var client = new UiClientRuntime(renderer);
        var link = new LocalSemanticLink(client);
        using var port = new NativeUiPort(link);
        using var admission = new AdmissionService(runtime.Game, composition, port);
        AdmissionResult waiting = admission.JoinAccountBacked(
            new ParticipantId(1),
            new TrustedLocalIdentity("alice-godot"));
        Expect(waiting.IsWaitingForCharacter, "Native account-backed admission did not open CharacterSelect.");
        UiViewInstanceId characterView = waiting.CharacterSelectView;
        Control characterRoot = GetNode<Control>($"UiView_{characterView.Value}");
        Expect(renderer.BlocksGameplayInput &&
            renderer.ResolveGameplayInput(new InputEventKey
            {
                Keycode = Key.W,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.Blocked,
            "CharacterSelect did not block gameplay input while admission was incomplete.");
        VBoxContainer characterRows = Find<VBoxContainer>(characterRoot, "Element_characters");
        Expect(characterRows.GetChildCount() == 1,
            "The native CharacterSelect list did not render Alice.");
        Expect(ContainsLabel(characterRows, "Alice"),
            "The native CharacterSelect row did not show Alice's display name.");
        AdmissionResult hot = admission.ReopenCharacterSelect(new ParticipantId(1));
        Expect(hot.IsWaitingForCharacter, "Native CharacterSelect did not reopen through Admission.");
        UiViewInstanceId hotCharacter = hot.CharacterSelectView;
        Expect(GetNodeOrNull<Control>($"UiView_{hotCharacter.Value}") is not null,
            "The native CharacterSelect definition did not reopen from cache.");
        ReadOnlySpan<UiListRow> hotRows = client.GetRows(hotCharacter, CharacterSelectUi.Characters);
        client.SubmitAction(hotCharacter, CharacterSelectUi.Play, hotRows[0].Id);
        Expect(admission.TryGetSession(new ParticipantId(1), out AdmissionResult entered) &&
            entered.IsEntered && entered.Entity == fixture.Alice,
            "Native CharacterSelect Play did not enter Alice.");

        using var inventory = new InventoryUiController(runtime.Game, port);
        InventoryUiOpenResult opened = inventory.Open(fixture.Backpack);
        Expect(opened.Succeeded, "Native Inventory did not open.");
        UiViewInstanceId inventoryView = opened.View;
        Control inventoryRoot = GetNode<Control>($"UiView_{inventoryView.Value}");
        Panel inventorySurface = Find<Panel>(inventoryRoot, "Element_background");
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = inventorySurface.GetGlobalRect().GetCenter(),
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.UiSurface,
            "Inventory allowed a world click through its visible UI surface.");
        Vector2 outsideInventory = inventorySurface.GetGlobalRect().End + new Vector2(8, 8);
        GodotUiGameplayInputDisposition inventoryOutsideDisposition =
            renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = outsideInventory,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
        Expect(inventoryOutsideDisposition == GodotUiGameplayInputDisposition.Gameplay &&
            !renderer.BlocksGameplayInput,
            $"Inventory did not allow outside gameplay input while remaining open " +
            $"(disposition={inventoryOutsideDisposition}, blocks={renderer.BlocksGameplayInput}).");
        VBoxContainer inventoryRows = Find<VBoxContainer>(inventoryRoot, "Element_items");
        Expect(inventoryRows.GetChildCount() == 2 &&
            ContainsLabel(inventoryRows, "Bread") &&
            ContainsLabel(inventoryRows, "Iron Sword"),
            "The native Inventory view did not show Bread and Iron Sword.");

        UiRowId bread = FindRow(client.GetRows(inventoryView, InventoryUi.Items), "Bread");
        client.SubmitAction(inventoryView, InventoryUi.Select, bread);
        client.SubmitAction(inventoryView, InventoryUi.Split);
        Expect(ReferenceEquals(inventoryRoot, GetNode<Control>($"UiView_{inventoryView.Value}")),
            "Inventory row deltas rebuilt the native root control.");
        Expect(Find<VBoxContainer>(inventoryRoot, "Element_items").GetChildCount() == 3,
            "The native Split delta did not update and add rows.");

        UiRowId sword = FindRow(client.GetRows(inventoryView, InventoryUi.Items), "Iron Sword");
        client.SubmitAction(inventoryView, InventoryUi.Select, sword);
        client.SubmitAction(inventoryView, InventoryUi.Equip);
        VBoxContainer equippedRows = Find<VBoxContainer>(inventoryRoot, "Element_items");
        Expect(equippedRows.GetChildCount() == 2 && !ContainsLabel(equippedRows, "Iron Sword"),
            "The native Equip delta did not remove Iron Sword.");
        Expect(inventory.Close(inventoryView), "The native Inventory view did not close.");
        InventoryUiOpenResult hotInventory = inventory.OpenHot(fixture.Backpack);
        Expect(hotInventory.Succeeded &&
            GetNodeOrNull<Control>($"UiView_{hotInventory.View.Value}") is not null,
            "The native Inventory definition did not reopen from cache.");
        runtime.Stop();
    }

    private static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
    {
        foreach (UiListRow row in rows)
        {
            if (row.Values.Length != 0 && row.Values[0].Text == name) return row.Id;
        }
        throw new InvalidOperationException($"Native inventory row '{name}' was not found.");
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T ??
        throw new InvalidOperationException($"Native control '{name}' was not found.");

    private static bool ContainsLabel(Node root, string text)
    {
        if (root is Label label && (label.Text == text ||
            label.Text.StartsWith(text + " ", StringComparison.Ordinal))) return true;
        foreach (Node child in root.GetChildren())
        {
            if (ContainsLabel(child, text)) return true;
        }
        return false;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct Fixture(Entity Alice, Entity Backpack);

    private sealed class NativeUiPort : IUiSemanticPort, IDisposable
    {
        private readonly LocalSemanticLink _link;
        private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];

        internal NativeUiPort(LocalSemanticLink link)
        {
            _link = link;
            _link.ActionSubmitted += Forward;
        }

        public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
            _actions.Add(instance, handler);
        public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

        public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
        public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
        public void UpdateView(UiUpdateView operation) => _link.UpdateView(operation);
        public bool CloseView(UiCloseView operation) => _link.CloseView(operation);

        public void Dispose()
        {
            _link.ActionSubmitted -= Forward;
            _actions.Clear();
        }

        private void Forward(UiActionRequest request)
        {
            if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
        }
    }
}
