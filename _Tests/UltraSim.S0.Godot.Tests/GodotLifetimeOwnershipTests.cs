using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.StandardUi;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Definitions;
using UltraSim.UI;
using UltraSim.Audio;
using UltraSim.Client.Sound;
using UltraSim.Client.Content;

internal static class GodotLifetimeOwnershipTests
{
    internal static void Run(Node nativeParent)
    {
        var resources = new GodotOwnedResources();
        var node = (MeshInstance3D)GodotPrimitiveSubstrate.CreateActor(ClientActorVisualKind.HumanX, resources);
        Mesh retained = node.Mesh ?? throw new InvalidOperationException("Primitive mesh is missing.");
        Expect(resources.Count == 2, "Primitive mesh and material need an explicit owner.");
        node.Free();
        Expect(retained.NativeInstance != IntPtr.Zero, "Consumer retirement is not wrapper disposal.");
        resources.Dispose();
        resources.Dispose();
        Expect(resources.IsRetired && resources.Count == 0 && retained.NativeInstance == IntPtr.Zero,
            "Owned primitive wrappers did not retire exactly once.");

        var replaced = new GodotOwnedResources();
        var replacedMaterial = replaced.Own(new StandardMaterial3D());
        replaced.Release(replacedMaterial);
        Expect(replacedMaterial.NativeInstance == IntPtr.Zero && replaced.Count == 0,
            "Replaced native wrapper was not retired at its ownership transition.");
        replaced.Dispose();

        using (var ui = new GodotUiRenderer())
        {
            Texture2D icon = ui.DiagnosticMissingIcon;
            Expect(icon.NativeInstance != IntPtr.Zero, "UI fallback icon was not realized.");
            ui.Dispose();
            Expect(icon.NativeInstance == IntPtr.Zero,
                "Session-owned fallback icon survived UI renderer retirement.");
        }

        var tooltipParent = new Control();
        var tooltip = new GodotWorldHoverTooltip(tooltipParent);
        StyleBoxFlat retainedTooltipStyle = tooltip.DiagnosticBackground;
        Expect(retainedTooltipStyle.NativeInstance != IntPtr.Zero,
            "Tooltip style was not live before owner retirement.");
        tooltip.Dispose();
        tooltipParent.Free();
        Expect(retainedTooltipStyle.NativeInstance == IntPtr.Zero,
            "Owned tooltip StyleBoxFlat was left to finalization.");

        var lifetime = new GodotApplicationLifetime();
        lifetime.BeginStartup();
        Expect(lifetime.RequestStop(0), "Startup close was not accepted.");
        lifetime.CompleteStartup();
        Expect(lifetime.Phase == GodotApplicationPhase.StopRequested && !lifetime.CanStartWork,
            "Startup completion resurrected a stopping application.");
        Expect(!lifetime.RequestStop(3) && lifetime.ExitCode == 3, "Duplicate close lost failure status.");
        Expect(lifetime.BeginStop() && !lifetime.BeginStop(), "Retirement was not exclusive.");
        Expect(!lifetime.ShutdownCompletion.IsCompleted, "Shutdown completed before owner retirement.");
        lifetime.CompleteStop();
        Expect(lifetime.ShutdownCompletion.IsCompletedSuccessfully, "Shutdown completion was not published.");

        AdapterControlPublication();
        VisualRoleRealization();
        GenericControlWidgets();
        AudioOwnership(nativeParent);
    }

