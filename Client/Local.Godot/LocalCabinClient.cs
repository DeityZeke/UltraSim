using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Local;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

namespace UltraSim.Client.Local.Godot;

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
    string? InitializationError);

/// <summary>
/// Permanent F0.2 Godot consumer for one Network-free local Cabin application session.
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
        WoodlandAnimal,
    }

    private const int RenderRadius = 1;
    private const int MaximumChatTextCharacters = 256;
    private const float CameraPivotMeters = 1.6f;
    private const float NameplateHeightMeters = 2.0f;
    private const float EffectsHeightMeters = 0.9f;

    private CabinContentOracle? _contentOracle;
    private CabinResolvedContent? _resolvedContent;
    private string? _saveRoot;
    private CanvasLayer? _shellHudLayer;
    private CanvasLayer? _shellWorldUiLayer;
    private CabinHumanBody _humanBody = CabinHumanBody.XBot;
    private ResolvedClientSettings? _settings;
    private CabinCatalogAssetResolver? _assetResolver;
    private LocalPlayableSession? _localSession;
    private BoundWorldPack? _boundWorld;
    private ClientWorldSemanticCache? _cache;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientWorldInputSequencer? _sequencer;
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _thirdPerson = new();
    private readonly IndependentMoveLookProfile _independent = new();
    private readonly ClientPresentationChange[] _presentationChanges = new ClientPresentationChange[18];
    private readonly ClientSemanticHash[] _semanticHashesBeforeRefresh = new ClientSemanticHash[25];
    private readonly ClientChatOccurrence[] _chatRows = new ClientChatOccurrence[64];
    private readonly ClientSemanticChunkInfo[] _chatChunkInfo = new ClientSemanticChunkInfo[25];
    private Camera3D? _camera;
    private Label? _status;
    private Label? _details;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private OptionButton? _chatMode;
    private Label? _overheadSpeech;
    private Control? _ownedHudRoot;
    private Control? _ownedWorldUiRoot;
    private Control? _inventoryHost;
    private UiClientRuntime? _uiClient;
    private LocalQueuedUiClient? _uiBridge;
    private Node3D? _humanPresentation;
    private int _humanMeshInstances;
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
    private float _cameraPitch = 1.12f;
    private float _cameraDistance = 28f;
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
        ResolvedClientSettings? settings = null)
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
        _settings = settings ?? ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
    }

    public void SetGameplayInputAllowed(bool allowed) => _gameplayInputAllowed = allowed;

    public void ApplySettings(ResolvedClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ApplyResolvedSettings();
    }

    public LocalCabinPresentationSnapshot GetPresentationSnapshot()
    {
        LocalPlayableSessionSnapshot local = _localSession?.GetSnapshot() ?? default;
        LocalPlayableAuthorityView authority = default;
        if (_localSession is not null) _ = _localSession.TryGetLatestView(out authority);
        return new LocalCabinPresentationSnapshot(
            _initialized,
            _resolvedContent?.Variant ?? default,
            _resolvedContent?.Catalog.Fingerprint ?? default,
            _resolvedContent?.Catalog.Generation ?? default,
            _assetResolver?.WallTextureHash ?? default,
            _humanBody,
            _humanMeshInstances,
            CameraPivotMeters,
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
            _initializationError);
    }

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
            CreateHumanPresentation();
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
                    _cameraDistance = Math.Max(7f, _cameraDistance - 2f);
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
        _renderer?.Dispose();
        _renderer = null;
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
        _localSession = LocalPlayableSession.CreateCabin(_saveRoot);
        _localSession.WaitUntilReady(TimeSpan.FromSeconds(10));
        _boundWorld = _localSession.BoundWorld;
        _chatState = new ClientChatState(
            _localSession.ApplicationSessionEpoch,
            new ClientChatLimits(64, 256, 128));
        if (!_localSession.TryGetLatestView(out LocalPlayableAuthorityView view))
            throw new InvalidOperationException("The local authority did not publish its initial view.");
        RebuildClientProjection(view);
        DriveLocalUntil(() =>
            CountPresentedPrefab(GameWorldPublicPrefabIds.Character) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.CabinDoor) == 1 &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.WorldChest) == 1);
        _lastOutcome = "Fresh woods/cabin world started";
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

    private void CreateHumanPresentation()
    {
        string resource = _humanBody == CabinHumanBody.XBot
            ? "res://Assets/Avatars/x_bot/x_bot_visual.tscn"
            : "res://Assets/Avatars/y_bot/y_bot_visual.tscn";
        PackedScene? scene = ResourceLoader.Load<PackedScene>(resource);
        if (scene is null)
            throw new InvalidOperationException($"The bundled human calibration body is missing: {resource}.");
        Node instantiated = scene.Instantiate();
        if (instantiated is not Node3D human)
        {
            instantiated.Free();
            throw new InvalidOperationException("The bundled human calibration body is not a Node3D scene.");
        }
        _humanPresentation = human;
        _humanPresentation.Name = $"Controlled{_humanBody}";
        AddChild(_humanPresentation);
        _humanMeshInstances = CountMeshInstances(_humanPresentation);
        if (_humanMeshInstances == 0)
            throw new InvalidOperationException("The bundled human calibration body contains no mesh presentation.");

        var nameplate = new Label3D
        {
            Name = "ControlledPlayerNameplateCalibration",
            Text = _humanBody == CabinHumanBody.XBot ? "Human Female" : "Human Male",
            Position = new Vector3(0, NameplateHeightMeters, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 32,
        };
        _humanPresentation.AddChild(nameplate);
        var effectsAnchor = new Marker3D
        {
            Name = "EffectsAnchorCalibration",
            Position = new Vector3(0, EffectsHeightMeters, 0),
        };
        _humanPresentation.AddChild(effectsAnchor);
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

    private void UpdateHumanPresentation(WorldYaw orientation)
    {
        if (_humanPresentation is null) return;
        float radians = orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn);
        _humanPresentation.Position = Vector3.Zero;
        _humanPresentation.Rotation = new Vector3(0, radians, 0);
    }

    private void RebuildClientProjection(in LocalPlayableAuthorityView view)
    {
        if (_localSession is null || _boundWorld is null || !view.IsReady)
            throw new InvalidOperationException("Authority is not ready.");
        _renderer?.Dispose();
        _cache = new ClientWorldSemanticCache(
            view.PreparedWorld,
            new ClientWorldSemanticCacheLimits(25, 16, 16, 16, 64));
        if (!StageAllChunks())
            throw new InvalidOperationException("The local semantic world could not stage.");

        _assetResolver = new CabinCatalogAssetResolver(_contentOracle!, _resolvedContent!);
        _renderer = new GodotWorldRenderer(
            this,
            _cache,
            GodotWorldRendererLimits.ForStructuralWindow(RenderRadius, 5, 32),
            _assetResolver);
        _window = new GodotSlidingRenderWindow(_cache, _renderer, RenderRadius);
        _sequencer = new ClientWorldInputSequencer(_localSession.ControlSession);
        _orbit.Reset();
        _residentGenerationId = view.GenerationId;
        _player = view.PlayerPosition;
        UpdateHumanPresentation(view.PlayerOrientation);
        TrackPresentation(reset: true);
        DrainRenderer();
    }

    private ClientControlProfileDecision EvaluateProfile(
        in ClientWorldInputSample physical,
        in ClientHorizontalCameraBasis basis)
    {
        if (_profileStopPending)
        {
            _profileStopPending = false;
            return new ClientControlProfileDecision(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    physical.HasFocus ? physical.HeldActions : GamePlayerActions.None,
                    physical.HasFocus),
                false,
                default,
                false,
                default);
        }
        return _profile switch
        {
            ControlProfile.OrbitCommand => _orbit.Evaluate(new ClientOrbitCommandSample(
                physical.HeldActions,
                physical.HasFocus,
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
            ControlProfile.ThirdPersonDirect => new ClientControlProfileDecision(
                new ClientWorldCommandIntent(
                    physical.HasFocus
                        ? _thirdPerson.Evaluate(physical.MoveX, physical.MoveZ, basis)
                        : GameLocomotionUpdate.Stop,
                    physical.HeldActions,
                    physical.HasFocus),
                false,
                default,
                false,
                default),
            ControlProfile.IndependentMoveLook => _independent.Evaluate(
                physical.MoveX,
                physical.MoveZ,
                physical.HeldActions,
                physical.HasFocus,
                _pointerPointValid,
                _pointerPoint),
            _ => throw new InvalidOperationException("Unknown control profile."),
        };
    }

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
        UpdateHumanPresentation(view.PlayerOrientation);
        ObserveAuthorityOutcomes(view);
        TrackPresentation(reset: false);
        _renderer.ProcessFrame(GodotWorldFrameBudget.Default);
    }

    private bool StageAllChunks()
    {
        if (_cache is null)
            return false;
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _))
                    return false;
                if (!_cache.TryStageFromBaseWorld(handle, [], default, out _) ||
                    !_cache.TryPublish(handle, out _))
                    return false;
            }
        }
        return true;
    }

    private void CaptureSemanticHashes()
    {
        if (_cache is null) return;
        int semanticIndex = 0;
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info))
                    throw new InvalidOperationException("A structural semantic chunk was unavailable.");
                _semanticHashesBeforeRefresh[semanticIndex++] = info.SemanticHash;
            }
        }
    }

    private void QueueChangedPublicChunks()
    {
        if (_cache is null || _renderer is null) return;
        int semanticIndex = 0;
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo after))
                    throw new InvalidOperationException("A structural semantic chunk was unavailable after apply.");
                if (after.SemanticHash != _semanticHashesBeforeRefresh[semanticIndex])
                    _ = _renderer.TryQueuePublicRefresh(handle, out _);
                semanticIndex++;
            }
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
                    ? $"Cabin door {(view.LastCommandResult.DoorIsOpen ? "opened" : "closed")}"
                    : $"Interaction: {view.LastCommandResult.Interaction}";
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
        if (_window is null)
            return;
        bool tracked = reset && _window.GetSnapshot().ActiveChunks != 0
            ? _window.TryResetForJump(
                _player, _presentationChanges, out _, out _, out ClientPresentationWindowError error,
                out GodotWorldRenderQueueError rendererError)
            : _window.TryTrackPlayer(
                _player, PresentationCenter(_player), _presentationChanges,
                out _, out _, out error, out rendererError);
        if (!tracked)
        {
            _lastOutcome = $"Presentation boundary: {error}/{rendererError}";
        }
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
        _selection = Nearest(point, GamePlayableWorld.DoorPosition, 1_750)
            ? SelectionKind.CabinDoor
            : Nearest(point, GamePlayableWorld.ChestPosition, 1_750)
                ? SelectionKind.CabinChest
                : Nearest(point, GamePlayableWorld.AnimalPosition, 1_750)
                    ? SelectionKind.WoodlandAnimal
                    : SelectionKind.Terrain;
        _lastOutcome = $"Selected {_selection} at ({point.XMillimeters / 1000d:F1}, {point.ZMillimeters / 1000d:F1}) m";
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
        _pointerPointValid = WorldPosition.TryFromGlobalMillimeters(
            globalX, _player.YMillimeters, globalZ, out WorldPosition candidate) &&
            _cache is not null && _cache.PreparedWorld.TrySampleBaseTerrainHeight(candidate, out _);
        _pointerPoint = new ClientControlWorldPoint(globalX, globalZ);
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
        Vector3 pivot = new(0, CameraPivotMeters, 0);
        _camera.Position = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance + CameraPivotMeters,
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
                _cameraPitch = 1.12f;
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
            _camera.Position.DistanceTo(orbitCamera) < 0.001f))
            throw new InvalidOperationException("Control profile switches did not realize distinct camera modes.");

        ValidateCatalogAndHumanPresentation();
        ProbePlayableChat();
        ProbePlayableInventory();
        ProbeDoorAndChestInteraction();

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

        int before = CountPresentedPrefab(GameWorldPublicPrefabIds.Character);
        RelocateControlledActor(GamePlayableWorld.ClearCrossChunkPosition);
        DriveLocalUntil(() => _player.Chunk == GamePlayableWorld.ClearCrossChunkPosition.Chunk &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.Character) == 1);
        int afterCrossing = CountPresentedPrefab(GameWorldPublicPrefabIds.Character);
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        DriveLocalUntil(() => _player == GamePlayableWorld.PlayerStart &&
            CountPresentedPrefab(GameWorldPublicPrefabIds.Character) == 1);
        int afterReturn = CountPresentedPrefab(GameWorldPublicPrefabIds.Character);
        if (before != 1 || afterCrossing != 1 || afterReturn != 1)
            throw new InvalidOperationException(
                $"Public presentation ghosted across a chunk move: {before}/{afterCrossing}/{afterReturn}.");

        var edge = new WorldPosition(
            GamePlayableWorld.MaximumChunk,
            GamePlayableWorld.MaximumChunk,
            15_000,
            14_000,
            0);
        RelocateControlledActor(edge);
        DriveLocalUntil(() => _player.Chunk == edge.Chunk &&
            _player.Local.XMillimeters == edge.Local.XMillimeters);
        SpatialChunkKey expectedCenter = ChunkKey(
            GamePlayableWorld.MaximumChunk - RenderRadius,
            GamePlayableWorld.MaximumChunk - RenderRadius);
        ClientPresentationWindowSnapshot edgeWindow = _window?.GetSnapshot() ?? default;
        if (_player.Chunk != edge.Chunk || edgeWindow.CenterChunk != expectedCenter ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.X, -15f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14f))
            throw new InvalidOperationException(
                "The finite structural window detached the camera from the edge-chunk player origin.");

        var fartherInsideEdge = new WorldPosition(
            GamePlayableWorld.MaximumChunk,
            GamePlayableWorld.MaximumChunk,
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
    }

    private int CountPresentedPrefab(uint prefabId)
    {
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

    private void ApplyProfileCameraDefaults()
    {
        switch (_profile)
        {
            case ControlProfile.OrbitCommand:
                _cameraYaw = -0.7f;
                _cameraPitch = 1.12f;
                _cameraDistance = 28f;
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
        var title = new Label { Text = "UltraSim — T0.10 Woods / Cabin", ThemeTypeVariation = "HeaderMedium" };
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
            Text = "LMB click select • hold steer • double-click destination • RMB/Esc stop\n" +
                "MMB drag camera • wheel zoom • E use selected door/chest • I inventory\n" +
                "1/F1 Orbit • 2/F2 third-person • 3/F3 fixed iso move/look • F5 save • F9 load",
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
                    if (_overheadSpeech is not null) _overheadSpeech.Text = newest.Text;
                }
                else if (newest.IsSpatial)
                {
                    _overheadAnchor = default;
                    _overheadOriginGenerationId = 0;
                }
            }
            _chatAppliedPresentationCount = snapshot.Applied;
        }

        if (_overheadSpeech is null || _camera is null || _cache is null ||
            !_overheadAnchor.IsValid || Time.GetTicksMsec() >= _overheadExpiresAtMilliseconds ||
            _overheadOriginGenerationId != ReadActiveGenerationId() ||
            !TryFindPublicAnchor(_overheadAnchor, out WorldPosition position))
        {
            if (_overheadSpeech is not null) _overheadSpeech.Visible = false;
            return;
        }
        Vector3 world = new(
            (position.GlobalXMillimeters - _player.GlobalXMillimeters) * 0.001f,
            (position.YMillimeters - _player.YMillimeters) * 0.001f + 2.4f,
            (position.GlobalZMillimeters - _player.GlobalZMillimeters) * 0.001f);
        if (_camera.IsPositionBehind(world))
        {
            _overheadSpeech.Visible = false;
            return;
        }
        Vector2 screen = _camera.UnprojectPosition(world);
        _overheadSpeech.Position = screen - new Vector2(_overheadSpeech.Size.X * 0.5f, 24);
        _overheadSpeech.Visible = true;
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
        _details.Text =
            $"Profile {_profile} • Selected {_selection} • Tick {view.SimulationTick}\n" +
            $"Player {_player.Chunk} + ({_player.Local.XMillimeters}, {_player.Local.ZMillimeters}) mm • " +
            $"door {(view.DoorIsOpen ? "open" : "closed")} • " +
            $"bread in {(view.BreadIsInBackpack ? "backpack" : "chest")}\n" +
            $"rendered {render.ActiveRenderChunks} chunks • pending {render.PendingBuilds}/{render.PendingRetirements}";
    }

    private static SpatialChunkKey ChunkKey(int x, int z) =>
        new(GameWorldState.DefaultSpace, new SpatialChunkCoord(x, z));

    private static SpatialChunkKey PresentationCenter(in WorldPosition player)
    {
        int minimum = checked(GamePlayableWorld.MinimumChunk + RenderRadius);
        int maximum = checked(GamePlayableWorld.MaximumChunk - RenderRadius);
        if (minimum > maximum)
            throw new InvalidOperationException("The authored world cannot contain the render window.");
        return ChunkKey(
            Math.Clamp(player.Chunk.X, minimum, maximum),
            Math.Clamp(player.Chunk.Z, minimum, maximum));
    }

    private static bool Nearest(
        in ClientControlWorldPoint point,
        in WorldPosition target,
        long radius)
    {
        Int128 dx = (Int128)point.XMillimeters - target.GlobalXMillimeters;
        Int128 dz = (Int128)point.ZMillimeters - target.GlobalZMillimeters;
        return dx * dx + dz * dz <= (Int128)radius * radius;
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

        public Mesh? ResolveStaticMesh(WorldStaticRenderKey key) => key.DefinitionId switch
        {
            var id when id.Value == GameWorldStaticDefinitionIds.Tree => _wood,
            var id when id.Value == GameWorldStaticDefinitionIds.Rock => _stone,
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

        public Mesh? ResolvePublicMesh(uint prefabId) => prefabId switch
        {
            GameWorldPublicPrefabIds.Character => _character,
            GameWorldPublicPrefabIds.CabinDoor => _door,
            GameWorldPublicPrefabIds.WorldChest => _chest,
            GameWorldPublicPrefabIds.WoodlandAnimal => _animal,
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
