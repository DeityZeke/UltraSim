using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.Network;

namespace UltraSim.Client.Playable.Godot;

public partial class PlayableWorldClient : Node3D
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
    private const int MaximumPublicRows = 16;

    private ServerRuntime? _runtime;
    private AdmissionService? _admission;
    private UltraSimGameComposition? _composition;
    private GameRuntimePersistence? _persistence;
    private BoundWorldPack? _boundWorld;
    private GamePlayableWorldEntities _entities;
    private ClientWorldSemanticCache? _cache;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientWorldInputSequencer? _sequencer;
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _thirdPerson = new();
    private readonly IndependentMoveLookProfile _independent = new();
    private readonly ClientPresentationChange[] _presentationChanges = new ClientPresentationChange[18];
    private readonly WorldPublicRecord[] _publicRows = new WorldPublicRecord[MaximumPublicRows];
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
    private ulong _saveSequence;
    private bool _initialized;
    private GameChatService? _chatAuthority;
    private RemoteChatDeliveryService? _chatDelivery;
    private RemoteChatServerSession? _chatServer;
    private RemoteChatClient? _chatClient;
    private ClientChatState? _chatState;
    private ProtocolLoopback? _chatLoopback;
    private ulong _chatAppliedPresentationCount;
    private WorldPublicEntityId _overheadAnchor;
    private ulong _overheadOriginGenerationId;
    private ulong _overheadExpiresAtMilliseconds;

    public override void _Ready()
    {
        try
        {
            CreateSceneFurniture();
            StartFreshWorld();
            _initialized = true;
            SetPhysicsProcess(true);
            if (OS.GetCmdlineUserArgs().Contains("--smoke", StringComparer.Ordinal))
                RunLaunchSmokeAcceptance();
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
            _lastOutcome = "Initialization failed — see Godot output";
            UpdateHud();
            SetPhysicsProcess(false);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized || _runtime is null || _sequencer is null || _camera is null)
            return;
        try
        {
            UpdatePointerWorldPoint();
            PumpChat();
            ClientWorldInputSample physical = GodotWorldInput.Capture(
                GodotWorldInputMap.Default,
                GetWindow().HasFocus() && (_chatInput is null || !_chatInput.HasFocus()));
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
            _runtime.Tick();
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
        if (inputEvent is InputEventKey chatKey && chatKey.Pressed && !chatKey.Echo &&
            chatKey.Keycode is global::Godot.Key.Enter or global::Godot.Key.KpEnter)
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
                if (mouse.ButtonIndex == MouseButton.Left)
                {
                    _primaryDown = mouse.Pressed;
                    _primaryPressed |= mouse.Pressed;
                    _primaryReleased |= !mouse.Pressed;
                }
                else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
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
                _cameraYaw -= motion.Relative.X * 0.006f;
                _cameraPitch = Math.Clamp(_cameraPitch - motion.Relative.Y * 0.006f, 0.2f, 1.35f);
                UpdateCamera();
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key.Keycode);
                break;
        }
    }

    public override void _ExitTree()
    {
        _chatServer?.Dispose();
        _chatDelivery?.Dispose();
        _chatAuthority?.Dispose();
        _chatLoopback?.Dispose();
        _renderer?.Dispose();
        _admission?.Dispose();
        _runtime?.Dispose();
    }

    private void StartFreshWorld()
    {
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        _boundWorld = BindPlayablePack(pack);
        _composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        _runtime = new ServerRuntime(
            _composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    128,
                    new PersistenceIdentityOptions(new PersistentWorldId(0x80_4010))),
                WorldClockOptions.Default));
        _runtime.Initialize();
        _runtime.Start();
        _admission = new AdmissionService(_runtime.Game, _composition);
        AdmissionResult entered = _admission.JoinAccountless(new ParticipantId(4010));
        if (!entered.IsEntered)
            throw new InvalidOperationException("The local playable participant could not enter.");
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
                (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
                out _entities))
            throw new InvalidOperationException("The woods/cabin world could not populate.");
        _runtime.Tick();

        StartChat(entered.Participant);

        string saveRoot = ProjectSettings.GlobalizePath("user://t0-10-saves");
        _persistence = new GameRuntimePersistence(
            _runtime.Game,
            _composition,
            RestoreOptions(),
            new SaveSetCoordinator(saveRoot));
        _saveSequence = checked((ulong)Math.Max(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        RebuildClientProjection();
        _lastOutcome = "Fresh woods/cabin world started";
    }

    private void RebuildClientProjection()
    {
        if (_runtime is null || _boundWorld is null)
            throw new InvalidOperationException("Authority is not ready.");
        _renderer?.Dispose();

        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldMutableOverlay>(
                (_, _, game) => game.WorldState.Overlay ?? WorldMutableOverlay.Empty(_boundWorld),
                out WorldMutableOverlay overlay))
            throw new InvalidOperationException("Current prepared-world lineage was unavailable.");
        _cache = new ClientWorldSemanticCache(
            new WorldPreparedView(_boundWorld, overlay),
            new ClientWorldSemanticCacheLimits(25, 16, 16, 16, 64));
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (_, _, game) => StageAllChunks(game.WorldState.Publications),
                out bool staged) || !staged)
            throw new InvalidOperationException("The local semantic world could not stage.");

        _renderer = new GodotWorldRenderer(
            this,
            _cache,
            GodotWorldRendererLimits.ForStructuralWindow(RenderRadius, 5, 32),
            new PlayableAssetResolver());
        _window = new GodotSlidingRenderWindow(_cache, _renderer, RenderRadius);
        _sequencer = new ClientWorldInputSequencer();
        _orbit.Reset();
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, _, game) => game.WorldActions.BeginControlSession(
                    world, Resolve(world, _entities.PlayerId), _sequencer.ControlSession),
                out GameWorldActionStatus bound) || bound != GameWorldActionStatus.Accepted)
            throw new InvalidOperationException($"Local control binding failed: {bound}.");
        ReadAuthorityView();
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
        if (_runtime is null)
            return;
        GamePlayerCommand admitted = command;
        ClientControlWorldPoint facingPoint = lookPoint;
        bool interactionPressed =
            (command.PressedActions & GamePlayerActions.Interact) != 0;
        bool chestInteraction = _selection == SelectionKind.CabinChest && interactionPressed;
        bool unsupportedInteraction = interactionPressed &&
            _selection is not (SelectionKind.CabinDoor or SelectionKind.CabinChest);
        if (chestInteraction || unsupportedInteraction)
        {
            admitted = command with
            {
                HeldActions = command.HeldActions & ~GamePlayerActions.Interact,
                PressedActions = command.PressedActions & ~GamePlayerActions.Interact,
            };
        }
        PersistentEntityId targetId = _selection == SelectionKind.CabinDoor
            ? _entities.CabinDoorId
            : default;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayerCommandResult>(
                (world, generationId, game) =>
                {
                    Entity target = targetId.IsValid ? Resolve(world, targetId) : Entity.Null;
                    game.WorldActions.ApplyPlayerCommand(
                        world,
                        game.WorldState,
                        Resolve(world, _entities.PlayerId),
                        admitted,
                        target,
                        out GamePlayerCommandResult result);
                    if (result.WasAdmitted && faceLookPoint &&
                        (!facingRequiresAcceptedLocomotion ||
                         result.Locomotion == GameWorldActionStatus.Accepted))
                    {
                        GameWorldActionStatus facing = game.WorldActions.FaceActorToward(
                            world,
                            game.WorldState,
                            Resolve(world, _entities.PlayerId),
                            facingPoint.XMillimeters,
                            facingPoint.ZMillimeters,
                            out _);
                        if (facing != GameWorldActionStatus.Accepted)
                            throw new InvalidOperationException($"Profile facing failed: {facing}.");
                    }
                    return result;
                }, out GamePlayerCommandResult result))
            throw new InvalidOperationException("The OfflineDirect command had no authority generation.");

        if (unsupportedInteraction)
        {
            _lastOutcome = "Select the cabin door or chest before using Interact";
        }
        else if (chestInteraction)
        {
            GameWorldActionStatus chest = ToggleBreadContainer();
            _lastOutcome = chest == GameWorldActionStatus.Accepted
                ? "Moved bread between backpack and cabin chest"
                : $"Chest use: {chest}";
        }
        else if (result.Interaction != GameWorldActionStatus.NotRequested)
        {
            _lastOutcome = result.Interaction == GameWorldActionStatus.Accepted
                ? $"Cabin door {(result.DoorIsOpen ? "opened" : "closed")}"
                : $"Interaction: {result.Interaction}";
        }
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

    private GameWorldActionStatus ToggleBreadContainer()
    {
        if (_runtime is null)
            return GameWorldActionStatus.InvariantViolation;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) =>
                {
                    Entity player = Resolve(world, _entities.PlayerId);
                    Entity bread = Resolve(world, _entities.BreadId);
                    Entity backpack = Resolve(world, _entities.BackpackId);
                    Entity chest = Resolve(world, _entities.CabinChestId);
                    if (!game.Inventory.TryReadItem(world, bread, out InventoryItem item))
                        return GameWorldActionStatus.MissingTarget;
                    Entity source = item.Parent;
                    Entity destination = source == backpack ? chest : backpack;
                    return game.WorldActions.MoveItemToNearbyContainer(
                        world, game.WorldState, game.Inventory,
                        player, bread, source, destination, out _);
                }, out GameWorldActionStatus status))
            return GameWorldActionStatus.InvariantViolation;
        return status;
    }

    private void SynchronizeProjection()
    {
        if (_runtime is null || _cache is null || _renderer is null)
            return;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (_, _, game) => RefreshAllChunks(game.WorldState.Publications),
                out bool refreshed) || !refreshed)
            throw new InvalidOperationException("Authoritative publication could not refresh locally.");
        ReadAuthorityView();
        TrackPresentation(reset: false);
        _renderer.ProcessFrame(GodotWorldFrameBudget.Default);
    }

    private bool StageAllChunks(ChunkPublicationStore publications)
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
                ChunkPublicationReadStatus status = publications.TryCopySnapshot(
                    key, _publicRows, out ChunkPublicationBatch batch);
                bool staged = status == ChunkPublicationReadStatus.Snapshot
                    ? _cache.TryStageFromBaseWorld(handle, _publicRows.AsSpan(0, batch.Count), batch.Cursor, out _)
                    : status == ChunkPublicationReadStatus.ChunkAbsent &&
                        _cache.TryStageFromBaseWorld(handle, [], default, out _);
                if (!staged || !_cache.TryPublish(handle, out _))
                    return false;
            }
        }
        return true;
    }

    private bool RefreshAllChunks(ChunkPublicationStore publications)
    {
        if (_cache is null || _renderer is null)
            return false;
        int semanticIndex = 0;
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info))
                    return false;
                _semanticHashesBeforeRefresh[semanticIndex++] = info.SemanticHash;
            }
        }

        semanticIndex = 0;
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
                    return false;
                ChunkPublicationReadStatus status = publications.TryCopySnapshot(
                    key, _publicRows, out ChunkPublicationBatch batch);
                bool applied = status == ChunkPublicationReadStatus.Snapshot
                    ? _cache.TryApplySnapshot(handle, batch, _publicRows.AsSpan(0, batch.Count), out _)
                    : status == ChunkPublicationReadStatus.ChunkAbsent &&
                        _cache.TryApplyChunkAbsent(handle, publications.Epoch, batch, out _);
                if (!applied)
                    return false;
            }
        }

        // Applying one snapshot can supersede the same public identity in a different resident
        // chunk. Compare the complete resident set after the transaction so both the old and new
        // presentation layers are refreshed; checking only the currently read chunk leaves ghosts.
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo after))
                    return false;
                if (after.SemanticHash != _semanticHashesBeforeRefresh[semanticIndex])
                    _ = _renderer.TryQueuePublicRefresh(handle, out _);
                semanticIndex++;
            }
        }
        return true;
    }

    private void ReadAuthorityView()
    {
        if (_runtime is null)
            return;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
                (world, _, game) => game.WorldState.TryReadPosition(
                    world, Resolve(world, _entities.PlayerId), out WorldPosition position)
                        ? position
                        : throw new InvalidOperationException("The controlled actor has no position."),
                out _player))
            throw new InvalidOperationException("The controlled actor could not be read.");
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
        _camera.Position = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance,
            MathF.Cos(_cameraYaw) * horizontal);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private void HandleKey(global::Godot.Key key)
    {
        switch (key)
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

    private void RunLaunchSmokeAcceptance()
    {
        if (_camera is null || _renderer is null || _runtime is null || _sequencer is null)
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

        ProbePlayableChat();

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
        _runtime.Tick();
        SynchronizeProjection();
        DrainRenderer();
        int afterCrossing = CountPresentedPrefab(GameWorldPublicPrefabIds.Character);
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        _runtime.Tick();
        SynchronizeProjection();
        DrainRenderer();
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
        _runtime.Tick();
        SynchronizeProjection();
        DrainRenderer();
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
        _runtime.Tick();
        SynchronizeProjection();
        DrainRenderer();
        if (!Mathf.IsEqualApprox(_renderer.Root.Position.X, -15.5f) ||
            !Mathf.IsEqualApprox(_renderer.Root.Position.Z, -14.5f))
            throw new InvalidOperationException(
                "Same-edge-chunk movement did not keep the camera centered on the player.");

        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        _runtime.Tick();
        SynchronizeProjection();
        DrainRenderer();

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
            _runtime.Tick();
            SynchronizeProjection();
            DrainRenderer();
            if (ReadPlayerOrientation() != expected)
                throw new InvalidOperationException(
                    $"{profile} did not face its accepted movement direction.");
        }
    }

    private void ProbePlayableChat()
    {
        if (_chatState is null || _chatFeed is null || _overheadSpeech is null)
            throw new InvalidOperationException("Playable Chat presentation was not composed.");
        ulong before = _chatState.GetSnapshot().Applied;
        SubmitChat("playable speech");
        for (int step = 0; step < 2_048 && _chatState.GetSnapshot().Applied == before; step++)
            PumpChat();
        if (_chatState.GetSnapshot().Applied != before + 1)
            throw new InvalidOperationException("Playable spatial speech did not apply.");
        UpdateChatPresentation();
        if (!_chatFeed.Text.Contains("playable speech", StringComparison.Ordinal) ||
            !_overheadAnchor.IsValid || !TryFindPublicAnchor(_overheadAnchor, out _))
            throw new InvalidOperationException("Playable speech did not reach feed and public-anchor presentation.");

        before = _chatState.GetSnapshot().Applied;
        SubmitChat("/where");
        for (int step = 0; step < 2_048 && _chatState.GetSnapshot().Applied == before; step++)
            PumpChat();
        UpdateChatPresentation();
        if (_chatState.GetSnapshot().Applied != before + 1 ||
            !_chatFeed.Text.Contains("Where:", StringComparison.Ordinal))
            throw new InvalidOperationException("Playable private /where result did not reach Chat.");
    }

    private WorldYaw ReadPlayerOrientation()
    {
        if (_runtime is null || !_runtime.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, WorldYaw>(
                (world, _, game) => game.WorldState.TryReadPublic(
                    world,
                    Resolve(world, _entities.PlayerId),
                    out WorldPublicDescriptor descriptor)
                        ? descriptor.Orientation
                        : throw new InvalidOperationException("The playable character lost public state."),
                out WorldYaw orientation))
            throw new InvalidOperationException("The playable character orientation was unavailable.");
        return orientation;
    }

    private void RelocateControlledActor(in WorldPosition requested)
    {
        if (_runtime is null)
            throw new InvalidOperationException("Authority is not available for relocation.");
        WorldPosition target = requested;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generationId, game) =>
                {
                    GameWorldActionStatus result = game.WorldActions.RelocateActor(
                        world,
                        game.WorldState,
                        Resolve(world, _entities.PlayerId),
                        target,
                        out WorldPosition relocated);
                    _ = generationId;
                    _ = relocated;
                    return result;
                },
                out GameWorldActionStatus status) || status != GameWorldActionStatus.Accepted)
            throw new InvalidOperationException($"Launch-smoke relocation failed: {status}.");
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
        if (_persistence is null)
            return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ulong sequence = _saveSequence++;
        GameSaveResult result = _persistence.TrySave(
            new SaveSetContext(sequence, now), static capture => capture());
        _lastOutcome = result.Succeeded
            ? $"Saved generation {sequence}"
            : $"Save failed: {result.Failure?.Message}";
    }

    private void LoadWorld()
    {
        if (_persistence is null)
            return;
        GameLoadResult result = _persistence.TryLoad();
        if (!result.Succeeded)
        {
            _lastOutcome = $"Load failed: {result.Failure?.Message}";
            return;
        }
        RebuildClientProjection();
        _selection = SelectionKind.None;
        _lastOutcome = $"Restored generation {result.GenerationId}; control intent reset";
    }

    private void CreateSceneFurniture()
    {
        _camera = new Camera3D
        {
            Name = "OrbitCamera",
            Current = true,
            Near = 0.1f,
            Far = 500f,
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

        var layer = new CanvasLayer { Name = "HUD" };
        AddChild(layer);
        var panel = new PanelContainer
        {
            OffsetLeft = 16,
            OffsetTop = 16,
            OffsetRight = 590,
            OffsetBottom = 220,
        };
        layer.AddChild(panel);
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
                "MMB drag camera • wheel zoom • E use selected door/chest\n" +
                "1/F1 Orbit • 2/F2 third-person • 3/F3 fixed iso move/look • F5 save • F9 load",
        });

        var chatPanel = new PanelContainer
        {
            OffsetLeft = 16,
            OffsetTop = 650,
            OffsetRight = 590,
            OffsetBottom = 890,
        };
        layer.AddChild(chatPanel);
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
            MaxLength = UltraSimGameProtocol.MaximumChatTextUtf8Bytes,
        };
        _chatInput.TextSubmitted += SubmitChat;
        chatEntry.AddChild(_chatInput);
        _overheadSpeech = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layer.AddChild(_overheadSpeech);

        void AddProfileButton(string text, ControlProfile profile)
        {
            var button = new Button { Text = text };
            button.Pressed += () => SetProfile(profile);
            profiles.AddChild(button);
        }
    }

    private void StartChat(ParticipantId participant)
    {
        if (_runtime is null || _admission is null)
            throw new InvalidOperationException("Chat requires active Game authority and Admission.");
        GameChatLimits chatLimits = GameChatLimits.Default with
        {
            MaximumParticipants = 8,
            MaximumRecipientsPerSpeech = 8,
        };
        _chatAuthority = new GameChatService(_runtime.Game, _admission, chatLimits);
        if (!ProtocolLoopback.TryCreate(
                NetworkProtocolLimits.Default,
                new DeterministicDatagramLimits(2_048, 1_472),
                default,
                out ProtocolLoopback? loopback,
                out _,
                UltraSimGameProtocol.Manifest))
            throw new InvalidOperationException("The playable Chat protocol path could not establish.");
        _chatLoopback = loopback;
        _chatDelivery = new RemoteChatDeliveryService(
            _runtime.Game,
            _chatAuthority,
            RemoteChatServerLimits.Default with
            {
                MaximumSessions = 8,
                MaximumPendingDeliveries = 64,
                MaximumPendingDeliveriesPerSession = 16,
            });
        const ulong applicationSessionEpoch = 0xC0_1001;
        _chatServer = _chatDelivery.Register(
            loopback!.Server, participant, applicationSessionEpoch);
        _chatState = new ClientChatState(
            applicationSessionEpoch,
            new ClientChatLimits(64, 256, 128));
        _chatClient = new RemoteChatClient(
            loopback.Client, applicationSessionEpoch, _chatState);
    }

    private void SubmitChat(string text)
    {
        if (_chatInput is null || _chatClient is null)
            return;
        ClientChatSpeechMode mode = _chatMode is null
            ? ClientChatSpeechMode.Say
            : (ClientChatSpeechMode)_chatMode.GetSelectedId();
        if (_chatClient.TrySubmit(text, mode))
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
        if (_chatLoopback is null || _chatServer is null || _chatClient is null)
            return;
        _ = _chatServer.TryProgress();
        _ = _chatClient.TryProgress();
        _chatLoopback.Pump();
        Drain(_chatLoopback.Server, _chatServer);
        Drain(_chatLoopback.Client, _chatClient);

        static void Drain(NetworkProtocolPeer peer, IRemoteGameProtocolHandler handler)
        {
            while (peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                _ = handler.TryProcess(in message, payload);
                _ = peer.TryConsumeReceived();
            }
        }
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
        if (_runtime is null || !_runtime.Game.TryUseActiveGeneration(
                static (_, generationId) => generationId, out ulong generationId))
            return 0;
        return generationId;
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
        if (!_initialized || _runtime is null || _renderer is null)
        {
            _details.Text = "Not running";
            return;
        }
        bool doorOpen = false;
        bool breadInChest = false;
        _ = _runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                _ = game.WorldActions.TryReadCabinDoor(
                    world, Resolve(world, _entities.CabinDoorId), out doorOpen);
                if (game.Inventory.TryReadItem(
                        world, Resolve(world, _entities.BreadId), out InventoryItem item))
                    breadInChest = item.Parent == Resolve(world, _entities.CabinChestId);
                return true;
            }, out _);
        GodotWorldRendererSnapshot render = _renderer.GetSnapshot();
        _details.Text =
            $"Profile {_profile} • Selected {_selection} • Tick {_runtime.SimulationTick}\n" +
            $"Player {_player.Chunk} + ({_player.Local.XMillimeters}, {_player.Local.ZMillimeters}) mm • " +
            $"door {(doorOpen ? "open" : "closed")} • bread in {(breadInChest ? "chest" : "backpack")}\n" +
            $"rendered {render.ActiveRenderChunks} chunks • pending {render.PendingBuilds}/{render.PendingRetirements}";
    }

    private static BoundWorldPack BindPlayablePack(WorldPack pack)
    {
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        if (!pack.TryBind(binding, out BoundWorldPack? bound, out WorldPackBindingError error))
            throw new InvalidOperationException($"The playable pack did not bind: {error}.");
        return bound!;
    }

    private static PersistenceRestoreOptions RestoreOptions() => new(
        new PersistenceLimits(
            maximumArtifactBytes: 16 * 1024 * 1024,
            maximumEntities: 100_000,
            maximumShapes: 64,
            maximumComponents: 64,
            maximumSections: 256,
            maximumComponentPayloadBytes: 8 * 1024 * 1024,
            maximumTemporaryBytes: 64 * 1024 * 1024),
        ChunkLayoutOptions.KiB16);

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

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id.Value} did not resolve.");

    private static bool Nearest(
        in ClientControlWorldPoint point,
        in WorldPosition target,
        long radius)
    {
        Int128 dx = (Int128)point.XMillimeters - target.GlobalXMillimeters;
        Int128 dz = (Int128)point.ZMillimeters - target.GlobalZMillimeters;
        return dx * dx + dz * dz <= (Int128)radius * radius;
    }

    private sealed class PlayableAssetResolver : IGodotWorldAssetResolver
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
        private readonly Mesh _wallEight = GroundedBox(
            new Vector3(8f, 2.5f, 0.3f), new Color(0.42f, 0.27f, 0.13f));
        private readonly Mesh _wallSix = GroundedBox(
            new Vector3(6f, 2.5f, 0.3f), new Color(0.42f, 0.27f, 0.13f));
        private readonly Mesh _wallThree = GroundedBox(
            new Vector3(3f, 2.5f, 0.3f), new Color(0.42f, 0.27f, 0.13f));
        private readonly Mesh _roof = GroundedBox(
            new Vector3(8.3f, 0.25f, 6.3f), new Color(0.30f, 0.16f, 0.07f, 0.42f), transparent: true);
        private readonly Mesh _shelf = GroundedBox(
            new Vector3(0.9f, 1.8f, 0.35f), new Color(0.34f, 0.20f, 0.10f));
        private readonly Mesh _book = GroundedBox(
            new Vector3(0.24f, 0.18f, 0.18f), new Color(0.52f, 0.18f, 0.12f));
        private readonly Mesh _fallbackStructure = GroundedBox(
            new Vector3(1f, 2f, 0.35f), new Color(0.42f, 0.27f, 0.13f));
        private readonly Mesh _character = CharacterMesh(new Color(0.82f, 0.55f, 0.18f));
        private readonly Mesh _door = GroundedBox(
            new Vector3(1.6f, 2.1f, 0.22f), new Color(0.36f, 0.18f, 0.07f));
        private readonly Mesh _chest = GroundedBox(
            new Vector3(1.2f, 0.8f, 0.8f), new Color(0.44f, 0.25f, 0.09f));
        private readonly Mesh _animal = GroundedBox(
            new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));

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

        private static ArrayMesh CharacterMesh(Color color)
        {
            StandardMaterial3D material = Material(color);
            return GodotMeshGeometry.CreateGroundedPointerWedge(
                0.42f, 0.42f, 0.68f, 1.8f, material);
        }
    }
}
