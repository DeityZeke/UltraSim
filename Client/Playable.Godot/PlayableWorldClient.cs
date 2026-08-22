using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

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
    private Camera3D? _camera;
    private Label? _status;
    private Label? _details;
    private WorldPosition _player;
    private ClientControlWorldPoint _pointerPoint;
    private bool _pointerPointValid;
    private bool _primaryDown;
    private bool _primaryPressed;
    private bool _primaryReleased;
    private bool _secondaryPressed;
    private bool _cancelPressed;
    private float _cameraYaw = -0.7f;
    private float _cameraPitch = 0.75f;
    private float _cameraDistance = 24f;
    private ControlProfile _profile;
    private SelectionKind _selection;
    private string _lastOutcome = "Ready";
    private ulong _saveSequence;
    private bool _initialized;

    public override void _Ready()
    {
        try
        {
            CreateSceneFurniture();
            StartFreshWorld();
            _initialized = true;
            SetPhysicsProcess(true);
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
            ClientWorldInputSample physical = GodotWorldInput.Capture(
                GodotWorldInputMap.Default, GetWindow().HasFocus());
            ClientHorizontalCameraBasis basis = CameraBasis();
            ClientControlProfileDecision decision = EvaluateProfile(physical, basis);
            if (decision.SelectionRequested)
                Select(decision.SelectionPoint);
            if (!_sequencer.TryCreateSemanticCommand(decision.Command, out GamePlayerCommand command))
                throw new InvalidOperationException("The selected control profile produced an invalid command.");

            ApplyCommand(command);
            _runtime.Tick();
            SynchronizeProjection();
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
                else if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
                {
                    _cameraDistance = Math.Max(7f, _cameraDistance - 2f);
                }
                else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
                {
                    _cameraDistance = Math.Min(55f, _cameraDistance + 2f);
                }
                break;
            case InputEventMouseMotion motion when
                (motion.ButtonMask & MouseButtonMask.Middle) != 0:
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
        return _profile switch
        {
            ControlProfile.OrbitCommand => _orbit.Evaluate(new ClientOrbitCommandSample(
                physical.MoveX,
                physical.MoveZ,
                physical.HeldActions,
                physical.HasFocus,
                basis,
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
                basis,
                _pointerPointValid,
                _pointerPoint),
            _ => throw new InvalidOperationException("Unknown control profile."),
        };
    }

    private void ApplyCommand(in GamePlayerCommand command)
    {
        if (_runtime is null)
            return;
        GamePlayerCommand admitted = command;
        bool chestInteraction = _selection == SelectionKind.CabinChest &&
            (command.PressedActions & GamePlayerActions.Interact) != 0;
        if (chestInteraction)
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
                    return result;
                }, out GamePlayerCommandResult result))
            throw new InvalidOperationException("The OfflineDirect command had no authority generation.");

        if (chestInteraction)
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
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = ChunkKey(x, z);
                if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo before))
                    return false;
                ChunkPublicationReadStatus status = publications.TryCopySnapshot(
                    key, _publicRows, out ChunkPublicationBatch batch);
                bool applied = status == ChunkPublicationReadStatus.Snapshot
                    ? _cache.TryApplySnapshot(handle, batch, _publicRows.AsSpan(0, batch.Count), out _)
                    : status == ChunkPublicationReadStatus.ChunkAbsent &&
                        _cache.TryApplyChunkAbsent(handle, publications.Epoch, batch, out _);
                if (!applied || !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo after))
                    return false;
                if (after.SemanticHash != before.SemanticHash)
                    _ = _renderer.TryQueuePublicRefresh(handle, out _);
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
                _player, _presentationChanges, out _, out _, out error, out rendererError);
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
                SetProfile(ControlProfile.OrbitCommand);
                break;
            case global::Godot.Key.F2:
                SetProfile(ControlProfile.ThirdPersonDirect);
                break;
            case global::Godot.Key.F3:
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
        _cancelPressed = true;
        _lastOutcome = $"Control profile: {profile}";
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
            OffsetBottom = 180,
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
        stack.AddChild(new Label
        {
            Text = "LMB click select • hold steer • double-click destination • RMB/Esc stop\n" +
                "WASD direct • MMB drag orbit • wheel zoom • E use selected door/chest\n" +
                "F1 OrbitCommand • F2 ThirdPersonDirect • F3 IndependentMoveLook • F5 save • F9 load",
        });
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
        private readonly BoxMesh _wood = Box(new Vector3(0.75f, 2.5f, 0.75f), new Color(0.24f, 0.14f, 0.07f));
        private readonly BoxMesh _stone = Box(Vector3.One, new Color(0.34f, 0.36f, 0.34f));
        private readonly BoxMesh _structure = Box(new Vector3(1f, 2f, 0.35f), new Color(0.42f, 0.27f, 0.13f));
        private readonly BoxMesh _character = Box(new Vector3(0.65f, 1.8f, 0.65f), new Color(0.82f, 0.55f, 0.18f));
        private readonly BoxMesh _door = Box(new Vector3(1.1f, 2.1f, 0.22f), new Color(0.36f, 0.18f, 0.07f));
        private readonly BoxMesh _chest = Box(new Vector3(1.2f, 0.8f, 0.8f), new Color(0.44f, 0.25f, 0.09f));
        private readonly BoxMesh _animal = Box(new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));

        public Mesh? ResolveStaticMesh(WorldStaticRenderKey key) => key.DefinitionId switch
        {
            var id when id.Value == GameWorldStaticDefinitionIds.Tree => _wood,
            var id when id.Value == GameWorldStaticDefinitionIds.Rock => _stone,
            _ => _structure,
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

        private static BoxMesh Box(Vector3 size, Color color) => new()
        {
            Size = size,
            Material = Material(color),
        };
    }
}