    private static void AudioOwnership(Node parent)
    {
        var adapter = new GodotClientAudioAdapter(parent);
        AudioStreamWav retained = adapter.GetOwnedStreamForNativeTest(SoundCueId.FootstepGrass) ??
            throw new InvalidOperationException("The native audio clip was not realized.");
        Expect(retained.NativeInstance != IntPtr.Zero, "A generated PCM stream has no native wrapper.");
        GodotClientAudioSnapshot cold = adapter.GetSnapshot();
        Expect(cold.LoadedCues == 14 && cold.SourceBudget == 16 &&
            cold.ActiveSources == 0, "Native audio did not cold-load its finite cue/source set.");
        adapter.SetMasterVolume(.5f);
        adapter.BeginSession();
        adapter.SetListener(new AudioListenerFrame(0, 0, 0));
        Expect(adapter.TryPlay(ClientSoundEvent.UiClick) &&
            adapter.TryPlay(ClientSoundEvent.Footstep, 500, 0, 0),
            "UI and spatial SFX did not reach native players.");
        Expect(adapter.GetCueRequestsForNativeTest(SoundCueId.UiClick) == 1 &&
            adapter.GetCueRequestsForNativeTest(SoundCueId.FootstepGrass) == 1,
            "Native semantic cue requests were not distinguished by category.");
        GodotClientAudioSnapshot playing = adapter.GetSnapshot();
        Expect(playing.ActiveSources >= 3 && playing.ActiveLoops == 1 &&
            playing.MasterVolume == .5f, "Native categories or ambience were not realized.");
        adapter.SetCategoryVolume(AudioCategory.Ambient, 0);
        Expect(adapter.GetSnapshot().ActiveLoops == 0 &&
            adapter.GetSnapshot().AmbientVolume == 0,
            "Muting the Ambient category retained its native loop.");
        adapter.SetCategoryVolume(AudioCategory.Ambient, .6f);
        Expect(adapter.GetSnapshot().ActiveLoops == 1,
            "Restoring Ambient category gain did not resume the session loop.");
        adapter.SetMasterVolume(0);
        Expect(adapter.GetSnapshot().Muted && adapter.GetSnapshot().ActiveSources == 0 &&
            !adapter.TryPlay(ClientSoundEvent.UiClick) &&
            adapter.GetCueRequestsForNativeTest(SoundCueId.UiClick) == 2,
            "Master mute retained native playback or admitted a muted cue.");
        adapter.SetMasterVolume(.8f);
        Expect(adapter.TryPlay(ClientSoundEvent.UiOpen),
            "Unmuting did not permit future native playback.");
        adapter.EndSession();
        Expect(adapter.GetSnapshot().ActiveSources == 0 &&
            adapter.GetSnapshot().ActiveLoops == 0,
            "Session retirement retained native audio source ownership.");
        adapter.Dispose();
        adapter.Dispose();
        Expect(retained.NativeInstance == IntPtr.Zero,
            "An AudioStreamWav wrapper survived its explicit Godot owner retirement.");
    }

