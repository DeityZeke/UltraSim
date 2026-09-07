using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Local;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

public enum CabinHumanBody : byte
{
    XBot = 1,
    YBot = 2,
}

public readonly record struct LocalCabinPresentationSnapshot(
    bool IsInitialized,
    CabinContentVariant ContentVariant,
    ContentHash ContentFingerprint,
    ContentCatalogGenerationId ContentGeneration,
    ContentHash WallTextureHash,
    CabinHumanBody HumanBody,
    string? DisplayName,
    int HumanMeshInstances,
    float CameraPivotMeters,
    float NameplateHeightMeters,
    float EffectsHeightMeters,
    bool AddedPresentationRealized,
    ulong ApplicationSessionEpoch,
    ulong ControlSessionEpoch,
    ulong AuthorityTicks,
    WorldPosition PlayerPosition,
    bool DoorIsOpen,
    bool BreadIsInBackpack,
    int OpenUiViews,
    ClientControlGrammar ControlGrammar,
    ClientCameraProfile CameraProfile,
    float CameraDistance,
    float CameraFovDegrees,
    int RenderDistanceChunks,
    int AuthoredChunks,
    int SemanticResidentChunks,
    int PeakSemanticResidentChunks,
    int RenderedChunks,
    int PeakRenderedChunks,
    string? InitializationError);

/// <summary>
/// Permanent Godot consumer for one Network-free local playable application session.
/// Authority remains behind <see cref="LocalPlayableSession"/>; this node owns only Client Core
/// semantic caches and Godot presentation.
/// </summary>
public partial class LocalCabinClient : Node3D
{
    private enum ControlProfile : byte
    {
        OrbitCommand,
        ThirdPersonDirect,
        IndependentMoveLook,
    }

    private enum SelectionKind : byte
    {
        None,
        Terrain,
        CabinDoor,
        CabinChest,
        SkyTelepad,
        FarGate,
        InformationalSign,
        FallbackLabSign,
        WoodlandAnimal,
    }

    private const int MaximumChatTextCharacters = 256;
    private const float CameraPivotMeters = 1.6f;
    private const float MinimumZoomCameraDistanceMeters = 3f;
    private const float OrbitDefaultPitchRadians = MathF.PI * 0.25f;
    private const float OrbitDefaultCameraDistanceMeters =
        ClientSettingsResolver.DefaultOrbitCameraDistance;
    private const float NameplateHeightMeters = 2.0f;
    private const float EffectsHeightMeters = 0.9f;

    private CabinContentOracle? _contentOracle;
    private CabinResolvedContent? _resolvedContent;
    private string? _saveRoot;
    private CanvasLayer? _shellHudLayer;
    private CanvasLayer? _shellWorldUiLayer;
    private CabinHumanBody _humanBody = CabinHumanBody.XBot;
    private string _displayName = "Local Guest";
    private GameGuestAdmissionRequest _guestAdmission = new(
        GameGuestBody.HumanFemale, "Local Guest");
    // F0.2 acceptance retains the historical Cabin fixture in the maintained application;
    // normal Single Player composition selects the Concept Demo explicitly.
    private bool _useConceptDemoWorld;
    private ResolvedClientSettings? _settings;
    private CabinCatalogAssetResolver? _assetResolver;
    private LocalPlayableSession? _localSession;
    private BoundWorldPack? _boundWorld;
    private ClientWorldSemanticCache? _cache;
    private ClientStructuralResidency? _residency;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientWorldInputSequencer? _sequencer;
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _thirdPerson = new();
    private readonly IndependentMoveLookProfile _independent = new();
    private ClientPresentationChange[] _presentationChanges = [];
    private ClientPresentationBinding[] _presentationBindings = [];
    private ClientSemanticHash[] _semanticHashesBeforeRefresh = [];
    private ClientSemanticChunkHandle[] _actorPresentedChunks = [];
    private ClientActorPresentationChange[] _actorChanges = [];
    private readonly ClientActorPresentationRecord[] _actorRows =
        new ClientActorPresentationRecord[64];
    private ClientPublicPresentationMetadataState? _publicPresentationMetadata;
    private ClientActorPresentationState? _actorState;
    private GodotActorPresenter? _actorPresenter;
    private readonly ClientChatOccurrence[] _chatRows = new ClientChatOccurrence[64];
    private ClientSemanticChunkInfo[] _chatChunkInfo = [];
    private Camera3D? _camera;
    private Label? _status;
    private Label? _details;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private OptionButton? _chatMode;
    private Label? _overheadSpeech;
    private Label? _hoverTooltip;
    private Control? _ownedHudRoot;
    private Control? _ownedWorldUiRoot;
    private Control? _inventoryHost;
    private UiClientRuntime? _uiClient;
    private LocalQueuedUiClient? _uiBridge;
    private Node3D? _humanPresentation;
    private int _humanMeshInstances;
    private float _cameraPivotMeters = CameraPivotMeters;
    private bool _addedPresentationRealized;
    private WorldPosition _player;
    private ClientControlWorldPoint _pointerPoint;
    private bool _pointerPointValid;
    private bool _primaryDown;
    private bool _primaryPressed;
    private bool _primaryReleased;
    private bool _secondaryPressed;
    private bool _cancelPressed;
    private float _cameraYaw = -0.7f;
    private float _cameraPitch = OrbitDefaultPitchRadians;
    private float _cameraDistance = OrbitDefaultCameraDistanceMeters;
    private ControlProfile _profile;
    private bool _profileStopPending;
    private SelectionKind _selection;
    private string _lastOutcome = "Ready";
    private bool _initialized;
    private ClientChatState? _chatState;
    private ulong _chatAppliedPresentationCount;
    private ulong _nextChatSubmission = 1;
    private ulong _nextAdministrativeRequest = 1;
    private ulong _residentGenerationId;
    private ulong _observedCommandSequence;
    private ulong _observedAdministrativeSequence;
    private WorldPublicEntityId _overheadAnchor;
    private ulong _overheadOriginGenerationId;
    private ulong _overheadExpiresAtMilliseconds;
    private int _peakRenderedChunks;
    private int _effectiveRenderDistanceChunks;
    private bool _gameplayInputAllowed = true;
    private bool _shutdown;
    private string? _initializationError;