    internal static async Task AudioGenerationReplacement(Node parent)
    {
        var adapter = new GodotClientAudioAdapter(parent);
        AudioStreamWav old = adapter.GetOwnedStreamForNativeTest(SoundCueId.UiClick) ??
            throw new InvalidOperationException("The old native clip is missing.");
        Expect(GodotAudioContentBank.TryOpenOwnedClip(SoundCueId.UiClick,
            out ResolvedContentAudioClip selected),
            "The scope-authorized audio clip could not be reopened.");
        Expect(!adapter.TryReplaceClip(SoundCueId.UiOpen, selected),
            "A clip from another semantic cue was accepted.");
        adapter.BeginSession();
        Expect(adapter.TryPlay(ClientSoundEvent.UiClick), "The old clip could not play.");
        Expect(adapter.TryReplaceClip(SoundCueId.UiClick, selected),
            "The new catalog generation could not replace a live clip.");
        Expect(!adapter.TryReplaceClip(SoundCueId.UiClick, selected),
            "Replacement escaped the bounded native retirement quarantine.");
        AudioStreamWav current = adapter.GetOwnedStreamForNativeTest(SoundCueId.UiClick) ??
            throw new InvalidOperationException("The new native clip is missing.");
        Expect(!ReferenceEquals(old, current) && old.NativeInstance != IntPtr.Zero &&
            current.NativeInstance != IntPtr.Zero,
            "A live old stream was disposed before its consumer retirement boundary.");
        // Godot emits ProcessFrame before incrementing its public frame counter.
        // Four signal boundaries cover two completed frames after replacement.
        for (int i = 0; i < 4; i++)
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            adapter.ProcessFrame();
        }
        Expect(old.NativeInstance == IntPtr.Zero && current.NativeInstance != IntPtr.Zero &&
            adapter.TryPlay(ClientSoundEvent.UiClick),
            "The old stream failed to retire or the new stream failed to play.");
        adapter.EndSession();
        adapter.Dispose();
        Expect(current.NativeInstance == IntPtr.Zero,
            "The replacement stream survived its explicit owner retirement.");
    }

    private static void VisualRoleRealization()
    {
        using var assets = new FixtureVisuals();
        Texture2D retained = assets.Texture;
        var source = new UiDefinitionBuilder("p5-visual-native");
        UiSlotReference label = source.DeclareSlot("label", UiSlotValueKind.Text);
        UiSlotReference selected = source.DeclareSlot("selected", UiSlotValueKind.Boolean);
        UiActionReference click = source.DeclareAction("click");
        UiActionReference flip = source.DeclareAction("flip", inputKind: UiValueKind.Boolean);
        source.AddCanvas(canvas =>
        {
            canvas.AddPanel("frame", "ui.silver.frame", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 120, 80)));
            canvas.AddImage("tile", "ui.tile", UiChildLayout.Canvas(new UiCanvasPlacement(4, 4, 32, 32)));
            canvas.AddButton("button", click, label, UiChildLayout.Canvas(new UiCanvasPlacement(40, 4, 70, 28)),
                "ui.button");
            canvas.AddToggle("toggle", selected, flip, UiChildLayout.Canvas(new UiCanvasPlacement(40, 38, 70, 28)),
                "ui.toggle");
        });
        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        UiElementId tileElement = definition.Elements.ToArray().Single(element => element.Key == "tile").Id;
        var semanticReference = new SemanticPresentationReference(new("world"), new("iron-sword"),
            PresentationRoleId.InventoryPortrait);
        var instance = new UiViewInstanceId(10_001);
        var semanticImages = new UiSemanticImageBindings(instance, definition,
            [(tileElement, semanticReference)]);
        Expect(semanticImages.TryGet(instance, definition, tileElement, out var readReference) &&
            readReference == semanticReference &&
            !semanticImages.TryGet(new UiViewInstanceId(10_002), definition, tileElement, out _),
            "Typed semantic image binding crossed a view-instance boundary.");
        bool rejectedNonImage = false;
        try
        {
            _ = new UiSemanticImageBindings(instance, definition,
                [(definition.Elements.ToArray().Single(element => element.Key == "button").Id, semanticReference)]);
        }
        catch (ArgumentException) { rejectedNonImage = true; }
        Expect(rejectedNonImage, "A non-image element accepted a semantic image reference.");
        var model = new UiModel(definition);
        model.SetText(definition.Bindings.RequireSlot("label"), "Apply");
        model.SetBoolean(definition.Bindings.RequireSlot("selected"), true);
        using var renderer = new GodotUiRenderer(assets: assets, semanticImages: semanticImages);
        var handle = (GodotUiRenderHandle)renderer.Create(instance, definition,
            new UiModelView(model), _ => { });
        Control root = handle.Root;
        Control frame = root.FindChild("Element_frame", true, false) as Control ??
            throw new InvalidOperationException("Nine-piece frame absent.");
        TextureRect tile = root.FindChild("Element_tile", true, false) as TextureRect ??
            throw new InvalidOperationException("Tiled image absent.");
        Button button = root.FindChild("Element_button", true, false) as Button ??
            throw new InvalidOperationException("Image-state button absent.");
        CheckBox toggle = root.FindChild("Element_toggle", true, false) as CheckBox ??
            throw new InvalidOperationException("Image-backed toggle absent.");
        Expect(frame.GetChildCount() == 9 && frame.CustomMinimumSize == new Vector2(25, 25) &&
            tile.StretchMode == TextureRect.StretchModeEnum.Tile && button.Icon is not null &&
            toggle.ButtonPressed && toggle.Icon is not null && assets.LastSemanticReference == semanticReference,
            "Neutral P5 visual roles did not realize through native Godot nodes.");
        root.Free();
        renderer.Dispose();
        assets.Dispose();
        Expect(retained.NativeInstance == IntPtr.Zero,
            "New visual-role asset wrapper was left to native finalization.");
    }

    private sealed class FixtureVisuals : IGodotUiVisualResolver, IGodotUiSemanticImageResolver, IDisposable
    {
        private readonly GodotOwnedResources _owned = new();
        internal Texture2D Texture { get; }
        internal FixtureVisuals() => Texture = _owned.Own(GodotPrimitiveSubstrate.CreateMissingIcon());
        public Texture2D? Resolve(string assetRole) => Texture;
        public SemanticPresentationReference LastSemanticReference { get; private set; }
        public Texture2D? Resolve(SemanticPresentationReference reference)
        {
            LastSemanticReference = reference;
            return Texture;
        }
        public UiImageVisual? ResolveImage(string visualRole) =>
            visualRole == "ui.tile" ? new("ui.tile", UiImageMode.Tile) : null;
        public UiNinePieceVisual? ResolveFrame(string visualRole) => visualRole == "ui.silver.frame"
            ? new(Enumerable.Repeat(new UiImageVisual("ui.frame.part"), 9), 12, 12, 12, 12,
                UiFrameFillMode.Tile, UiFrameFillMode.Stretch) : null;
        public UiButtonImageVisual? ResolveButton(string visualRole) => visualRole == "ui.button"
            ? new(new("ui.button.normal"), hover: new("ui.button.hover"), pressed: new("ui.button.pressed")) : null;
        public UiToggleImageVisual? ResolveToggle(string visualRole) => visualRole == "ui.toggle"
            ? new(new("ui.toggle.off"), new("ui.toggle.on")) : null;
        public void Dispose() => _owned.Dispose();
    }

    private static void GenericControlWidgets()
    {
        var registry = new ControlPlaneRegistry();
        var provider = new WidgetProvider();
        Expect(registry.TryRegister(provider, out _, out _), "P5 widget provider failed registration.");
        var principal = new ControlPrincipal("native-widget-test", ControlPrincipalKind.TestAutomation,
            [new("widget.read"), new("widget.write")]);
        var viewer = new ControlPlaneViewer(registry, principal);
        var native = new GodotControlPlaneView(viewer, _ => { });
        var host = new VBoxContainer();
        native.Build(host);
        Expect(host.FindChild("ManagementCategories", true, false) is TabContainer &&
            host.FindChild("ManagementControl_flag", true, false) is HBoxContainer flag &&
            flag.GetChildren().OfType<CheckBox>().Count() == 1 &&
            host.FindChild("ManagementControl_option", true, false) is HBoxContainer option &&
            option.GetChildren().OfType<OptionButton>().Count() == 1 &&
            host.FindChild("ManagementControl_count", true, false) is HBoxContainer count &&
            count.GetChildren().OfType<SpinBox>().Count() == 1 &&
            host.FindChild("ManagementControl_ratio", true, false) is HBoxContainer ratio &&
            ratio.GetChildren().OfType<SpinBox>().Count() == 1 &&
            host.FindChild("ManagementControl_name", true, false) is HBoxContainer name &&
            name.GetChildren().OfType<LineEdit>().Count() == 1 &&
            host.FindChild("ManagementAction_fire", true, false) is HBoxContainer action &&
            action.GetChildren().OfType<LineEdit>().Count() == 1 &&
            host.FindChild("Management_Sample_Table", true, false) is VBoxContainer table &&
            table.GetChildren().OfType<GridContainer>().Single() is GridContainer grid &&
            grid.GetChildCount() == 2 + 32 * 2 &&
            (table.FindChild("ManagementCollectionNavigation", true, false) as HBoxContainer)!.GetChildren()
                .OfType<Button>().Single(button => button.Text == "Next").Disabled == false,
            "Generic management vocabulary did not map to native bounded controls.");
        host.Free();
    }

    private sealed class WidgetProvider : IControlProvider
    {
        private static readonly ControlCapabilityId Read = new("widget.read"), Write = new("widget.write");
        private static readonly ControlCategoryPath Category = new("Sample/Table");
        public WidgetProvider()
        {
            ControlDescriptor Field(string id, ControlValueSchema schema, ControlValue value) =>
                new(new(id), Category, id, "fixture", schema, value, Read, Write);
            Definition = new(new("widget.fixture"),
                [Field("flag", new(ControlValueKind.Bool), ControlValue.Bool(true)),
                 Field("option", new(ControlValueKind.Option, options: [new("one"), new("two")]), ControlValue.Option(new("one"))),
                 Field("count", new(ControlValueKind.Int64, ControlValue.Int64(0), ControlValue.Int64(10)), ControlValue.Int64(2)),
                 Field("ratio", new(ControlValueKind.Float64, ControlValue.Float64(0), ControlValue.Float64(1)), ControlValue.Float64(.5)),
                 Field("name", new(ControlValueKind.Text, maximumText: 8), ControlValue.Text("name"))],
                actions: [new(new("fire"), Category, "fire", "fixture", [new(ControlValueKind.Text, maximumText: 8)], Read, Write)],
                collections: [new(new("rows"), Category, "rows", "fixture", 40,
                    [new(new("cell"), "cell", new(ControlValueKind.Int64))], Read)]);
        }
        public ControlProviderDefinition Definition { get; }
        public ulong Revision => 1;
        public ControlState Capture(ControlId id)
        {
            ControlDescriptor descriptor = Definition.Controls.Single(control => control.Id == id);
            var provenance = new ControlProvenance(ControlProvenanceKind.BuiltInDefault);
            return new(descriptor.DefaultValue, descriptor.DefaultValue, null, provenance, provenance, null, true);
        }
        public ControlValue Capture(DiagnosticId id) => throw new ArgumentException(nameof(id));
        public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) =>
            Enumerable.Range(0, 33).Select(index =>
                new ControlCollectionRow(new("row-" + index), [ControlValue.Int64(index)]));
        public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations) =>
            ControlOwnerResult.Accepted;
        public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id,
            IReadOnlyList<ControlValue> arguments) => ControlOwnerResult.Accepted;
    }

    private static void AdapterControlPublication()
    {
        var current = new GodotAdapterStatus("Complete", GodotApplicationPhase.Starting, false, 4, 6);
        var provider = new GodotAdapterControlProvider(() => current);
        var registry = new ControlPlaneRegistry();
        Expect(registry.TryRegister(provider, out ulong generation, out _), "Native adapter publication was rejected.");
        var reader = new ControlPrincipal("native-reader", ControlPrincipalKind.TestAutomation,
            [GodotAdapterControlProvider.Read, GodotAdapterControlProvider.Select]);
        var denied = new ControlPrincipal("no-adapter-read", ControlPrincipalKind.TestAutomation, []);
        Expect(registry.Discover(denied).Count == 0 &&
            registry.Read(denied, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("phase"), out _) == ControlResultCode.Unauthorized,
            "Adapter state was visible without the read capability.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new ControlId("presentation-implementation"), out var presentation) == ControlResultCode.Accepted &&
            presentation?.Effective.AsOption().Value == "godot" && !presentation.Writable,
            "Physical presentation selection was not fixed/read-only.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new ControlId("audio-implementation"), out var audio) == ControlResultCode.Accepted &&
            audio?.Effective.AsOption().Value == "none",
            "A composition without Audio was falsely published as active.");
        current = current with { Audio = new GodotClientAudioSnapshot(
            16, 1, 1, 0, 0, 14, .8f, false, true, 0, 0, true) };
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new ControlId("audio-implementation"), out audio) == ControlResultCode.Accepted &&
            audio?.Effective.AsOption().Value == "godot",
            "A composition with owned Godot Audio was not published as active.");
        var attempt = registry.Set(new SetControlRequest(
            new(new("p3-adapter-selection"), reader), GodotAdapterControlProvider.Id,
            new("presentation-implementation"), generation, ControlValue.Option(new("godot"))));
        Expect(attempt.Outcome.Code == ControlResultCode.NotWritable,
            "A physical adapter was accepted as a live runtime setting.");
        ulong before = provider.Revision;
        current = current with { Phase = GodotApplicationPhase.Running, SessionActive = true, WorldRootChildren = 5 };
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("session-active"), out var session) == ControlResultCode.Accepted &&
            session?.Value.AsBool() == true && provider.Revision > before,
            "Adapter diagnostic did not track the owner lifecycle/session change.");
        var localOperator = new ControlPrincipal("local-operator", ControlPrincipalKind.LocalOperator,
            [GodotAdapterControlProvider.Read]);
        var operatorViewer = new ControlPlaneViewer(registry, localOperator);
        Expect(operatorViewer.Capture().Any(category => category.Path.Value == "Client/Adapter" &&
            category.Items.Any(item => item.Diagnostic is not null)) &&
            !operatorViewer.SetDraft(GodotAdapterControlProvider.Id, generation,
                new ControlId("presentation-implementation"), ControlValue.Option(new("none"))),
            "The generic Operator viewer bypassed owner-published read-only policy.");
        var operatorHost = new VBoxContainer();
        new GodotControlPlaneView(operatorViewer, _ => { }).Build(operatorHost);
        Expect(operatorHost.FindChild("ManagementControl_presentation-implementation", true, false)
                is HBoxContainer operatorControl &&
            operatorControl.GetChildren().OfType<Button>().Single(button => button.Text == "Apply").Disabled,
            "The generic Operator native viewer made a fixed adapter editable.");
        operatorHost.Free();
        Expect(registry.Retire(GodotAdapterControlProvider.Id, generation), "Adapter publication did not retire.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("phase"), out _) == ControlResultCode.UnknownProvider,
            "Retired adapter diagnostics stayed reachable.");
        Expect(registry.TryRegister(new GodotAdapterControlProvider(() => current),
                out ulong replacement, out _) && replacement > generation &&
            !registry.Retire(GodotAdapterControlProvider.Id, generation) &&
            registry.Retire(GodotAdapterControlProvider.Id, replacement),
            "A stale adapter generation retired its replacement.");
    }

    internal static ArrayMesh[] CaptureTerrain(Node root) => Capture(root,
        name => name == "BaseTerrain" || name.StartsWith("AdditionalTerrain_", StringComparison.Ordinal));

    internal static ArrayMesh[] CaptureActorTools(Node root) => Capture(root,
        name => name is "PrimitiveBlade" or "PrimitiveGuard" or "PrimitiveGrip" or "AcceptedInteractCue");

    internal static void RequireRetired(ArrayMesh[] meshes, string boundary)
    {
        Expect(meshes.Length != 0, $"{boundary}: test retained no owned meshes.");
        foreach (ArrayMesh mesh in meshes)
            Expect(mesh.NativeInstance == IntPtr.Zero, $"{boundary}: owned ArrayMesh was left to finalization.");
    }

    private static ArrayMesh[] Capture(Node root, Func<string, bool> include)
    {
        var meshes = new List<ArrayMesh>();
        Visit(root);
        return meshes.ToArray();
        void Visit(Node node)
        {
            if (node is MeshInstance3D instance && include(node.Name.ToString()) && instance.Mesh is ArrayMesh mesh)
                meshes.Add(mesh);
            for (int index = 0; index < node.GetChildCount(); index++) Visit(node.GetChild(index));
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