    public void Configure(
        CabinContentOracle contentOracle,
        CabinResolvedContent resolvedContent,
        string saveRoot,
        CanvasLayer? hudLayer = null,
        CanvasLayer? worldUiLayer = null,
        CabinHumanBody humanBody = CabinHumanBody.XBot,
        ResolvedClientSettings? settings = null,
        bool useConceptDemoWorld = false,
        string? guestDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(contentOracle);
        ArgumentNullException.ThrowIfNull(resolvedContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        if (IsInsideTree() || _contentOracle is not null)
            throw new InvalidOperationException("Cabin composition must be configured before entering the tree.");
        if (humanBody is not (CabinHumanBody.XBot or CabinHumanBody.YBot))
            throw new ArgumentOutOfRangeException(nameof(humanBody));
        _contentOracle = contentOracle;
        _resolvedContent = resolvedContent;
        _saveRoot = saveRoot;
        _shellHudLayer = hudLayer;
        _shellWorldUiLayer = worldUiLayer;
        _humanBody = humanBody;
        _guestAdmission = new GameGuestAdmissionRequest(
            humanBody == CabinHumanBody.XBot
                ? GameGuestBody.HumanFemale
                : GameGuestBody.HumanMale,
            guestDisplayName ?? (humanBody == CabinHumanBody.XBot
                ? "Human Female"
                : "Human Male"));
        _useConceptDemoWorld = useConceptDemoWorld;
        _settings = settings ?? ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
    }

    public void SetGameplayInputAllowed(bool allowed)
    {
        _gameplayInputAllowed = allowed;
        if (!allowed) _actorPresenter?.SetRevealHeld(false);
    }

    public void ApplySettings(ResolvedClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int previousRenderDistance = _settings?.RenderDistanceChunks ?? settings.RenderDistanceChunks;
        _settings = settings;
        ApplyResolvedSettings();
        if (_initialized && previousRenderDistance != settings.RenderDistanceChunks &&
            _localSession is not null && _localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
        {
            RebuildClientProjection(view);
            ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
            GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
            string clamp = _effectiveRenderDistanceChunks == settings.RenderDistanceChunks
                ? string.Empty
                : $" (requested {settings.RenderDistanceChunks}; limited by this world)";
            _lastOutcome = $"Render distance applied: {_effectiveRenderDistanceChunks} chunks{clamp}; " +
                $"({residency.SemanticResidentChunks} resident / {render.ActiveRenderChunks} rendered)";
            UpdateHud();
        }
    }

    public LocalCabinPresentationSnapshot GetPresentationSnapshot()
    {
        LocalPlayableSessionSnapshot local = _localSession?.GetSnapshot() ?? default;
        ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
        GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
        _peakRenderedChunks = Math.Max(_peakRenderedChunks, render.ActiveRenderChunks);
        LocalPlayableAuthorityView authority = default;
        if (_localSession is not null) _ = _localSession.TryGetLatestView(out authority);
        return new LocalCabinPresentationSnapshot(
            _initialized,
            _resolvedContent?.Variant ?? default,
            _resolvedContent?.Catalog.Fingerprint ?? default,
            _resolvedContent?.Catalog.Generation ?? default,
            _assetResolver?.WallTextureHash ?? default,
            _humanBody,
            _displayName,
            _humanMeshInstances,
            _cameraPivotMeters,
            NameplateHeightMeters,
            EffectsHeightMeters,
            _addedPresentationRealized,
            _localSession?.ApplicationSessionEpoch ?? 0,
            _localSession?.ControlSession.Value ?? 0,
            local.AuthorityTicks,
            authority.PlayerPosition,
            authority.DoorIsOpen,
            authority.BreadIsInBackpack,
            _uiClient?.OpenViewCount ?? 0,
            _settings?.ControlGrammar ?? default,
            _settings?.CameraProfile ?? default,
            _cameraDistance,
            _camera?.Fov ?? 0,
            _effectiveRenderDistanceChunks,
            residency.AuthoredChunks,
            residency.SemanticResidentChunks,
            residency.PeakSemanticResidentChunks,
            render.ActiveRenderChunks,
            _peakRenderedChunks,
            _initializationError);
    }

    /// <summary>Permanent D0.1 canary for a live, settings-shaped window rebuild.</summary>
    public LocalCabinPresentationSnapshot RunD01Acceptance()
    {
        if (!_initialized || _settings is null || !_useConceptDemoWorld)
            throw new InvalidOperationException("D0.1 requires the initialized Concept Demo consumer.");
        // Normalize through an actual resize even when D0.0 left the requested radius at one.
        ApplySettings(WithRenderDistance(_settings, 2));
        ApplySettings(WithRenderDistance(_settings, 1));
        LocalCabinPresentationSnapshot before = GetPresentationSnapshot();
        ClientStructuralResidencySnapshot residencyBefore = _residency!.GetSnapshot();
        int terrainTrianglesBefore = CountRenderedTerrainTriangles();
        ClientWorldInputSnapshot inputBefore = _sequencer?.Snapshot() ?? default;
        if (before.RenderDistanceChunks != 1 ||
            before.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
            residencyBefore.DesiredResidentChunks != 25 ||
            before.SemanticResidentChunks < residencyBefore.DesiredResidentChunks ||
            before.RenderedChunks != 9)
            throw new InvalidOperationException(
                $"The default D0.1 working set was unexpected: {before.AuthoredChunks}/" +
                $"{before.SemanticResidentChunks}/{before.RenderedChunks}.");

        ApplySettings(WithRenderDistance(_settings, 2));

        LocalCabinPresentationSnapshot after = GetPresentationSnapshot();
        ClientStructuralResidencySnapshot residencyAfter = _residency!.GetSnapshot();
        int terrainTrianglesAfter = CountRenderedTerrainTriangles();
        ClientWorldInputSnapshot inputAfter = _sequencer?.Snapshot() ?? default;
        if (after.RenderDistanceChunks != 2 ||
            after.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
            residencyAfter.DesiredResidentChunks != 49 ||
            after.SemanticResidentChunks < residencyAfter.DesiredResidentChunks ||
            after.RenderedChunks != 25 ||
            after.PeakSemanticResidentChunks < 49 || after.PeakRenderedChunks < 25 ||
            terrainTrianglesAfter <= terrainTrianglesBefore)
            throw new InvalidOperationException(
                $"The expanded D0.1 working set was unexpected: {after.AuthoredChunks}/" +
                $"{after.SemanticResidentChunks}/{after.RenderedChunks}; terrain triangles " +
                $"{terrainTrianglesBefore}->{terrainTrianglesAfter}.");
        if (inputBefore.ControlSession != inputAfter.ControlSession ||
            inputBefore.CommandsProduced != inputAfter.CommandsProduced ||
            inputBefore.LastSequence != inputAfter.LastSequence)
            throw new InvalidOperationException(
                "A render-distance projection rebuild reset the live control-session sequence.");

        WorldPosition movementStart = _player;
        if (_sequencer is null || !_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(
                        checked((short)-GamePlayerCommand.MaximumAxisMagnitude), 0),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand movement))
            throw new InvalidOperationException("The post-settings movement probe could not be formed.");
        ApplyCommand(movement, false, default, false);
        DriveLocalUntil(() =>
            _localSession!.TryGetLatestView(out LocalPlayableAuthorityView current) &&
            current.LastCommandSequence == movement.Sequence &&
            _player.GlobalXMillimeters < movementStart.GlobalXMillimeters);
        SubmitStopAfterSettings();
        return after;

        void SubmitStopAfterSettings()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand stop))
                throw new InvalidOperationException("The post-settings movement stop could not be formed.");
            ApplyCommand(stop, false, default, false);
            DriveLocalUntil(() =>
                _localSession!.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == stop.Sequence);
        }
    }

    /// <summary>
    /// Permanent D0.2 canary for sparse authored geography and reciprocal authoritative gates.
    /// The two structural clusters are separated by unauthored distance, so each crossing must
    /// retire and rebuild the bounded semantic/presentation working set.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD02Acceptance()
    {
        if (!_initialized || _settings is null || !_useConceptDemoWorld ||
            _boundWorld is null || _cache is null || _residency is null ||
            _window is null || _renderer is null || _localSession is null || _sequencer is null)
            throw new InvalidOperationException("D0.2 requires the initialized Concept Demo consumer.");
        ApplySettings(WithRenderDistance(_settings, 1));
        SpatialChunkKey centralKey = GamePlayableWorld.PlayerStart.ToChunkKey(_boundWorld.Space);
        SpatialChunkKey farKey = GamePlayableWorld.FarGateDestination.ToChunkKey(_boundWorld.Space);
        var gapKey = new SpatialChunkKey(
            _boundWorld.Space,
            (GamePlayableWorld.ConceptMaximumChunk + GamePlayableWorld.FarIslandMinimumChunkX) / 2,
            0);
        if (_boundWorld.ChunkCount != GamePlayableWorld.ConceptChunkCount ||
            _boundWorld.TryGetBaseTerrain(gapKey, out _))
            throw new InvalidOperationException(
                "The Concept Demo does not contain two sparse authored clusters separated by unauthored distance.");

        ClientStructuralResidencySnapshot residencyBefore = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot presentationBefore = _window.GetSnapshot();
        UseGate(GamePlayableWorld.CentralFarGatePosition, GamePlayableWorld.FarGateDestination);
        DriveLocalUntil(() =>
            _player == GamePlayableWorld.FarGateDestination &&
            !_cache.TryGetResidentHandle(centralKey, out _) &&
            _cache.TryGetResidentHandle(farKey, out _) &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor) == 0 &&
            _renderer.GetSnapshot().ActiveRenderChunks == 9);
        ClientStructuralResidencySnapshot farResidency = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot farPresentation = _window.GetSnapshot();
        if (farResidency.DesiredResidentChunks != 25 || farResidency.SemanticResidentChunks != 25 ||
            farResidency.ResetTransitions != residencyBefore.ResetTransitions + 1 ||
            farPresentation.ResetTransitions != presentationBefore.ResetTransitions + 1 ||
            farPresentation.CenterChunk.Coord.X != GamePlayableWorld.FarIslandCenterChunkX)
            throw new InvalidOperationException(
                "The central-to-far gate did not perform one bounded structural/presentation reset.");

        ReleaseInteraction();
        UseGate(GamePlayableWorld.FarReturnGatePosition, GamePlayableWorld.CentralGateDestination);
        DriveLocalUntil(() =>
            _player == GamePlayableWorld.CentralGateDestination &&
            _cache.TryGetResidentHandle(centralKey, out _) &&
            !_cache.TryGetResidentHandle(farKey, out _) &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
            _renderer.GetSnapshot().ActiveRenderChunks == 9);
        ClientStructuralResidencySnapshot returnedResidency = _residency.GetSnapshot();
        ClientPresentationWindowSnapshot returnedPresentation = _window.GetSnapshot();
        if (returnedResidency.DesiredResidentChunks != 25 ||
            returnedResidency.SemanticResidentChunks != 25 ||
            returnedResidency.ResetTransitions != residencyBefore.ResetTransitions + 2 ||
            returnedPresentation.ResetTransitions != presentationBefore.ResetTransitions + 2)
            throw new InvalidOperationException(
                "The far-to-central gate did not retire and rebuild the bounded working set.");
        ReleaseInteraction();
        return GetPresentationSnapshot();

        void UseGate(in WorldPosition gate, in WorldPosition destination)
        {
            WorldPosition expectedDestination = destination;
            RelocateControlledActor(gate);
            SetProfile(ControlProfile.ThirdPersonDirect);
            if (!TrySetPointerWorldPoint(gate.GlobalXMillimeters, gate.GlobalZMillimeters))
                throw new InvalidOperationException("The authored Far Gate had no structural pointer support.");
            Select(new ClientControlWorldPoint(gate.GlobalXMillimeters, gate.GlobalZMillimeters));
            if (_selection != SelectionKind.FarGate ||
                !_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.Interact,
                        true),
                    out GamePlayerCommand command))
                throw new InvalidOperationException("The Far Gate interaction could not be formed.");
            ApplyCommand(command, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == command.Sequence &&
                view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
                _player == expectedDestination);
        }

        void ReleaseInteraction()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand release))
                throw new InvalidOperationException("The Far Gate interaction release could not be formed.");
            ApplyCommand(release, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == release.Sequence);
        }
    }

    private static ResolvedClientSettings WithRenderDistance(
        ResolvedClientSettings settings,
        int renderDistanceChunks) =>
        ClientSettingsResolver.Resolve(new ClientSettingsDocument(new ClientSettingsLayer(
            settings.UiScale,
            settings.MasterVolume,
            settings.PointerSensitivity,
            settings.InvertY,
            settings.ControlGrammar,
            settings.CameraProfile,
            settings.CameraDistance,
            settings.CameraFovDegrees,
            settings.Bindings,
            renderDistanceChunks)));

    public override void _Ready()
    {
        try
        {
            if (_contentOracle is null)
            {
                _contentOracle = CabinContentOracle.Create();
                _resolvedContent = _contentOracle.Default;
                _saveRoot = ProjectSettings.GlobalizePath("user://t0-10-saves");
            }
            _settings ??= ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
            ApplyResolvedSettings();
            CreateSceneFurniture();
            StartFreshWorld();
            CreateInventoryConsumer();
            RealizeAddedContent();
            _initialized = true;
            SetPhysicsProcess(true);
            if (OS.GetCmdlineUserArgs().Contains("--smoke", StringComparer.Ordinal))
                RunF02Acceptance();
            UpdateHud();
            GodotWorldRendererSnapshot render = _renderer?.GetSnapshot() ?? default;
            GD.Print(
                $"T0.10 PLAYABLE CLIENT READY chunks={render.ActiveRenderChunks} " +
                $"player={_player} profile={_profile}");
            if (OS.GetCmdlineUserArgs().Contains("--smoke", StringComparer.Ordinal))
            {
                GD.Print("T0.10 PLAYABLE CLIENT SMOKE PASS");
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Playable client initialization failed: {exception}");
            _initializationError = exception.ToString();
            _lastOutcome = "Initialization failed — see Godot output";
            UpdateHud();
            SetPhysicsProcess(false);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized || _localSession is null || _sequencer is null || _camera is null)
            return;
        try
        {
            UpdatePointerWorldPoint();
            UpdateWorldHover();
            PumpChat();
            _ = _uiBridge?.Pump();
            ClientWorldInputSample physical = GodotWorldInput.Capture(
                _settings ?? throw new InvalidOperationException("Client settings are unavailable."),
                _gameplayInputAllowed && GetWindow().HasFocus() &&
                    (_chatInput is null || !_chatInput.HasFocus()));
            ClientHorizontalCameraBasis basis = CameraBasis();
            ClientControlProfileDecision decision = EvaluateProfile(physical, basis);
            if (decision.SelectionRequested)
                Select(decision.SelectionPoint);
            if (!_sequencer.TryCreateSemanticCommand(decision.Command, out GamePlayerCommand command))
                throw new InvalidOperationException("The selected control profile produced an invalid command.");

            bool faceRequested = TryResolveProfileFacing(
                command,
                decision,
                out ClientControlWorldPoint facingPoint,
                out bool facingRequiresAcceptedLocomotion);
            ApplyCommand(
                command,
                faceRequested,
                facingPoint,
                facingRequiresAcceptedLocomotion);
            SynchronizeProjection();
            UpdateChatPresentation();
            UpdateCamera();
            UpdateHud();
        }
        catch (Exception exception)
        {
            GD.PushError($"Playable client frame failed: {exception}");
            _lastOutcome = "Frame failed — see Godot output";
            SetPhysicsProcess(false);
        }
        finally
        {
            _primaryPressed = false;
            _primaryReleased = false;
            _secondaryPressed = false;
            _cancelPressed = false;
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!_gameplayInputAllowed) return;
        if (inputEvent is InputEventKey reveal && !reveal.Echo && IsControlKey(reveal))
        {
            _actorPresenter?.SetRevealHeld(reveal.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey chatKey && chatKey.Pressed && !chatKey.Echo &&
            _settings is not null &&
            GodotWorldInput.Matches(chatKey, _settings, ClientSemanticAction.Chat))
        {
            if (_chatInput is not null && !_chatInput.HasFocus())
            {
                _chatInput.GrabFocus();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (_chatInput?.HasFocus() == true)
        {
            if (inputEvent is InputEventKey escape && escape.Pressed && !escape.Echo &&
                escape.Keycode == global::Godot.Key.Escape)
            {
                _chatInput.ReleaseFocus();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        switch (inputEvent)
        {
            case InputEventMouseButton mouse:
                if (_settings is not null &&
                    (GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Select) ||
                     GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Destination)))
                {
                    _primaryDown = mouse.Pressed;
                    _primaryPressed |= mouse.Pressed;
                    _primaryReleased |= !mouse.Pressed;
                }
                else if (_settings is not null && mouse.Pressed &&
                    GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.ContextMenu))
                {
                    _secondaryPressed = true;
                }
                else if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed &&
                    _profile != ControlProfile.IndependentMoveLook)
                {
                    _cameraDistance = Math.Max(
                        MinimumZoomCameraDistanceMeters, _cameraDistance - 2f);
                }
                else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed &&
                    _profile != ControlProfile.IndependentMoveLook)
                {
                    _cameraDistance = Math.Min(55f, _cameraDistance + 2f);
                }
                break;
            case InputEventMouseMotion motion when
                (motion.ButtonMask & MouseButtonMask.Middle) != 0 &&
                _profile != ControlProfile.IndependentMoveLook:
                float sensitivity = (_settings?.PointerSensitivity ?? 1f) * 0.006f;
                float invert = _settings?.InvertY == true ? -1f : 1f;
                _cameraYaw -= motion.Relative.X * sensitivity;
                _cameraPitch = Math.Clamp(
                    _cameraPitch - motion.Relative.Y * sensitivity * invert,
                    0.2f,
                    1.35f);
                UpdateCamera();
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key);
                break;
        }
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        SetPhysicsProcess(false);
        _uiBridge?.Dispose();
        _uiBridge = null;
        _uiClient?.Dispose();
        _uiClient = null;
        _actorPresenter?.Dispose();
        _actorPresenter = null;
        _actorState = null;
        _publicPresentationMetadata = null;
        _renderer?.Dispose();
        _renderer = null;
        _residency = null;
        _localSession?.Dispose();
        _localSession = null;
        ReleaseExternalUi(_ownedHudRoot);
        ReleaseExternalUi(_ownedWorldUiRoot);
        _ownedHudRoot = null;
        _ownedWorldUiRoot = null;
        _initialized = false;
    }

    private void StartFreshWorld()
    {
        if (_saveRoot is null || _resolvedContent is null || _contentOracle is null)
            throw new InvalidOperationException("Cabin composition was not configured.");
        _localSession = _useConceptDemoWorld
            ? LocalPlayableSession.CreateConceptDemo(_saveRoot, _guestAdmission)
            : LocalPlayableSession.CreateCabin(_saveRoot, _guestAdmission);
        _localSession.WaitUntilReady(TimeSpan.FromSeconds(10));
        // Command sequencing belongs to the application/control-session lineage. Presentation
        // rebuilds (render-distance changes and Game-generation replacement) must not reset it.
        _sequencer = new ClientWorldInputSequencer(_localSession.ControlSession);
        _boundWorld = _localSession.BoundWorld;
        _chatState = new ClientChatState(
            _localSession.ApplicationSessionEpoch,
            new ClientChatLimits(64, 256, 128));
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local authority did not publish its initial view.");
        _humanBody = view.GuestIdentity.Body switch
        {
            GameGuestBody.HumanFemale => CabinHumanBody.XBot,
            GameGuestBody.HumanMale => CabinHumanBody.YBot,
            _ => throw new InvalidOperationException(
                "The authority published an unsupported guest body."),
        };
        _displayName = view.GuestIdentity.DisplayName ??
            throw new InvalidOperationException("The authority published no guest display name.");
        _publicPresentationMetadata = new ClientPublicPresentationMetadataState(64);
        _publicPresentationMetadata.ResetServerGeneration(view.GenerationId);
        ApplyControlledActorMetadata(in view);
        _actorState = new ClientActorPresentationState(
            ClientActorDefinitionCatalog.ConceptDemo, 64);
        _actorPresenter = new GodotActorPresenter(
            this,
            ClientActorDefinitionCatalog.ConceptDemo,
            64,
            new BundledActorVisualResolver());
        _actorPresenter.SetControlledActor(view.PublicActor);
        RebuildClientProjection(view);
        DriveLocalUntil(() =>
            CountPresentedCharacters() == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.WorldChest) == 1 &&
            (!_useConceptDemoWorld ||
                CountPresentedPrefab(GameWorldPublicPrefabIds.SkyTelepad) == 1 &&
                CountPresentedPrefab(GameWorldPublicPrefabIds.FarGate) == 1 &&
                CountPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign) == 2));
        _lastOutcome = _useConceptDemoWorld
            ? "Fresh Concept Demo island started"
            : "Fresh woods/cabin world started";
    }

    /// <summary>
    /// Permanent D0.3 canary for a real authoritative informational sign. Hover uses only
    /// resident presentation truth; Interact crosses Game admission and the bounded UI bridge.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD03Acceptance()
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _sequencer is null || _uiClient is null || _uiBridge is null ||
            _inventoryHost is null || _hoverTooltip is null)
            throw new InvalidOperationException("D0.3 requires the initialized Concept Demo UI consumer.");
        ValidateInitialNativePresentation();
        if (CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign) != 2 ||
            !TrySetPointerWorldPoint(
                GamePlayableWorld.WelcomeSignPosition.GlobalXMillimeters,
                GamePlayableWorld.WelcomeSignPosition.GlobalZMillimeters))
            throw new InvalidOperationException("The authored welcome sign was not resident and selectable.");

        UpdateWorldHover();
        if (!_hoverTooltip.Visible || _hoverTooltip.Text != "Sign")
            throw new InvalidOperationException("Known sign presentation did not produce its local hover label.");
        Select(_pointerPoint);
        if (_selection != SelectionKind.InformationalSign)
            throw new InvalidOperationException("The known sign presentation was not selected as a sign.");

        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand use))
            throw new InvalidOperationException("The informational-sign interaction could not be formed.");
        ulong before = _localSession.GetSnapshot().UiOperationsApplied;
        ApplyCommand(use, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == use.Sequence &&
            view.LastSelection == LocalPlayableSelection.InformationalSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _uiClient.OpenViewCount == 1 &&
            _localSession.GetSnapshot().UiOperationsApplied > before);

        Control? root = null;
        foreach (Node child in _inventoryHost.GetChildren())
        {
            if (child is Control candidate && candidate.Name.ToString().StartsWith(
                    "UiView_", StringComparison.Ordinal))
            {
                root = candidate;
                break;
            }
        }
        Label? title = root?.FindChild("Element_title", recursive: true, owned: false) as Label;
        Label? body = root?.FindChild("Element_body", recursive: true, owned: false) as Label;
        Button? close = root?.FindChild("Element_close", recursive: true, owned: false) as Button;
        if (title?.Text != GameInformationalSigns.ConceptDemoWelcome.Title ||
            body?.Text != GameInformationalSigns.ConceptDemoWelcome.Body || close is null)
            throw new InvalidOperationException("Godot did not realize the authoritative sign title/body/actions.");

        close!.EmitSignal(BaseButton.SignalName.Pressed);
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        if (_inventoryHost.GetChildCount() != 0)
            throw new InvalidOperationException("Closing the informational sign left stale Godot UI presentation.");
        return GetPresentationSnapshot();
    }

    /// <summary>
    /// Permanent D0.4 canary for presentation-only missing-model/material fallbacks. The four
    /// objects retain immutable World identity and collision regardless of the chosen visual.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD04Acceptance()
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _sequencer is null || _uiClient is null || _inventoryHost is null ||
            _cache is null || _renderer is null || _settings is null)
            throw new InvalidOperationException("D0.4 requires the initialized Concept Demo consumer.");
        if (default(GodotWorldMeshResolution).IsValid ||
            _renderer.FallbackMaterialMode != GodotWorldFallbackMaterialMode.Diagnostic)
            throw new InvalidOperationException("The fallback result or presentation policy did not fail closed.");
        ValidateInitialNativePresentation();
        SpatialChunkKey labKey = GamePlayableWorld.FallbackLabCorrectPosition.ToChunkKey(
            GameWorldState.DefaultSpace);
        if (!_cache.TryGetResidentHandle(labKey, out ClientSemanticChunkHandle handle) ||
            !_renderer.TryGetChunkDiagnostics(handle, out GodotWorldRenderChunkDiagnostics diagnostics) ||
            diagnostics.ModelFallbackGroups != 1 || diagnostics.TextureFallbackGroups != 1 ||
            diagnostics.ModelAndTextureFallbackGroups != 1)
            throw new InvalidOperationException(
                "The resident Fallback Lab did not retain one group for each failure class.");

        MultiMeshInstance3D correct = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabCorrect, GodotWorldAssetAvailability.Available);
        MultiMeshInstance3D modelMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabModelMissing,
            GodotWorldAssetAvailability.ModelMissing);
        MultiMeshInstance3D textureMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabTextureMissing,
            GodotWorldAssetAvailability.TextureMissing);
        MultiMeshInstance3D bothMissing = RequireFallbackLabGroup(
            GameWorldStaticVariants.FallbackLabModelAndTextureMissing,
            GodotWorldAssetAvailability.ModelAndTextureMissing);
        if (correct.Multimesh!.Mesh!.GetInstanceId() != textureMissing.Multimesh!.Mesh!.GetInstanceId() ||
            modelMissing.Multimesh!.Mesh!.GetInstanceId() != bothMissing.Multimesh!.Mesh!.GetInstanceId() ||
            correct.Multimesh.Mesh.GetInstanceId() == modelMissing.Multimesh.Mesh.GetInstanceId() ||
            correct.MaterialOverride is not null || modelMissing.MaterialOverride is not null ||
            textureMissing.MaterialOverride is not ShaderMaterial ||
            bothMissing.MaterialOverride is not ShaderMaterial)
            throw new InvalidOperationException(
                "The Fallback Lab did not preserve model and material failure as independent axes.");

        RelocateControlledActor(GamePlayableWorld.FallbackLabApproachPosition);
        if (!TrySetPointerWorldPoint(
                GamePlayableWorld.FallbackLabSignPosition.GlobalXMillimeters,
                GamePlayableWorld.FallbackLabSignPosition.GlobalZMillimeters))
            throw new InvalidOperationException("The Fallback Lab sign had no selectable support point.");
        Select(_pointerPoint);
        if (_selection != SelectionKind.FallbackLabSign ||
            !_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand use))
            throw new InvalidOperationException("The Fallback Lab sign interaction could not be formed.");
        ApplyCommand(use, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == use.Sequence &&
            view.LastSelection == LocalPlayableSelection.FallbackLabSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _uiClient.OpenViewCount == 1);
        Control? signView = FindOpenUiView();
        Label? title = signView?.FindChild("Element_title", recursive: true, owned: false) as Label;
        Label? body = signView?.FindChild("Element_body", recursive: true, owned: false) as Label;
        Button? close = signView?.FindChild("Element_close", recursive: true, owned: false) as Button;
        if (title?.Text != GameInformationalSigns.FallbackLab.Title ||
            body?.Text != GameInformationalSigns.FallbackLab.Body || close is null)
            throw new InvalidOperationException(
                "Godot did not realize the authoritative Fallback Lab explanation.");
        close.EmitSignal(BaseButton.SignalName.Pressed);
        DriveLocalUntil(() => _uiClient.OpenViewCount == 0);
        return GetPresentationSnapshot();
    }

    /// <summary>
    /// Permanent D0.5 canary proving the accepted Game guest identity, selected body prefab,
    /// controlled presentation, nameplate, and Chat label all agree.
    /// </summary>
    public LocalCabinPresentationSnapshot RunD05Acceptance(
        string expectedDisplayName,
        CabinHumanBody expectedBody)
    {
        if (!_initialized || !_useConceptDemoWorld || _localSession is null ||
            _humanPresentation is null || _chatState is null)
            throw new InvalidOperationException(
                "D0.5 requires the initialized Concept Demo guest consumer.");
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView authority) ||
            authority.GuestIdentity.DisplayName != expectedDisplayName ||
            authority.GuestIdentity.Body != (expectedBody == CabinHumanBody.XBot
                ? GameGuestBody.HumanFemale
                : GameGuestBody.HumanMale) ||
            _displayName != expectedDisplayName || _humanBody != expectedBody)
            throw new InvalidOperationException(
                "The controlled presentation did not consume the authoritative guest identity.");
        if (_humanPresentation.FindChild(
                "Nameplate", recursive: true, owned: false) is not
            Label3D { Text: var nameplate } || nameplate != expectedDisplayName)
            throw new InvalidOperationException(
                "The controlled actor nameplate did not use the accepted guest display name.");
        uint expectedPrefab = expectedBody == CabinHumanBody.XBot
            ? GameWorldPublicPrefabIds.Character
            : GameWorldPublicPrefabIds.CharacterMale;
        uint otherPrefab = expectedBody == CabinHumanBody.XBot
            ? GameWorldPublicPrefabIds.CharacterMale
            : GameWorldPublicPrefabIds.Character;
        if (CountPresentedPrefab(expectedPrefab) != 1 || CountPresentedPrefab(otherPrefab) != 0)
            throw new InvalidOperationException(
                "The authoritative guest body did not select the matching public character prefab.");
        GodotActorPresenterSnapshot actors = _actorPresenter?.GetSnapshot() ?? default;
        if (actors.ActiveActors < 2 || actors.VisibleNameplates != 1)
            throw new InvalidOperationException(
                "The shared actor presenter did not retain the controlled actor and nearby animal.");
        _actorPresenter!.SetRevealHeld(true);
        if (_actorPresenter.GetSnapshot().VisibleNameplates < 2)
            throw new InvalidOperationException(
                "Local Ctrl reveal did not expose already-known eligible actor labels.");
        _actorPresenter.SetRevealHeld(false);
        if (_actorPresenter.GetSnapshot().VisibleNameplates != 1)
            throw new InvalidOperationException(
                "Releasing local Ctrl reveal did not restore normal nameplate policy.");

        ulong before = _chatState.GetSnapshot().Applied;
        SubmitChat("guest identity canary");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied == before + 1);
        int count = _chatState.CopyRecent(_chatRows);
        if (count == 0 || _chatRows[count - 1].DisplayLabel != expectedDisplayName ||
            _chatRows[count - 1].Text != "guest identity canary" ||
            _actorPresenter.GetSnapshot().VisibleOverheadLabels != 1)
            throw new InvalidOperationException(
                "Spatial Chat did not use and anchor the accepted guest display name.");
        return GetPresentationSnapshot();
    }

    private MultiMeshInstance3D RequireFallbackLabGroup(
        ushort variant,
        GodotWorldAssetAvailability availability)
    {
        if (_renderer is null)
            throw new InvalidOperationException("The Fallback Lab renderer is unavailable.");
        string suffix = availability == GodotWorldAssetAvailability.Available
            ? string.Empty
            : $"_Fallback_{availability}";
        string expected = $"Static_{GameWorldStaticDefinitionIds.Rock}_{variant}_" +
            $"{(ushort)(WorldStaticFlags.Renderable | WorldStaticFlags.Collidable)}{suffix}";
        MultiMeshInstance3D? found = _renderer.Root.FindChild(
            expected, recursive: true, owned: false) as MultiMeshInstance3D;
        if (found?.Multimesh is not { InstanceCount: 1, Mesh: not null } ||
            found.CustomAabb.Size.LengthSquared() <= 0f)
            throw new InvalidOperationException($"Fallback Lab native group '{expected}' was incomplete.");
        return found;
    }

    private Control? FindOpenUiView()
    {
        if (_inventoryHost is null) return null;
        foreach (Node child in _inventoryHost.GetChildren())
        {
            if (child is Control candidate && candidate.Name.ToString().StartsWith(
                    "UiView_", StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    private void CreateInventoryConsumer()
    {
        if (_localSession is null || _inventoryHost is null)
            throw new InvalidOperationException("Inventory presentation was composed out of order.");
        _uiClient = new UiClientRuntime(
            new GodotUiRenderer(_inventoryHost),
            new UiClientLimits(8, 8));
        _uiBridge = _localSession.AttachUiClient(_uiClient);
    }

    private void OpenInventory()
    {
        if (_localSession is null) return;
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestOpenInventory(request)
            ? "Inventory requested"
            : "Inventory request queue busy";
    }

    private void RealizeAddedContent()
    {
        if (_resolvedContent?.Variant != CabinContentVariant.Add || _assetResolver?.AddedMaterial is null)
            return;
        var badge = new MeshInstance3D
        {
            Name = "AddedCabinContent",
            Mesh = GodotMeshGeometry.CreateGroundedBox(
                new Vector3(0.8f, 0.12f, 0.8f), _assetResolver.AddedMaterial),
            Position = new Vector3(2.5f, 0.02f, -2.5f),
        };
        AddChild(badge);
        _addedPresentationRealized = true;
    }

    private void RebuildClientProjection(in LocalPlayableAuthorityView view)
    {
        if (_localSession is null || _boundWorld is null || !view.IsReady)
            throw new InvalidOperationException("Authority is not ready.");
        int requestedRenderRadius = _settings?.RenderDistanceChunks ??
            ClientSettingsResolver.MinimumRenderDistanceChunks;
        int renderRadius = Math.Min(
            requestedRenderRadius,
            MaximumRenderRadius(view.PreparedWorld.BaseWorld));
        _effectiveRenderDistanceChunks = renderRadius;
        _renderer?.Dispose();
        ClientWorldSemanticCacheLimits cacheLimits =
            ClientWorldSemanticCacheLimits.ForStructuralViewRadius(
                checked(renderRadius + ClientStructuralResidency.DefaultStructuralFringeChunks),
                spareResidentChunks: 16,
                maximumFrozenStaticsPerChunk: 16,
                maximumMultiFragmentsPerChunk: 16,
                maximumPublicRecordsPerChunk: 16,
                maximumKnownPublicIdentities: 64);
        _cache = new ClientWorldSemanticCache(
            view.PreparedWorld,
            cacheLimits);
        _residency = new ClientStructuralResidency(_cache, renderRadius);

        _assetResolver = new CabinCatalogAssetResolver(_contentOracle!, _resolvedContent!);
        _renderer = new GodotWorldRenderer(
            this,
            _cache,
            GodotWorldRendererLimits.ForStructuralWindow(renderRadius, 5, 32),
            _assetResolver,
            publicBatchFilter: _actorPresenter);
        _window = new GodotSlidingRenderWindow(_cache, _renderer, renderRadius);
        int maximumPresented = _window.MaximumActiveChunks;
        _presentationChanges = new ClientPresentationChange[checked(maximumPresented * 2)];
        _presentationBindings = new ClientPresentationBinding[maximumPresented];
        _semanticHashesBeforeRefresh = new ClientSemanticHash[maximumPresented];
        _actorPresentedChunks = new ClientSemanticChunkHandle[maximumPresented];
        _actorChanges = new ClientActorPresentationChange[128];
        _chatChunkInfo = new ClientSemanticChunkInfo[cacheLimits.MaximumResidentChunks];
        _orbit.Reset();
        _residentGenerationId = view.GenerationId;
        _player = view.PlayerPosition;
        if (_publicPresentationMetadata?.GetSnapshot().ServerGenerationId != view.GenerationId)
        {
            _publicPresentationMetadata?.ResetServerGeneration(view.GenerationId);
            ApplyControlledActorMetadata(in view);
        }
        _actorPresenter?.SetControlledActor(view.PublicActor);
        TrackPresentation(reset: true);
        DrainRenderer();
        SynchronizeActorPresentation(in view);
    }

    private void ApplyControlledActorMetadata(in LocalPlayableAuthorityView view)
    {
        if (_publicPresentationMetadata is null || !view.PublicActor.IsValid ||
            !view.GuestIdentity.IsValid)
            throw new InvalidOperationException("The controlled actor metadata source was incomplete.");
        var metadata = new ClientPublicPresentationMetadata(
            view.GenerationId,
            view.PublicActor,
            1,
            ClientPublicPresentationFlags.NameplateEligible,
            view.GuestIdentity.DisplayName!);
        ClientPublicPresentationApplyStatus applied = _publicPresentationMetadata.TryApply(
            ClientPublicPresentationOperation.Present, in metadata);
        if (applied is not (ClientPublicPresentationApplyStatus.Applied or
                ClientPublicPresentationApplyStatus.Duplicate))
            throw new InvalidOperationException($"Controlled actor metadata was rejected: {applied}.");
    }

    private void SynchronizeActorPresentation(in LocalPlayableAuthorityView view)
    {
        if (_window is null || _cache is null || _actorState is null ||
            _publicPresentationMetadata is null || _actorPresenter is null)
            return;
        if (!_window.TryCopyBindings(_presentationBindings, out int count, out _))
            throw new InvalidOperationException("Actor presentation bindings exceeded capacity.");
        for (int index = 0; index < count; index++)
            _actorPresentedChunks[index] = _presentationBindings[index].SemanticHandle;
        ClientActorPresentationSyncStatus status = _actorState.TrySynchronize(
            _cache,
            _actorPresentedChunks.AsSpan(0, count),
            _publicPresentationMetadata,
            view.GenerationId,
            _actorChanges,
            out int changed,
            out _);
        if (status is not (ClientActorPresentationSyncStatus.Applied or
                ClientActorPresentationSyncStatus.NoChanges))
            throw new InvalidOperationException($"Actor presentation synchronization failed: {status}.");
        _actorPresenter.SetControlledActor(view.PublicActor);
        _actorPresenter.Apply(
            _actorChanges.AsSpan(0, changed), _player, Time.GetTicksMsec());
        if (!_actorPresenter.TryGetActorRoot(view.PublicActor, out _humanPresentation) ||
            _humanPresentation is null)
        {
            _humanMeshInstances = 0;
            return;
        }
        _humanPresentation.Name = $"Controlled{_humanBody}";
        if (_actorState.TryGet(view.PublicActor, out ClientActorPresentationRecord controlled))
            _cameraPivotMeters = controlled.Definition.CameraPivotHeightMillimeters * 0.001f;
        _humanMeshInstances = CountMeshInstances(_humanPresentation);
        if (_humanMeshInstances == 0)
            throw new InvalidOperationException("The controlled actor contains no mesh presentation.");
    }

    private static int MaximumRenderRadius(BoundWorldPack world)
    {
        for (int radius = ClientSettingsResolver.MaximumRenderDistanceChunks;
             radius >= ClientSettingsResolver.MinimumRenderDistanceChunks;
             radius--)
        {
            for (int index = 0; index < world.ChunkCount; index++)
            {
                if (!world.TryGetChunkKey(index, out SpatialChunkKey center))
                    throw new InvalidOperationException("A validated WorldPack lost a chunk key.");
                if (IsComplete(center, radius)) return radius;
            }
        }
        throw new InvalidOperationException(
            "The authored world cannot contain the minimum client presentation window.");

        bool IsComplete(SpatialChunkKey center, int radius)
        {
            for (long x = (long)center.Coord.X - radius;
                 x <= (long)center.Coord.X + radius;
                 x++)
            for (long z = (long)center.Coord.Z - radius;
                 z <= (long)center.Coord.Z + radius;
                 z++)
            {
                if (x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue ||
                    !world.TryGetBaseTerrain(
                        new SpatialChunkKey(world.Space, checked((int)x), checked((int)z)), out _))
                    return false;
            }
            return true;
        }
    }

    private ClientControlProfileDecision EvaluateProfile(
        in ClientWorldInputSample physical,
        in ClientHorizontalCameraBasis basis)
    {
        // OrbitCommand intentionally retains its UO-like no-jump grammar; direct human-control
        // profiles may emit the common Game Jump action edge.
        ClientWorldInputSample admitted = _profile == ControlProfile.OrbitCommand
            ? physical with { HeldActions = physical.HeldActions & ~GamePlayerActions.Jump }
            : physical;
        if (_profileStopPending)
        {
            _profileStopPending = false;
            return new ClientControlProfileDecision(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    admitted.HasFocus ? admitted.HeldActions : GamePlayerActions.None,
                    admitted.HasFocus),
                false,
                default,
                false,
                default);
        }
        return _profile switch
        {
            ControlProfile.OrbitCommand => _orbit.Evaluate(new ClientOrbitCommandSample(
                admitted.HeldActions,
                admitted.HasFocus,
                _player.GlobalXMillimeters,
                _player.GlobalZMillimeters,
                _primaryDown,
                _primaryPressed,
                _primaryReleased,
                _secondaryPressed,
                _cancelPressed,
                _pointerPointValid,
                _pointerPoint,
                checked((long)Time.GetTicksMsec()))),
            ControlProfile.ThirdPersonDirect => AddDirectProfileSelection(
                new ClientControlProfileDecision(
                    new ClientWorldCommandIntent(
                        admitted.HasFocus
                            ? _thirdPerson.Evaluate(admitted.MoveX, admitted.MoveZ, basis)
                            : GameLocomotionUpdate.Stop,
                        admitted.HeldActions,
                        admitted.HasFocus),
                    false,
                    default,
                    false,
                    default)),
            ControlProfile.IndependentMoveLook => AddDirectProfileSelection(_independent.Evaluate(
                admitted.MoveX,
                admitted.MoveZ,
                admitted.HeldActions,
                admitted.HasFocus,
                _pointerPointValid,
                _pointerPoint)),
            _ => throw new InvalidOperationException("Unknown control profile."),
        };
    }

    private ClientControlProfileDecision AddDirectProfileSelection(
        in ClientControlProfileDecision decision) =>
        _primaryReleased && _pointerPointValid
            ? decision with
            {
                SelectionRequested = true,
                SelectionPoint = _pointerPoint,
            }
            : decision;

    private void ApplyCommand(
        in GamePlayerCommand command,
        bool faceLookPoint,
        in ClientControlWorldPoint lookPoint,
        bool facingRequiresAcceptedLocomotion)
    {
        if (_localSession is null)
            return;
        LocalPlayableSelection selection = _selection switch
        {
            SelectionKind.None => LocalPlayableSelection.None,
            SelectionKind.CabinDoor => LocalPlayableSelection.CabinDoor,
            SelectionKind.CabinChest => LocalPlayableSelection.CabinChest,
            SelectionKind.SkyTelepad => LocalPlayableSelection.SkyTelepad,
            SelectionKind.FarGate => LocalPlayableSelection.FarGate,
            SelectionKind.InformationalSign => LocalPlayableSelection.InformationalSign,
            SelectionKind.FallbackLabSign => LocalPlayableSelection.FallbackLabSign,
            _ => LocalPlayableSelection.Unsupported,
        };
        if (!_localSession.TrySubmit(new LocalPlayableCommand(
                command,
                selection,
                faceLookPoint,
                lookPoint,
                facingRequiresAcceptedLocomotion)))
            _lastOutcome = "Local command queue busy";
    }

    private bool TryResolveProfileFacing(
        in GamePlayerCommand command,
        in ClientControlProfileDecision decision,
        out ClientControlWorldPoint facingPoint,
        out bool requiresAcceptedLocomotion)
    {
        GameLocomotionUpdate locomotion = command.Locomotion;
        switch (_profile)
        {
            case ControlProfile.OrbitCommand when
                decision.LookRequested &&
                locomotion.Kind is GameLocomotionUpdateKind.Direct or
                    GameLocomotionUpdateKind.Destination:
                facingPoint = decision.LookPoint;
                requiresAcceptedLocomotion = true;
                return true;
            case ControlProfile.ThirdPersonDirect when
                locomotion.Kind == GameLocomotionUpdateKind.Direct:
                facingPoint = new ClientControlWorldPoint(
                    checked(_player.GlobalXMillimeters + locomotion.DirectX),
                    checked(_player.GlobalZMillimeters + locomotion.DirectZ));
                requiresAcceptedLocomotion = true;
                return true;
            case ControlProfile.IndependentMoveLook when decision.LookRequested:
                facingPoint = decision.LookPoint;
                requiresAcceptedLocomotion = false;
                return true;
            default:
                facingPoint = default;
                requiresAcceptedLocomotion = false;
                return false;
        }
    }

    private void SynchronizeProjection()
    {
        if (_localSession is null || _cache is null || _renderer is null)
            return;
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local authority view was unavailable.");
        if (view.GenerationId != _residentGenerationId)
        {
            RebuildClientProjection(view);
            _selection = SelectionKind.None;
            _lastOutcome = $"Entered authoritative generation {view.GenerationId}";
            return;
        }

        CaptureSemanticHashes();
        _ = _localSession.PumpWorld(_cache);
        QueueChangedPublicChunks();
        _player = view.PlayerPosition;
        ObserveAuthorityOutcomes(view);
        TrackPresentation(reset: false);
        _renderer.ProcessFrame(GodotWorldFrameBudget.Default);
        SynchronizeActorPresentation(in view);
    }

    private void CaptureSemanticHashes()
    {
        if (_cache is null || _window is null) return;
        if (!_window.TryCopyBindings(_presentationBindings, out int count, out _))
            throw new InvalidOperationException("The presentation bindings exceeded their fixed destination.");
        for (int index = 0; index < count; index++)
        {
            ClientPresentationBinding binding = _presentationBindings[index];
            if (!_cache.TryGetInfo(binding.SemanticHandle, out ClientSemanticChunkInfo info))
                throw new InvalidOperationException("A presented semantic chunk was unavailable.");
            _semanticHashesBeforeRefresh[index] = info.SemanticHash;
        }
        if (count < _presentationBindings.Length)
            _presentationBindings[count] = default;
    }

    private void QueueChangedPublicChunks()
    {
        if (_cache is null || _renderer is null) return;
        for (int index = 0; index < _presentationBindings.Length; index++)
        {
            ClientPresentationBinding binding = _presentationBindings[index];
            if (!binding.SemanticHandle.IsValid) break;
            if (!_cache.TryGetResidentHandle(binding.Key, out ClientSemanticChunkHandle handle) ||
                !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo after))
                throw new InvalidOperationException("A presented semantic chunk was unavailable after apply.");
            if (after.SemanticHash != _semanticHashesBeforeRefresh[index])
                _ = _renderer.TryQueuePublicRefresh(handle, out _);
        }
    }

    private void ObserveAuthorityOutcomes(in LocalPlayableAuthorityView view)
    {
        if (view.LastCommandSequence != 0 && view.LastCommandSequence != _observedCommandSequence)
        {
            _observedCommandSequence = view.LastCommandSequence;
            if (view.LastChestAction != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastChestAction == GameWorldActionStatus.Accepted
                    ? "Moved bread between backpack and cabin chest"
                    : view.LastChestAction == GameWorldActionStatus.MissingTarget
                        ? "Select the cabin door or chest before using Interact"
                        : $"Chest use: {view.LastChestAction}";
            }
            else if (view.LastCommandResult.Interaction != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted
                    ? view.LastSelection switch
                    {
                        LocalPlayableSelection.CabinDoor =>
                            $"Cabin door {(view.LastCommandResult.DoorIsOpen ? "opened" : "closed")}",
                        LocalPlayableSelection.SkyTelepad => "Sky Island telepad activated",
                        LocalPlayableSelection.FarGate => "Far Gate activated",
                        LocalPlayableSelection.InformationalSign => "Sign opened",
                        LocalPlayableSelection.FallbackLabSign => "Fallback Lab sign opened",
                        _ => "Interaction accepted",
                    }
                    : $"{view.LastSelection} interaction: {view.LastCommandResult.Interaction}";
            }
            else if (view.LastCommandResult.Jump != GameWorldActionStatus.NotRequested)
            {
                _lastOutcome = view.LastCommandResult.Jump == GameWorldActionStatus.Accepted
                    ? "Jumped"
                    : $"Jump: {view.LastCommandResult.Jump}";
            }
        }
        if (view.LastAdministrativeSequence != 0 &&
            view.LastAdministrativeSequence != _observedAdministrativeSequence)
        {
            _observedAdministrativeSequence = view.LastAdministrativeSequence;
            _lastOutcome = view.LastAdministrativeMessage ??
                $"{view.LastAdministrativeKind}: {(view.LastAdministrativeSucceeded ? "accepted" : "rejected")}";
        }
    }

    private void TrackPresentation(bool reset)
    {
        if (_window is null || _residency is null)
            return;
        SpatialChunkKey center = _residency.GetPresentationCenter(_player);
        ClientPresentationWindowSnapshot current = _window.GetSnapshot();
        if (!reset && current.ActiveChunks != 0 &&
            PresentationWindowsAreDisjoint(current.CenterChunk, center, _window.RenderRadiusChunks))
        {
            if (!_window.TryResetForJump(
                    _player, _presentationChanges, out _, out _,
                    out ClientPresentationWindowError windowResetError,
                    out GodotWorldRenderQueueError rendererResetError))
            {
                _lastOutcome = $"Distant presentation reset: {windowResetError}/{rendererResetError}";
                return;
            }
            DrainRenderer();
            if (!_residency.TryPrepareReset(
                    out ClientStructuralResidencyPlan resetPlan, out _,
                    out ClientStructuralResidencyError residencyResetError))
            {
                _lastOutcome = $"Distant structural reset: {residencyResetError}";
                return;
            }
            _residency.Commit(resetPlan);
        }
        if (!_residency.TryPrepare(
                center,
                reset,
                out ClientStructuralResidencyPlan residencyPlan,
                out _,
                out ClientStructuralResidencyError residencyError))
        {
            _lastOutcome = $"Structural residency boundary: {residencyError}";
            return;
        }
        bool tracked = reset && _window.GetSnapshot().ActiveChunks != 0
            ? _window.TryResetForJump(
                _player, _presentationChanges, out _, out _, out ClientPresentationWindowError error,
                out GodotWorldRenderQueueError rendererError)
            : _window.TryTrackPlayer(
                _player, center, _presentationChanges,
                out _, out _, out error, out rendererError);
        if (!tracked)
        {
            _residency.Cancel(residencyPlan);
            _lastOutcome = $"Presentation boundary: {error}/{rendererError}";
            return;
        }
        _residency.Commit(residencyPlan);
    }

    private static bool PresentationWindowsAreDisjoint(
        SpatialChunkKey first,
        SpatialChunkKey second,
        int radius)
    {
        long maximumSharedCenterDistance = checked(radius * 2L);
        return Math.Abs((long)first.Coord.X - second.Coord.X) > maximumSharedCenterDistance ||
            Math.Abs((long)first.Coord.Z - second.Coord.Z) > maximumSharedCenterDistance;
    }

    private void DrainRenderer()
    {
        if (_renderer is null)
            return;
        for (int frame = 0; frame < 64; frame++)
        {
            GodotWorldRendererSnapshot snapshot = _renderer.GetSnapshot();
            if (snapshot.PendingBuilds == 0 && snapshot.PendingRetirements == 0)
                return;
            _renderer.ProcessFrame(new GodotWorldFrameBudget(16, 16));
        }
        throw new InvalidOperationException("Initial render window did not converge.");
    }

    private void Select(in ClientControlWorldPoint point)
    {
        bool signPresented = _useConceptDemoWorld && IsKnownPresentedPrefabNear(
            GameWorldPublicPrefabIds.InformationalSign, point, radius: 1_500);
        _selection = signPresented && Nearest(
                point, GamePlayableWorld.FallbackLabSignPosition, 1_500)
            ? SelectionKind.FallbackLabSign
            : signPresented && Nearest(point, GamePlayableWorld.WelcomeSignPosition, 1_500)
            ? SelectionKind.InformationalSign
            : Nearest(point, GamePlayableWorld.DoorPosition, 1_750)
            ? SelectionKind.CabinDoor
            : Nearest(point, GamePlayableWorld.ChestPosition, 1_750)
                ? SelectionKind.CabinChest
                : Nearest(point, GamePlayableWorld.SkyTelepadPosition, 1_750)
                    ? SelectionKind.SkyTelepad
                    : Nearest(point, GamePlayableWorld.CentralFarGatePosition, 1_750) ||
                        Nearest(point, GamePlayableWorld.FarReturnGatePosition, 1_750)
                        ? SelectionKind.FarGate
                        : Nearest(point, GamePlayableWorld.AnimalPosition, 1_750)
                            ? SelectionKind.WoodlandAnimal
                            : SelectionKind.Terrain;
        _lastOutcome = _profile == ControlProfile.OrbitCommand && _selection == SelectionKind.Terrain
            ? "Terrain selected — hold LMB to steer or double-click to travel"
            : $"Selected {_selection} at ({point.XMillimeters / 1000d:F1}, " +
                $"{point.ZMillimeters / 1000d:F1}) m";
    }

    private void UpdateWorldHover()
    {
        if (_hoverTooltip is null)
            return;
        bool show = _useConceptDemoWorld && _pointerPointValid &&
            IsKnownPresentedPrefabNear(
                GameWorldPublicPrefabIds.InformationalSign, _pointerPoint, radius: 1_500);
        _hoverTooltip.Visible = show;
        if (!show)
            return;
        _hoverTooltip.Text = "Sign";
        _hoverTooltip.Position = GetViewport().GetMousePosition() + new Vector2(14, 18);
    }

    private bool IsKnownPresentedPrefabNear(
        uint prefabId,
        in ClientControlWorldPoint point,
        long radius)
    {
        if (_cache is null || CountPresentedPrefab(prefabId) == 0 ||
            !_cache.TryCopyResidentInfo(_chatChunkInfo, out int count, out _))
            return false;
        Int128 radiusSquared = (Int128)radius * radius;
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!_cache.TryGetResidentView(
                    _chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
            {
                if (record.PrefabId != prefabId)
                    continue;
                Int128 dx = (Int128)point.XMillimeters - record.Position.GlobalXMillimeters;
                Int128 dz = (Int128)point.ZMillimeters - record.Position.GlobalZMillimeters;
                if (dx * dx + dz * dz <= radiusSquared)
                    return true;
            }
        }
        return false;
    }

    private void UpdatePointerWorldPoint()
    {
        if (_camera is null)
            return;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 origin = _camera.ProjectRayOrigin(mouse);
        Vector3 direction = _camera.ProjectRayNormal(mouse);
        if (Math.Abs(direction.Y) < 0.0001f)
        {
            _pointerPointValid = false;
            return;
        }
        float distance = -origin.Y / direction.Y;
        if (distance <= 0)
        {
            _pointerPointValid = false;
            return;
        }
        Vector3 hit = origin + direction * distance;
        long globalX = checked(_player.GlobalXMillimeters +
            (long)Math.Round(hit.X * 1000d, MidpointRounding.AwayFromZero));
        long globalZ = checked(_player.GlobalZMillimeters +
            (long)Math.Round(hit.Z * 1000d, MidpointRounding.AwayFromZero));
        _ = TrySetPointerWorldPoint(globalX, globalZ);
    }

    private bool TrySetPointerWorldPoint(long globalXMillimeters, long globalZMillimeters)
    {
        _pointerPoint = new ClientControlWorldPoint(globalXMillimeters, globalZMillimeters);
        _pointerPointValid = WorldPosition.TryFromGlobalMillimeters(
                globalXMillimeters,
                _player.YMillimeters,
                globalZMillimeters,
                out WorldPosition candidate) &&
            _cache is not null &&
            _cache.PreparedWorld.TryFindNearestSupport(
                candidate,
                _player.YMillimeters,
                out WorldTerrainColumnSurface support) == WorldTerrainColumnQueryStatus.Complete &&
            support.IsValid && support.IsSupport;
        return _pointerPointValid;
    }

    private ClientHorizontalCameraBasis CameraBasis()
    {
        if (_camera is null)
            return ClientHorizontalCameraBasis.WorldAxes;
        Vector3 right = _camera.GlobalTransform.Basis.X;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        right.Y = 0;
        forward.Y = 0;
        right = right.Normalized();
        forward = forward.Normalized();
        return new ClientHorizontalCameraBasis(right.X, right.Z, forward.X, forward.Z);
    }

    private void UpdateCamera()
    {
        if (_camera is null)
            return;
        float horizontal = MathF.Cos(_cameraPitch) * _cameraDistance;
        Vector3 pivot = new(0, _cameraPivotMeters, 0);
        _camera.Position = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance + _cameraPivotMeters,
            MathF.Cos(_cameraYaw) * horizontal);
        _camera.LookAt(pivot, Vector3.Up);
    }

    private void HandleKey(InputEventKey input)
    {
        if (_settings is not null &&
            GodotWorldInput.Matches(input, _settings, ClientSemanticAction.OpenInventory))
        {
            OpenInventory();
            return;
        }
        switch (input.Keycode)
        {
            case global::Godot.Key.Escape:
                _cancelPressed = true;
                break;
            case global::Godot.Key.F1:
            case global::Godot.Key.Key1:
                SetProfile(ControlProfile.OrbitCommand);
                break;
            case global::Godot.Key.F2:
            case global::Godot.Key.Key2:
                SetProfile(ControlProfile.ThirdPersonDirect);
                break;
            case global::Godot.Key.F3:
            case global::Godot.Key.Key3:
                SetProfile(ControlProfile.IndependentMoveLook);
                break;
            case global::Godot.Key.F5:
                SaveWorld();
                break;
            case global::Godot.Key.F9:
                LoadWorld();
                break;
        }
    }

    private static bool IsControlKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta ||
        input.PhysicalKeycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta;

    private void ApplyResolvedSettings()
    {
        if (_settings is null) return;
        _profile = _settings.ControlGrammar switch
        {
            ClientControlGrammar.OrbitCommand => ControlProfile.OrbitCommand,
            ClientControlGrammar.ThirdPersonDirect => ControlProfile.ThirdPersonDirect,
            ClientControlGrammar.IndependentMoveLook => ControlProfile.IndependentMoveLook,
            _ => throw new InvalidOperationException("The resolved control grammar is unsupported."),
        };
        ApplyCameraProfileDefaults(_settings.CameraProfile);
        _cameraDistance = _settings.CameraDistance;
        if (_camera is not null) _camera.Fov = _settings.CameraFovDegrees;
        if (_ownedHudRoot is not null) _ownedHudRoot.Scale = Vector2.One * _settings.UiScale;
        if (_initialized) _profileStopPending = true;
        UpdateCamera();
    }

    private void ApplyCameraProfileDefaults(ClientCameraProfile profile)
    {
        switch (profile)
        {
            case ClientCameraProfile.Orbit:
                _cameraYaw = -0.7f;
                _cameraPitch = OrbitDefaultPitchRadians;
                break;
            case ClientCameraProfile.ThirdPersonFollow:
                _cameraYaw = -0.7f;
                _cameraPitch = 0.34f;
                break;
            case ClientCameraProfile.IndependentMoveLook:
                _cameraYaw = -MathF.PI * 0.25f;
                _cameraPitch = 0.61548f;
                break;
            default:
                throw new InvalidOperationException("The resolved camera profile is unsupported.");
        }
    }

    private void SetProfile(ControlProfile profile)
    {
        if (_profile == profile)
            return;
        _profile = profile;
        _orbit.Reset();
        _thirdPerson.Reset();
        _independent.Reset();
        _primaryDown = false;
        _primaryPressed = false;
        _primaryReleased = false;
        _profileStopPending = true;
        ApplyProfileCameraDefaults();
        UpdateCamera();
        _lastOutcome = $"Control profile: {profile}";
    }

    public void RunF02Acceptance()
    {
        if (_camera is null || _renderer is null || _localSession is null || _sequencer is null)
            throw new InvalidOperationException("The launch smoke started before composition completed.");

        Vector3 orbitCamera = _camera.Position;
        SetProfile(ControlProfile.ThirdPersonDirect);
        Vector3 thirdPersonCamera = _camera.Position;
        SetProfile(ControlProfile.IndependentMoveLook);
        Vector3 independentCamera = _camera.Position;
        SetProfile(ControlProfile.OrbitCommand);
        if (!(thirdPersonCamera.Length() < orbitCamera.Length() &&
            thirdPersonCamera.Y < orbitCamera.Y &&
            independentCamera.DistanceTo(thirdPersonCamera) > 0.5f &&
            Mathf.IsEqualApprox(_cameraPitch, OrbitDefaultPitchRadians) &&
            Mathf.IsEqualApprox(_cameraDistance, OrbitDefaultCameraDistanceMeters)))
            throw new InvalidOperationException("Control profile switches did not realize distinct camera modes.");

        ValidateCatalogAndHumanPresentation();
        ProbePlayableChat();
        ProbePlayableInventory();
        ProbeDoorAndChestInteraction();
        ProbeAuthoritativeJump();

        ProbeMovementFacing(
            ControlProfile.OrbitCommand,
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MinimumAxisMagnitude, 0),
            new ClientControlWorldPoint(
                _player.GlobalXMillimeters - 1_000,
                _player.GlobalZMillimeters),
            WorldYaw.ThreeQuarterTurn);
        ProbeMovementFacing(
            ControlProfile.ThirdPersonDirect,
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            default,
            WorldYaw.QuarterTurn);
        SetProfile(ControlProfile.OrbitCommand);

        int before = CountPresentedCharacters();
        RelocateControlledActor(GamePlayableWorld.ClearCrossChunkPosition);
        DriveLocalUntil(() => _player.Chunk == GamePlayableWorld.ClearCrossChunkPosition.Chunk &&
            CountPresentedCharacters() == 1);
        int afterCrossing = CountPresentedCharacters();
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart &&
            CountPresentedCharacters() == 1);
        int afterReturn = CountPresentedCharacters();
        if (before != 1 || afterCrossing != 1 || afterReturn != 1)
            throw new InvalidOperationException(
                $"Public presentation ghosted across a chunk move: {before}/{afterCrossing}/{afterReturn}.");

        int authoredMaximumChunk = _useConceptDemoWorld
            ? GamePlayableWorld.ConceptMaximumChunk
            : GamePlayableWorld.MaximumChunk;
        int renderRadius = _effectiveRenderDistanceChunks;
        var edge = new WorldPosition(
            authoredMaximumChunk,
            authoredMaximumChunk,
            15_000,
            14_000,
            0);
        RelocateControlledActor(edge);
        DriveLocalUntil(() => _player.Chunk == edge.Chunk &&
            _player.Local.XMillimeters == edge.Local.XMillimeters);
        SpatialChunkKey expectedCenter = ChunkKey(
            authoredMaximumChunk - renderRadius,
            authoredMaximumChunk - renderRadius);
        ClientPresentationWindowSnapshot edgeWindow = _window?.GetSnapshot() ?? default;
        if (_player.Chunk != edge.Chunk || edgeWindow.CenterChunk != expectedCenter ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.X, -15f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14f))
            throw new InvalidOperationException(
                "The finite structural window detached the camera from the edge-chunk player origin.");

        var fartherInsideEdge = new WorldPosition(
            authoredMaximumChunk,
            authoredMaximumChunk,
            15_500,
            14_500,
            0);
        RelocateControlledActor(fartherInsideEdge);
        DriveLocalUntil(() =>
            _player.Chunk == fartherInsideEdge.Chunk &&
            _player.Local.XMillimeters == fartherInsideEdge.Local.XMillimeters &&
            _player.Local.ZMillimeters == fartherInsideEdge.Local.ZMillimeters);
        if (!Mathf.IsEqualApprox(_renderer.Root.Position.X, -15.5f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14.5f))
            throw new InvalidOperationException(
                "Same-edge-chunk movement did not keep the camera centered on the player.");

        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart);

        void ProbeMovementFacing(
            ControlProfile profile,
            in GameLocomotionUpdate locomotion,
            in ClientControlWorldPoint lookPoint,
            WorldYaw expected)
        {
            SetProfile(profile);
            var intent = new ClientWorldCommandIntent(
                locomotion,
                GamePlayerActions.None,
                true);
            if (!_sequencer.TryCreateSemanticCommand(intent, out GamePlayerCommand command))
                throw new InvalidOperationException($"{profile} facing probe could not form a command.");
            var decision = new ClientControlProfileDecision(
                intent,
                false,
                default,
                profile == ControlProfile.OrbitCommand,
                lookPoint);
            if (!TryResolveProfileFacing(
                    command,
                    decision,
                    out ClientControlWorldPoint facingPoint,
                    out bool requiresAcceptedLocomotion))
                throw new InvalidOperationException($"{profile} did not produce movement-facing intent.");
            ApplyCommand(command, true, facingPoint, requiresAcceptedLocomotion);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == command.Sequence &&
                current.PlayerOrientation == expected);
            if (ReadPlayerOrientation() != expected)
                throw new InvalidOperationException(
                    $"{profile} did not face its accepted movement direction.");
        }
    }

    /// <summary>
    /// D0.0 permanent-client journey over the first physical Concept Demo island. Administrative
    /// relocation is used only to choose deterministic starting points; jump, telepad interaction,
    /// falling, landing, and recovery cross the ordinary local Game command/semantic boundary.
    /// </summary>
    public void RunD00Acceptance()
    {
        if (_boundWorld is null || _cache is null || _renderer is null ||
            _localSession is null || _sequencer is null || _camera is null)
            throw new InvalidOperationException("The Concept Demo smoke started before composition completed.");
        if (!_useConceptDemoWorld)
            throw new InvalidOperationException("The Concept Demo smoke requires the Concept Demo world fixture.");
        if (_settings is null)
            throw new InvalidOperationException("The Concept Demo settings are unavailable.");
        ApplySettings(WithRenderDistance(_settings, 1));

        if (!WorldPosition.TryFromGlobalMillimeters(63_000, 0, 60_000, out WorldPosition outerVoid) ||
            _boundWorld.ProbeBaseTerrainSurface(outerVoid).Status != BaseTerrainSurfaceStatus.Absent ||
            _boundWorld.ProbeBaseTerrainSurface(GamePlayableWorld.SkyTelepadDestination).Status !=
                BaseTerrainSurfaceStatus.Absent)
            throw new InvalidOperationException("The central island/sky gap is not intentional BaseTerrain void.");

        Span<WorldTerrainColumnSurface> column = stackalloc WorldTerrainColumnSurface[8];
        if (_boundWorld.TryCopyTerrainColumnSurfaces(
                GamePlayableWorld.CaveLedgePoint, column, out int caveCount, out _) !=
                WorldTerrainColumnQueryStatus.Complete || caveCount != 4 ||
            !column[..caveCount].Contains(new WorldTerrainColumnSurface(
                GamePlayableWorld.MountainSurface,
                WorldTerrainSurfaceFace.Bottom,
                5_000,
                GameTerrainMaterialIds.Rock)) ||
            !column[..caveCount].Contains(new WorldTerrainColumnSurface(
                GamePlayableWorld.CaveLedgeSurface,
                WorldTerrainSurfaceFace.Top,
                3_000,
                GameTerrainMaterialIds.Rock)))
            throw new InvalidOperationException("The MountainShell/preserved-base cave column is incomplete.");
        if (_boundWorld.TryCopyTerrainColumnSurfaces(
                GamePlayableWorld.SkyTelepadDestination, column, out int skyCount, out _) !=
                WorldTerrainColumnQueryStatus.Complete || skyCount != 2 ||
            column[0].SurfaceId != GamePlayableWorld.SkyIslandSurface ||
            column[0].Face != WorldTerrainSurfaceFace.Top ||
            column[0].HeightMillimeters != 3_500)
            throw new InvalidOperationException("The detached Sky Island shell is incomplete.");

        ValidateRenderedShell(new SpatialChunkKey(GameWorldState.DefaultSpace, -1, 0), 2);
        ValidateRenderedShell(new SpatialChunkKey(GameWorldState.DefaultSpace, 1, 0), 1);

        // Enter the mountain through its authored south opening. The preserved BaseTerrain remains
        // the cave floor while the MountainShell Bottom is real overhead clearance geometry.
        RelocateControlledActor(GamePlayableWorld.CaveEntranceApproach);
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(
                        0, GamePlayerCommand.MaximumAxisMagnitude),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand enterCave))
            throw new InvalidOperationException("The mountain cave traversal could not form a command.");
        ApplyCommand(enterCave, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == enterCave.Sequence &&
            _player.GlobalZMillimeters >= GamePlayableWorld.CaveFloorPoint.GlobalZMillimeters &&
            _player.YMillimeters < 5_000);
        SubmitStop();

        // Third-person profile: cross the intentional gap by authoritative jump/air control.
        RelocateControlledActor(GamePlayableWorld.SkyJumpStart);
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
                    GamePlayerActions.Jump,
                    true),
                out GamePlayerCommand jump))
            throw new InvalidOperationException("The Sky Island jump could not form a command.");
        ApplyCommand(jump, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == jump.Sequence &&
            view.LastCommandResult.Jump == GameWorldActionStatus.Accepted &&
            _player.YMillimeters == GamePlayableWorld.SkyTelepadDestination.YMillimeters &&
            _player.GlobalXMillimeters >= 25_000);
        SubmitStop();

        // Pointer command/selection resolves against the actual nearby structural shell rather
        // than requiring BaseTerrain beneath a detached island.
        const long skyPointerX = 29_000;
        const long skyPointerZ = 9_000;
        if (!TrySetPointerWorldPoint(skyPointerX, skyPointerZ))
            throw new InvalidOperationException("A Sky Island structural pointer point was rejected.");
        SetProfile(ControlProfile.OrbitCommand);
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryDown = true;
        _primaryPressed = true;
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryPressed = false;
        _primaryDown = false;
        _primaryReleased = true;
        ClientControlProfileDecision skyTap = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryReleased = false;
        _primaryDown = true;
        _primaryPressed = true;
        ClientControlProfileDecision skyDoubleClick = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryPressed = false;
        _primaryDown = false;
        if (!skyTap.SelectionRequested ||
            skyDoubleClick.Command.Locomotion.Kind != GameLocomotionUpdateKind.Destination ||
            skyDoubleClick.Command.Locomotion.DestinationXMillimeters != skyPointerX ||
            skyDoubleClick.Command.Locomotion.DestinationZMillimeters != skyPointerZ)
            throw new InvalidOperationException(
                "OrbitCommand could not select or form a destination on the detached Sky Island.");

        // Orbit profile: Jump is absent from its grammar; the selected public telepad invokes the
        // server-owned destination and the accepted relocation path.
        RelocateControlledActor(GamePlayableWorld.SkyTelepadPosition);
        SetProfile(ControlProfile.OrbitCommand);
        ClientControlProfileDecision orbitJump = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.Jump, true), CameraBasis());
        if ((orbitJump.Command.HeldActions & GamePlayerActions.Jump) != 0)
            throw new InvalidOperationException("OrbitCommand unexpectedly admitted a Jump action.");
        SetProfile(ControlProfile.ThirdPersonDirect);
        _ = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _ = TrySetPointerWorldPoint(
            GamePlayableWorld.SkyTelepadPosition.GlobalXMillimeters,
            GamePlayableWorld.SkyTelepadPosition.GlobalZMillimeters);
        _primaryReleased = true;
        ClientControlProfileDecision directSelection = EvaluateProfile(
            new ClientWorldInputSample(0, 0, GamePlayerActions.None, true), CameraBasis());
        _primaryReleased = false;
        if (!directSelection.SelectionRequested)
            throw new InvalidOperationException(
                "ThirdPersonDirect did not expose pointer selection for interaction.");
        Select(directSelection.SelectionPoint);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.Interact,
                    true),
                out GamePlayerCommand telepad))
            throw new InvalidOperationException("The Sky Island telepad interaction could not be submitted.");
        ApplyCommand(telepad, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == telepad.Sequence &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            _player == GamePlayableWorld.SkyTelepadDestination);

        // Walking east eventually leaves both the Sky Island and central sheet. Game owns the
        // below-envelope recovery and clears retained locomotion at the safe occupied start point.
        SetProfile(ControlProfile.ThirdPersonDirect);
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand walkOff))
            throw new InvalidOperationException("The Concept Demo fall could not form a command.");
        ApplyCommand(walkOff, false, default, false);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart);
        SetProfile(ControlProfile.OrbitCommand);

        void ValidateRenderedShell(SpatialChunkKey key, int expectedFragments)
        {
            if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                !_renderer.TryGetChunkDiagnostics(handle, out GodotWorldRenderChunkDiagnostics diagnostics) ||
                diagnostics.AdditionalTerrainSurfaceFragmentCount != expectedFragments ||
                diagnostics.AdditionalTerrainTriangleCount <= 0)
                throw new InvalidOperationException($"Concept Demo shell chunk {key} was not realized.");
        }

        void SubmitStop()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand stop))
                throw new InvalidOperationException("The Sky Island landing stop could not form a command.");
            ApplyCommand(stop, false, default, false);
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == stop.Sequence);
        }
    }

    private void ValidateCatalogAndHumanPresentation()
    {
        LocalCabinPresentationSnapshot snapshot = GetPresentationSnapshot();
        string expected = snapshot.ContentVariant switch
        {
            CabinContentVariant.Default => CabinContentFingerprints.DefaultCatalog,
            CabinContentVariant.Replace => CabinContentFingerprints.ReplaceCatalog,
            CabinContentVariant.Add => CabinContentFingerprints.AddCatalog,
            _ => throw new InvalidOperationException("The Cabin content variant is invalid."),
        };
        if (_resolvedContent is null ||
            !_resolvedContent.Catalog.TryGet(CabinContentIds.WallMaterial, out ResolvedContentEntry? wall) ||
            wall!.ReferencedFiles.Count != 1)
            throw new InvalidOperationException("The resolved Cabin wall material is unavailable.");
        if (snapshot.ContentFingerprint.ToString() != expected ||
            snapshot.WallTextureHash != wall.ReferencedFiles[0] || snapshot.HumanMeshInstances == 0 ||
            !Mathf.IsEqualApprox(snapshot.CameraPivotMeters, CameraPivotMeters) ||
            !Mathf.IsEqualApprox(snapshot.NameplateHeightMeters, NameplateHeightMeters) ||
            !Mathf.IsEqualApprox(snapshot.EffectsHeightMeters, EffectsHeightMeters) ||
            snapshot.AddedPresentationRealized !=
                (snapshot.ContentVariant == CabinContentVariant.Add))
            throw new InvalidOperationException("Cabin catalog/human calibration realization is incomplete.");
    }

    private void ProbePlayableInventory()
    {
        if (_localSession is null || _uiClient is null || _uiBridge is null)
            throw new InvalidOperationException("Playable Inventory presentation was not composed.");
        ulong before = _localSession.GetSnapshot().UiOperationsApplied;
        OpenInventory();
        DriveLocalUntil(() =>
        {
            _ = _uiBridge.Pump();
            return _uiClient.OpenViewCount == 1 &&
                _localSession.GetSnapshot().UiOperationsApplied > before;
        });
    }

    private void ProbeAuthoritativeJump()
    {
        if (_localSession is null || _sequencer is null)
            throw new InvalidOperationException("Playable vertical motion was not composed.");
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        SetProfile(ControlProfile.ThirdPersonDirect);
        WorldPosition start = _player;
        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Unchanged,
                    GamePlayerActions.Jump,
                    true),
                out GamePlayerCommand jump))
            throw new InvalidOperationException("Playable Jump could not form a command edge.");
        ApplyCommand(jump, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == jump.Sequence &&
            view.LastCommandResult.Jump == GameWorldActionStatus.Accepted &&
            _player.YMillimeters > start.YMillimeters);

        if (!_sequencer.TryCreateSemanticCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Unchanged,
                    GamePlayerActions.None,
                    true),
                out GamePlayerCommand release))
            throw new InvalidOperationException("Playable Jump release could not form a command.");
        ApplyCommand(release, false, default, false);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == release.Sequence && _player == start);
    }

    private void ProbeDoorAndChestInteraction()
    {
        if (_localSession is null || _sequencer is null)
            throw new InvalidOperationException("Playable interaction was not composed.");
        SubmitInteraction(LocalPlayableSelection.CabinDoor, out GamePlayerCommand doorCommand);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == doorCommand.Sequence && view.DoorIsOpen &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted);
        ReleaseInteraction();

        if (!WorldPosition.TryFromGlobalMillimeters(18_000, 0, 9_500, out WorldPosition chestApproach))
            throw new InvalidOperationException("The Cabin chest approach is not canonical.");
        RelocateControlledActor(chestApproach);
        SubmitInteraction(LocalPlayableSelection.CabinChest, out GamePlayerCommand chestCommand);
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == chestCommand.Sequence &&
            view.LastChestAction == GameWorldActionStatus.Accepted && !view.BreadIsInBackpack);
        ReleaseInteraction();
        RelocateControlledActor(GamePlayableWorld.PlayerStart);

        void SubmitInteraction(LocalPlayableSelection selection, out GamePlayerCommand command)
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.Interact,
                        true),
                    out command) ||
                !_localSession.TrySubmit(new LocalPlayableCommand(
                    command, selection, false, default, false)))
                throw new InvalidOperationException($"Playable {selection} interaction could not be submitted.");
        }

        void ReleaseInteraction()
        {
            if (!_sequencer.TryCreateSemanticCommand(
                    new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop,
                        GamePlayerActions.None,
                        true),
                    out GamePlayerCommand release) ||
                !_localSession.TrySubmit(new LocalPlayableCommand(
                    release, LocalPlayableSelection.None, false, default, false)))
                throw new InvalidOperationException("Playable interaction release could not be submitted.");
            DriveLocalUntil(() =>
                _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastCommandSequence == release.Sequence);
        }
    }

    private void ProbePlayableChat()
    {
        if (_chatState is null || _chatFeed is null || _overheadSpeech is null)
            throw new InvalidOperationException("Playable Chat presentation was not composed.");
        ulong before = _chatState.GetSnapshot().Applied;
        SubmitChat("playable speech");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied != before);
        if (_chatState.GetSnapshot().Applied != before + 1)
            throw new InvalidOperationException("Playable spatial speech did not apply.");
        UpdateChatPresentation();
        if (!_chatFeed.Text.Contains("playable speech", StringComparison.Ordinal) ||
            !_overheadAnchor.IsValid || !TryFindPublicAnchor(_overheadAnchor, out _))
            throw new InvalidOperationException("Playable speech did not reach feed and public-anchor presentation.");

        before = _chatState.GetSnapshot().Applied;
        SubmitChat("/where");
        DriveLocalUntil(() => _chatState.GetSnapshot().Applied != before);
        UpdateChatPresentation();
        if (_chatState.GetSnapshot().Applied != before + 1 ||
            !_chatFeed.Text.Contains("Where:", StringComparison.Ordinal))
            throw new InvalidOperationException("Playable private /where result did not reach Chat.");
    }

    private WorldYaw ReadPlayerOrientation()
    {
        if (_localSession is null ||
            !_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The playable character orientation was unavailable.");
        return view.PlayerOrientation;
    }

    private void RelocateControlledActor(in WorldPosition requested)
    {
        if (_localSession is null)
            throw new InvalidOperationException("Authority is not available for relocation.");
        ulong request = _nextAdministrativeRequest++;
        if (!_localSession.TryRequestRelocation(request, requested))
            throw new InvalidOperationException("Launch-smoke relocation queue was full.");
        DriveLocalUntil(() =>
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastAdministrativeSequence == request);
        _localSession.TryGetLatestView(out LocalPlayableAuthorityView completed);
        if (!completed.LastAdministrativeSucceeded)
            throw new InvalidOperationException(
                $"Launch-smoke relocation failed: {completed.LastAdministrativeMessage}.");

        // The authority may publish the administrative result after this frame copied its
        // presentation view. Do not let the next client-authored absolute facing point use the
        // pre-relocation _player origin. Cross the same semantic boundary as ordinary play:
        // authority commits first, then Client Core/presentation observes that exact position.
        WorldPosition authoritativePosition = completed.PlayerPosition;
        DriveLocalUntil(() => _player == authoritativePosition);
    }

    private int CountPresentedPrefab(uint prefabId)
    {
        if (ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefabId, out _) &&
            _actorState is not null)
        {
            int actorCount = _actorState.CopyCurrent(_actorRows);
            int matched = 0;
            for (int index = 0; index < actorCount; index++)
                if (_actorRows[index].PrefabId == prefabId) matched++;
            return matched;
        }
        if (_renderer is null) return 0;
        string expectedName = $"Public_{prefabId}";
        int count = 0;
        Count(_renderer.Root);
        return count;

        void Count(Node node)
        {
            if (node is MultiMeshInstance3D instance && instance.Name == expectedName &&
                instance.Multimesh is not null)
                count += instance.Multimesh.InstanceCount;
            foreach (Node child in node.GetChildren()) Count(child);
        }
    }

    private int CountPresentedCharacters() =>
        CountPresentedPrefab(GameWorldPublicPrefabIds.Character) +
        CountPresentedPrefab(GameWorldPublicPrefabIds.CharacterMale);

    /// <summary>
    /// Verifies the actual native public-presentation resources after the shell has committed the
    /// session. This is deliberately later than initial semantic convergence: loading may build
    /// nodes before their render resources and bounds have been validated.
    /// </summary>
    private void ValidateInitialNativePresentation()
    {
        int characters = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.Character) +
            CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.CharacterMale);
        int doors = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor);
        int chests = CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.WorldChest);
        int telepads = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.SkyTelepad)
            : 0;
        int gates = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.FarGate)
            : 0;
        int signs = _useConceptDemoWorld
            ? CountVisiblyPresentedPrefab(GameWorldPublicPrefabIds.InformationalSign)
            : 0;
        if (characters != 1 || doors != 1 || chests != 1 ||
            _useConceptDemoWorld &&
            (telepads != 1 || gates != 1 || signs != 2))
            throw new InvalidOperationException(
                "The initial public world does not own complete visible native mesh presentation: " +
                $"characters={characters}, doors={doors}, chests={chests}, telepads={telepads}, " +
                $"gates={gates}, signs={signs}.");
    }

    private int CountVisiblyPresentedPrefab(uint prefabId)
    {
        if (ClientActorDefinitionCatalog.ConceptDemo.TryGet(prefabId, out _) &&
            _actorState is not null && _actorPresenter is not null)
        {
            int actorCount = _actorState.CopyCurrent(_actorRows);
            int matched = 0;
            for (int index = 0; index < actorCount; index++)
            {
                ref readonly ClientActorPresentationRecord actor = ref _actorRows[index];
                if (actor.PrefabId == prefabId &&
                    _actorPresenter.TryGetActorRoot(actor.Id, out Node3D? root) &&
                    root?.Visible == true && CountMeshInstances(root) != 0)
                    matched++;
            }
            return matched;
        }
        if (_renderer is null) return 0;
        string expectedName = $"Public_{prefabId}";
        int count = 0;
        Count(_renderer.Root);
        return count;

        void Count(Node node)
        {
            if (node is MultiMeshInstance3D instance && instance.Name == expectedName &&
                instance.Visible &&
                instance.Multimesh is { } multiMesh &&
                multiMesh.VisibleInstanceCount != 0 &&
                multiMesh.Mesh is { } mesh && mesh.GetSurfaceCount() > 0 &&
                instance.CustomAabb.Size.LengthSquared() > 0f)
                count += multiMesh.InstanceCount;
            foreach (Node child in node.GetChildren()) Count(child);
        }
    }

    private int CountRenderedTerrainTriangles()
    {
        if (_cache is null || _renderer is null ||
            !_cache.TryCopyResidentInfo(_chatChunkInfo, out int count, out _))
            return 0;
        int triangles = 0;
        for (int index = 0; index < count; index++)
        {
            if (_renderer.TryGetChunkDiagnostics(
                    _chatChunkInfo[index].Handle,
                    out GodotWorldRenderChunkDiagnostics diagnostics))
                triangles = checked(triangles + diagnostics.TerrainTriangleCount +
                    diagnostics.AdditionalTerrainTriangleCount);
        }
        return triangles;
    }

    private void ApplyProfileCameraDefaults()
    {
        switch (_profile)
        {
            case ControlProfile.OrbitCommand:
                _cameraYaw = -0.7f;
                _cameraPitch = OrbitDefaultPitchRadians;
                _cameraDistance = OrbitDefaultCameraDistanceMeters;
                break;
            case ControlProfile.ThirdPersonDirect:
                _cameraYaw = -0.7f;
                _cameraPitch = 0.34f;
                _cameraDistance = 8.5f;
                break;
            case ControlProfile.IndependentMoveLook:
                _cameraYaw = -MathF.PI * 0.25f;
                _cameraPitch = 0.61548f;
                _cameraDistance = 14f;
                break;
        }
    }

    private void SaveWorld()
    {
        if (_localSession is null)
            return;
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestSave(request)
            ? "Save requested"
            : "Save queue busy";
    }

    private void LoadWorld()
    {
        if (_localSession is null)
            return;
        ulong request = _nextAdministrativeRequest++;
        _lastOutcome = _localSession.TryRequestLoad(request)
            ? "Load requested"
            : "Load queue busy";
    }

    private void DriveLocalUntil(Func<bool> complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        for (int step = 0; step < 10_000; step++)
        {
            PumpChat();
            _ = _uiBridge?.Pump();
            SynchronizeProjection();
            UpdateChatPresentation();
            DrainRenderer();
            if (complete()) return;
            LocalPlayableSessionSnapshot session = _localSession?.GetSnapshot() ?? default;
            if (!string.IsNullOrEmpty(session.Fault))
                throw new InvalidOperationException(
                    $"The local authority faulted while the client was converging: {session.Fault}");
            Thread.Sleep(1);
        }
        LocalPlayableSessionSnapshot final = _localSession?.GetSnapshot() ?? default;
        LocalPlayableAuthorityView authority = default;
        if (_localSession is not null) _ = _localSession.TryGetLatestView(out authority);
        throw new TimeoutException(
            "The local authority/client semantic bridge did not converge. " +
            $"ticks={final.AuthorityTicks} pendingCommands={final.PendingCommands} " +
            $"lastCommand={authority.LastCommandSequence} position={authority.PlayerPosition} " +
            $"orientation={authority.PlayerOrientation} fault={final.Fault ?? "none"}.");
    }

    private static int CountMeshInstances(Node node)
    {
        int count = node is MeshInstance3D ? 1 : 0;
        foreach (Node child in node.GetChildren())
            count += CountMeshInstances(child);
        return count;
    }

    private static Control FullRectControl(string name)
    {
        var result = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        result.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return result;
    }

    private static void ReleaseExternalUi(Control? root)
    {
        if (root is null || !GodotObject.IsInstanceValid(root)) return;
        root.GetParent()?.RemoveChild(root);
        root.QueueFree();
    }

    private void CreateSceneFurniture()
    {
        _camera = new Camera3D
        {
            Name = "OrbitCamera",
            Current = true,
            Near = 0.1f,
            Far = 500f,
            Fov = _settings?.CameraFovDegrees ?? 70f,
        };
        AddChild(_camera);
        UpdateCamera();

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55, -35, 0),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sun);
        var fill = new WorldEnvironment { Name = "Environment" };
        fill.Environment = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.055f, 0.075f, 0.06f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.65f, 0.72f, 0.68f),
            AmbientLightEnergy = 0.65f,
        };
        AddChild(fill);

        _ownedHudRoot = FullRectControl("CabinHUD");
        _ownedHudRoot.Scale = Vector2.One * (_settings?.UiScale ?? 1f);
        if (_shellHudLayer is not null)
            _shellHudLayer.AddChild(_ownedHudRoot);
        else
        {
            var layer = new CanvasLayer { Name = "HUD" };
            AddChild(layer);
            layer.AddChild(_ownedHudRoot);
        }
        var panel = new PanelContainer
        {
            OffsetLeft = 16,
            OffsetTop = 16,
            OffsetRight = 590,
            OffsetBottom = 220,
        };
        _ownedHudRoot.AddChild(panel);
        var stack = new VBoxContainer();
        panel.AddChild(stack);
        var title = new Label
        {
            Text = _useConceptDemoWorld
                ? "UltraSim — Concept Demo Island"
                : "UltraSim — T0.10 Woods / Cabin",
            ThemeTypeVariation = "HeaderMedium",
        };
        stack.AddChild(title);
        _status = new Label();
        _details = new Label();
        stack.AddChild(_status);
        stack.AddChild(_details);
        var profiles = new HBoxContainer();
        stack.AddChild(profiles);
        AddProfileButton("1/F1 Orbit", ControlProfile.OrbitCommand);
        AddProfileButton("2/F2 Third person", ControlProfile.ThirdPersonDirect);
        AddProfileButton("3/F3 Iso move/look", ControlProfile.IndependentMoveLook);
        stack.AddChild(new Label
        {
            Text = "F1: single-click selects • hold LMB steers • double-click ground travels • RMB/Esc stops\n" +
                (_useConceptDemoWorld
                    ? "MMB drag camera • wheel zoom • E use selected door/chest/sign/telepad/gate • I inventory\n"
                    : "MMB drag camera • wheel zoom • E use selected door/chest • I inventory\n") +
                "Space jump (direct profiles) • 1/F1 Orbit • 2/F2 third-person • " +
                "3/F3 fixed iso move/look • F5 save • F9 load",
        });

        var chatPanel = new PanelContainer
        {
            OffsetLeft = 16,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -250,
            OffsetRight = 590,
            OffsetBottom = -16,
        };
        _ownedHudRoot.AddChild(chatPanel);
        var chatStack = new VBoxContainer();
        chatPanel.AddChild(chatStack);
        chatStack.AddChild(new Label { Text = "Chat", ThemeTypeVariation = "HeaderSmall" });
        _chatFeed = new RichTextLabel
        {
            BbcodeEnabled = false,
            CustomMinimumSize = new Vector2(540, 145),
            ScrollFollowing = true,
        };
        chatStack.AddChild(_chatFeed);
        var chatEntry = new HBoxContainer();
        chatStack.AddChild(chatEntry);
        _chatMode = new OptionButton { CustomMinimumSize = new Vector2(100, 0) };
        _chatMode.AddItem("Whisper", (int)ClientChatSpeechMode.Whisper);
        _chatMode.AddItem("Say", (int)ClientChatSpeechMode.Say);
        _chatMode.AddItem("Yell", (int)ClientChatSpeechMode.Yell);
        _chatMode.Select(1);
        chatEntry.AddChild(_chatMode);
        _chatInput = new LineEdit
        {
            PlaceholderText = "Enter chat or /help /where",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = MaximumChatTextCharacters,
        };
        _chatInput.TextSubmitted += SubmitChat;
        chatEntry.AddChild(_chatInput);
        _ownedWorldUiRoot = FullRectControl("CabinWorldUI");
        if (_shellWorldUiLayer is not null)
            _shellWorldUiLayer.AddChild(_ownedWorldUiRoot);
        else
            _ownedHudRoot.AddChild(_ownedWorldUiRoot);
        _overheadSpeech = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ownedWorldUiRoot.AddChild(_overheadSpeech);

        _hoverTooltip = new Label
        {
            Name = "WorldHoverTooltip",
            Text = "Sign",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = "HeaderSmall",
        };
        _ownedWorldUiRoot.AddChild(_hoverTooltip);

        _inventoryHost = FullRectControl("CabinInventory");
        _ownedHudRoot.AddChild(_inventoryHost);

        void AddProfileButton(string text, ControlProfile profile)
        {
            var button = new Button { Text = text };
            button.Pressed += () => SetProfile(profile);
            profiles.AddChild(button);
        }
    }

    private void SubmitChat(string text)
    {
        if (_chatInput is null || _localSession is null)
            return;
        ClientChatSpeechMode mode = _chatMode is null
            ? ClientChatSpeechMode.Say
            : (ClientChatSpeechMode)_chatMode.GetSelectedId();
        if (_localSession.TrySubmitChat(_nextChatSubmission++, mode, text))
        {
            _chatInput.Clear();
            _chatInput.ReleaseFocus();
            _lastOutcome = "Chat submitted";
        }
        else
        {
            _lastOutcome = "Chat input rejected or transport busy";
        }
    }

    private void PumpChat()
    {
        if (_localSession is null || _chatState is null)
            return;
        _ = _localSession.PumpChat(_chatState);
    }

    private void UpdateChatPresentation()
    {
        if (_chatState is null || _chatFeed is null)
            return;
        ClientChatStateSnapshot snapshot = _chatState.GetSnapshot();
        if (snapshot.Applied != _chatAppliedPresentationCount)
        {
            int count = _chatState.CopyRecent(_chatRows);
            int first = Math.Max(0, count - 8);
            var lines = new string[count - first];
            for (int index = first; index < count; index++)
            {
                ClientChatOccurrence row = _chatRows[index];
                string prefix = row.Kind == ClientChatOccurrenceKind.SpatialSpeech
                    ? $"[{row.SpeechMode}] {row.DisplayLabel}: "
                    : $"[{row.DisplayLabel}] ";
                lines[index - first] = prefix + row.Text;
            }
            _chatFeed.Text = string.Join('\n', lines);
            if (count != 0)
            {
                ClientChatOccurrence newest = _chatRows[count - 1];
                ulong residentGenerationId = ReadActiveGenerationId();
                if (newest.CanPresentOverhead(residentGenerationId))
                {
                    _overheadAnchor = newest.PublicAnchor;
                    _overheadOriginGenerationId = newest.ServerGenerationId;
                    _overheadExpiresAtMilliseconds = checked(Time.GetTicksMsec() + 4_000);
                    _ = _actorPresenter?.TryPresentOverhead(
                        in newest,
                        residentGenerationId,
                        _overheadExpiresAtMilliseconds);
                }
                else if (newest.IsSpatial)
                {
                    _overheadAnchor = default;
                    _overheadOriginGenerationId = 0;
                }
            }
            _chatAppliedPresentationCount = snapshot.Applied;
        }

        if (_overheadSpeech is not null) _overheadSpeech.Visible = false;
        _actorPresenter?.UpdateOriginAndLifetimes(_player, Time.GetTicksMsec());
    }

    private ulong ReadActiveGenerationId()
    {
        return _localSession is not null &&
            _localSession.TryGetLatestView(out LocalPlayableAuthorityView view)
                ? view.GenerationId
                : 0;
    }

    private bool TryFindPublicAnchor(WorldPublicEntityId id, out WorldPosition position)
    {
        if (_cache is not null && _cache.TryCopyResidentInfo(
                _chatChunkInfo, out int count, out _))
        {
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!_cache.TryGetResidentView(_chatChunkInfo[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.Id != id) continue;
                    position = record.Position;
                    return true;
                }
            }
        }
        position = default;
        return false;
    }

    private void UpdateHud()
    {
        if (_status is null || _details is null)
            return;
        _status.Text = _lastOutcome;
        if (!_initialized || _localSession is null || _renderer is null ||
            !_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
        {
            _details.Text = "Not running";
            return;
        }
        GodotWorldRendererSnapshot render = _renderer.GetSnapshot();
        _peakRenderedChunks = Math.Max(_peakRenderedChunks, render.ActiveRenderChunks);
        ClientStructuralResidencySnapshot residency = _residency?.GetSnapshot() ?? default;
        ClientPresentationWindowSnapshot presentation = _window?.GetSnapshot() ?? default;
        _details.Text =
            $"Profile {_profile} • Selected {_selection} • Tick {view.SimulationTick}\n" +
            $"Player {_player.Chunk} + ({_player.Local.XMillimeters}, {_player.Local.ZMillimeters}) mm • " +
            $"door {(view.DoorIsOpen ? "open" : "closed")} • " +
            $"bread in {(view.BreadIsInBackpack ? "backpack" : "chest")}\n" +
            $"distance {residency.ConfiguredRenderRadiusChunks} chunks • " +
            $"authored/resident/rendered {residency.AuthoredChunks}/" +
            $"{residency.SemanticResidentChunks}/{render.ActiveRenderChunks} " +
            $"(peak {residency.PeakSemanticResidentChunks}/{_peakRenderedChunks})\n" +
            $"semantic enter/exit {residency.SemanticEnters}/{residency.SemanticExits} • " +
            $"presentation enter/exit {presentation.EnteredChunks}/{presentation.ExitedChunks} • " +
            $"resets {residency.ResetTransitions}/{presentation.ResetTransitions} • " +
            $"pending {render.PendingBuilds}/{render.PendingRetirements}";
    }

    private static SpatialChunkKey ChunkKey(int x, int z) =>
        new(GameWorldState.DefaultSpace, new SpatialChunkCoord(x, z));

    private static bool Nearest(
        in ClientControlWorldPoint point,
        in WorldPosition target,
        long radius)
    {
        Int128 dx = (Int128)point.XMillimeters - target.GlobalXMillimeters;
        Int128 dz = (Int128)point.ZMillimeters - target.GlobalZMillimeters;
        return dx * dx + dz * dz <= (Int128)radius * radius;
    }

    private sealed class BundledActorVisualResolver : IGodotActorVisualResolver
    {
        public Node3D? Instantiate(in ClientActorDefinition definition)
        {
            string? resource = definition.VisualKind switch
            {
                ClientActorVisualKind.HumanX =>
                    "res://Assets/Avatars/x_bot/x_bot_visual.tscn",
                ClientActorVisualKind.HumanY =>
                    "res://Assets/Avatars/y_bot/y_bot_visual.tscn",
                _ => null,
            };
            if (resource is null) return null;
            PackedScene? scene = ResourceLoader.Load<PackedScene>(resource);
            if (scene is null)
                throw new InvalidOperationException(
                    $"The bundled actor presentation is missing: {resource}.");
            Node instantiated = scene.Instantiate();
            if (instantiated is Node3D visual) return visual;
            instantiated.Free();
            throw new InvalidOperationException(
                $"The bundled actor presentation is not a Node3D scene: {resource}.");
        }
    }

    private sealed class CabinCatalogAssetResolver : IGodotWorldAssetResolver
    {
        private readonly Dictionary<ushort, StandardMaterial3D> _terrain = new()
        {
            [GameTerrainMaterialIds.Water] = Material(new Color(0.08f, 0.28f, 0.5f)),
            [GameTerrainMaterialIds.Sand] = Material(new Color(0.62f, 0.55f, 0.34f)),
            [GameTerrainMaterialIds.Grass] = Material(new Color(0.16f, 0.38f, 0.14f)),
            [GameTerrainMaterialIds.Rock] = Material(new Color(0.32f, 0.34f, 0.31f)),
            [GameTerrainMaterialIds.Snow] = Material(new Color(0.78f, 0.84f, 0.82f)),
        };
        private readonly Mesh _wood = GroundedBox(
            new Vector3(0.75f, 4.5f, 0.75f), new Color(0.24f, 0.14f, 0.07f));
        private readonly Mesh _stone = GroundedBox(
            Vector3.One, new Color(0.34f, 0.36f, 0.34f));
        private readonly Mesh _wallEight;
        private readonly Mesh _wallSix;
        private readonly Mesh _wallThree;
        private readonly Mesh _roof;
        private readonly Mesh _shelf;
        private readonly Mesh _book;
        private readonly Mesh _fallbackStructure;
        private readonly Mesh _character = CharacterMesh();
        private readonly Mesh _door;
        private readonly Mesh _chest;
        private readonly Mesh _animal = GroundedBox(
            new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));
        private readonly Mesh _telepad = GroundedBox(
            new Vector3(1.8f, 0.18f, 1.8f), new Color(0.12f, 0.72f, 0.9f));
        private readonly Mesh _farGate = GroundedBox(
            new Vector3(1.5f, 2.8f, 0.35f), new Color(0.68f, 0.22f, 0.86f));
        private readonly Mesh _informationalSign = GroundedBox(
            new Vector3(1.25f, 1.65f, 0.16f), new Color(0.52f, 0.31f, 0.12f));
        private readonly Mesh _mountainWallEight = GroundedBox(
            new Vector3(8f, 4.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
        private readonly Mesh _mountainWallTen = GroundedBox(
            new Vector3(10f, 4.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
        private readonly Mesh _fallbackLabPedestal = GroundedBox(
            new Vector3(2f, 0.4f, 2f), new Color(0.23f, 0.24f, 0.26f));
        private readonly Mesh _fallbackLabModel = GodotMeshGeometry.CreateGroundedPointerWedge(
            0.5f,
            0.42f,
            0.72f,
            1.6f,
            Material(new Color(0.18f, 0.78f, 0.36f)));

        public CabinCatalogAssetResolver(
            CabinContentOracle oracle,
            CabinResolvedContent resolved)
        {
            StandardMaterial3D wall = ResolveMaterial(
                oracle, resolved.Catalog, CabinContentIds.WallMaterial, out ContentHash wallHash);
            StandardMaterial3D wood = ResolveMaterial(
                oracle, resolved.Catalog, CabinContentIds.WoodMaterial, out _);
            WallTextureHash = wallHash;
            _wallEight = GroundedBox(new Vector3(8f, 2.5f, 0.3f), wall);
            _wallSix = GroundedBox(new Vector3(6f, 2.5f, 0.3f), wall);
            _wallThree = GroundedBox(new Vector3(3f, 2.5f, 0.3f), wall);
            _roof = GroundedBox(new Vector3(8.3f, 0.25f, 6.3f), wood, transparent: true);
            _shelf = GroundedBox(new Vector3(0.9f, 1.8f, 0.35f), wood);
            _book = GroundedBox(new Vector3(0.24f, 0.18f, 0.18f), wood);
            _fallbackStructure = GroundedBox(new Vector3(1f, 2f, 0.35f), wall);
            _door = GroundedBox(new Vector3(1.6f, 2.1f, 0.22f), wood);
            _chest = GroundedBox(new Vector3(1.2f, 0.8f, 0.8f), wood);
            if (resolved.Catalog.TryGet(CabinContentIds.BlueGlowMaterial, out _))
                AddedMaterial = ResolveMaterial(
                    oracle, resolved.Catalog, CabinContentIds.BlueGlowMaterial, out _);
        }

        public ContentHash WallTextureHash { get; }
        public StandardMaterial3D? AddedMaterial { get; }

        private GodotWorldMeshResolution ResolveFallbackLabMesh(ushort variant) => variant switch
        {
            GameWorldStaticVariants.FallbackLabCorrect =>
                GodotWorldMeshResolution.Available(_fallbackLabModel),
            GameWorldStaticVariants.FallbackLabModelMissing =>
                GodotWorldMeshResolution.MissingModel(),
            GameWorldStaticVariants.FallbackLabTextureMissing =>
                GodotWorldMeshResolution.MissingTexture(_fallbackLabModel),
            GameWorldStaticVariants.FallbackLabModelAndTextureMissing =>
                GodotWorldMeshResolution.MissingModelAndTexture(),
            _ => GodotWorldMeshResolution.Available(ResolveStaticMeshCore(
                new WorldStaticRenderKey(
                    new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock),
                    variant,
                    WorldStaticFlags.Renderable))),
        };

        private Mesh ResolveStaticMeshCore(WorldStaticRenderKey key) => key.DefinitionId switch
        {
            var id when id.Value == GameWorldStaticDefinitionIds.Tree => _wood,
            var id when id.Value == GameWorldStaticDefinitionIds.Rock => key.Variant switch
            {
                GameWorldStaticVariants.MountainWallEightMeters => _mountainWallEight,
                GameWorldStaticVariants.MountainWallTenMeters => _mountainWallTen,
                GameWorldStaticVariants.FallbackLabPedestal => _fallbackLabPedestal,
                GameWorldStaticVariants.FallbackLabCorrect or
                    GameWorldStaticVariants.FallbackLabModelMissing or
                    GameWorldStaticVariants.FallbackLabTextureMissing or
                    GameWorldStaticVariants.FallbackLabModelAndTextureMissing => _fallbackLabModel,
                _ => _stone,
            },
            var id when id.Value == GameWorldStaticDefinitionIds.CabinWall => key.Variant switch
            {
                GameWorldStaticVariants.CabinWallEightMeters => _wallEight,
                GameWorldStaticVariants.CabinWallSixMeters => _wallSix,
                GameWorldStaticVariants.CabinWallThreeMeters => _wallThree,
                _ => _fallbackStructure,
            },
            var id when id.Value == GameWorldStaticDefinitionIds.CabinRoof => _roof,
            var id when id.Value == GameWorldStaticDefinitionIds.Shelf => _shelf,
            var id when id.Value == GameWorldStaticDefinitionIds.Book => _book,
            _ => _fallbackStructure,
        };

        GodotWorldMeshResolution IGodotWorldAssetResolver.ResolveStaticMesh(
            WorldStaticRenderKey key) =>
            key.DefinitionId.Value == GameWorldStaticDefinitionIds.Rock &&
            key.Variant is >= GameWorldStaticVariants.FallbackLabCorrect and
                <= GameWorldStaticVariants.FallbackLabModelAndTextureMissing
                ? ResolveFallbackLabMesh(key.Variant)
                : GodotWorldMeshResolution.Available(ResolveStaticMeshCore(key));

        public GodotWorldMeshResolution ResolvePublicMesh(uint prefabId) =>
            GodotWorldMeshResolution.Available(ResolvePublicMeshCore(prefabId));

        private Mesh ResolvePublicMeshCore(uint prefabId) => prefabId switch
        {
            GameWorldPublicPrefabIds.Character or GameWorldPublicPrefabIds.CharacterMale => _character,
            GameWorldPublicPrefabIds.CabinDoor => _door,
            GameWorldPublicPrefabIds.WorldChest => _chest,
            GameWorldPublicPrefabIds.WoodlandAnimal => _animal,
            GameWorldPublicPrefabIds.SkyTelepad => _telepad,
            GameWorldPublicPrefabIds.FarGate => _farGate,
            GameWorldPublicPrefabIds.InformationalSign => _informationalSign,
            _ => _stone,
        };

        public Material? ResolveTerrainMaterial(ushort materialId) =>
            _terrain.TryGetValue(materialId, out StandardMaterial3D? material) ? material : null;

        private static StandardMaterial3D Material(Color color) => new()
        {
            AlbedoColor = color,
            Roughness = 0.95f,
        };

        private static ArrayMesh GroundedBox(Vector3 size, Color color, bool transparent = false)
        {
            StandardMaterial3D material = Material(color);
            if (transparent)
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            return GodotMeshGeometry.CreateGroundedBox(size, material);
        }

        private static ArrayMesh GroundedBox(
            Vector3 size,
            StandardMaterial3D material,
            bool transparent = false)
        {
            if (transparent)
            {
                material = (StandardMaterial3D)material.Duplicate();
                material.AlbedoColor = new Color(material.AlbedoColor, 0.42f);
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }
            return GodotMeshGeometry.CreateGroundedBox(size, material);
        }

        private static ArrayMesh CharacterMesh()
        {
            StandardMaterial3D material = Material(new Color(0, 0, 0, 0));
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            return GodotMeshGeometry.CreateGroundedPointerWedge(
                0.42f, 0.42f, 0.68f, 1.8f, material);
        }

        private static StandardMaterial3D ResolveMaterial(
            CabinContentOracle oracle,
            ResolvedContentCatalog catalog,
            LogicalContentId id,
            out ContentHash textureHash)
        {
            if (!catalog.TryGet(id, out ResolvedContentEntry? entry) ||
                entry!.Kind != ContentDefinitionKind.Material ||
                entry.ReferencedFiles.Count != 1)
                throw new InvalidOperationException($"Cabin material '{id}' is unavailable.");
            textureHash = entry.ReferencedFiles[0];
            if (!oracle.TryGetPayload(textureHash, out ReadOnlyMemory<byte> bytes))
                throw new InvalidOperationException($"Cabin texture payload '{textureHash}' is unavailable.");
            var image = new Image();
            Error status = image.LoadPngFromBuffer(bytes.ToArray());
            if (status != Error.Ok)
                throw new InvalidOperationException($"Cabin texture '{id}' could not be decoded: {status}.");
            ContentMaterialDefinition definition = entry.Definition.Material;
            var material = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(image),
                Roughness = definition.Roughness,
                Metallic = definition.Metallic,
            };
            if (definition.Emission > 0)
            {
                material.EmissionEnabled = true;
                material.Emission = Colors.White;
                material.EmissionEnergyMultiplier = definition.Emission;
            }
            return material;
        }
    }
}
