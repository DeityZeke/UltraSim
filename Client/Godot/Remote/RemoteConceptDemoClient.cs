#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using Godot;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Client.StandardUi;
using UltraSim.Game.Admission;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;

namespace UltraSim.Client.Godot;

public enum RemoteConceptDemoClientState : byte
{
    AwaitingContent = 1,
    AwaitingGuest = 2,
    AdmissionPending = 3,
    Active = 4,
    Faulted = 5,
}

public readonly record struct RemoteConceptDemoClientSnapshot(
    RemoteConceptDemoClientState State,
    RemotePlayableSessionSnapshot Session,
    GodotWorldRendererSnapshot Renderer,
    WorldPosition Position,
    int RenderedChunks,
    int TerrainTriangles,
    int OpenUiViews,
    GodotActorPresenterSnapshot Actors,
    bool UsedPrimitiveSubstrate,
    bool SmokeComplete,
    string? Fault);

/// <summary>
/// Native A0.8 consumer over one authenticated remote application session. The Online product
/// starts with an empty local content catalog; authenticated content supplies World/UI bytes while
/// Godot's tiny primitive substrate remains available for every unresolved visual leaf.
/// </summary>
public sealed partial class RemoteConceptDemoClient : Node3D
{
    private const int RenderRadiusChunks = 1;
    private const float CameraPivotMeters = 1.6f;
    private const float MinimumCameraDistanceMeters = 3f;
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _direct = new();
    private readonly IndependentMoveLookProfile _independent = new();
    private readonly ClientPresentationChange[] _presentationChanges = new ClientPresentationChange[18];
    private readonly ClientPresentationBinding[] _presentationBindings = new ClientPresentationBinding[9];
    private readonly ClientSemanticChunkHandle[] _actorPresentedChunks =
        new ClientSemanticChunkHandle[9];
    private readonly ClientActorPresentationChange[] _actorChanges =
        new ClientActorPresentationChange[128];
    private readonly ClientChatOccurrence[] _chatRows = new ClientChatOccurrence[64];
    private ContentCache? _cache;
    private HttpContentPackageSource? _source;
    private RemotePlayableSession? _session;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientActorPresentationState? _actorState;
    private GodotActorPresenter? _actorPresenter;
    private ResolvedClientSettings? _settings;
    private Camera3D? _camera;
    private Control? _ownedHud;
    private Control? _uiHost;
    private Label? _status;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private GodotWorldHoverTooltip? _hoverTooltip;
    private WorldPosition _player;
    private WorldPosition _smokeStart;
    private ulong _lastOwnerState;
    private ulong _lastPrimaryCommandSequence;
    private ulong _lastChatApplied;
    private bool _inventoryRequested;
    private bool _primaryDown;
    private bool _primaryPressed;
    private bool _primaryReleased;
    private bool _secondaryPressed;
    private bool _pointerPointValid;
    private ClientControlWorldPoint _pointerPoint;
    private WorldPublicEntityId _pointerTarget;
    private WorldPublicEntityId _selectedTarget;
    private float _cameraYaw = -0.7f;
    private float _cameraPitch = 0.34f;
    private float _cameraDistance = 8.5f;
    private float _cameraPivotMeters = CameraPivotMeters;
    private bool _smoke;
    private bool _gameplayInputAllowed;
    private int _smokePhase;
    private ulong _lastSmokeDiagnosticMilliseconds;
    private int _terrainTriangles;
    private bool _configured;
    private bool _disposed;
    private RemoteConceptDemoClientState _state = RemoteConceptDemoClientState.AwaitingContent;
    private string? _fault;

    public void Configure(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        UltraSim.Game.Application.ApplicationRealmId realm,
        string cacheRoot,
        CanvasLayer hudLayer,
        ResolvedClientSettings settings,
        bool allowInsecurePrivateContent,
        bool smoke)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentNullException.ThrowIfNull(hudLayer);
        ArgumentNullException.ThrowIfNull(settings);
        if (_configured || IsInsideTree())
            throw new InvalidOperationException("Remote composition must be configured before entering the tree.");

        if (!ContentCache.TryOpen(
                cacheRoot, out _cache, out ContentCacheError cacheError))
            throw new InvalidOperationException($"The content cache could not open: {cacheError}.");
        try
        {
            _source = new HttpContentPackageSource(new ContentPackageSourceLimits(
                maximumArchiveBytes: 768L * 1024 * 1024,
                requestTimeoutMilliseconds: 30_000,
                allowInsecureServerHostedHttp: allowInsecurePrivateContent,
                allowPrivateNetworkTargets: allowInsecurePrivateContent,
                maximumConcurrentRequests: 4));
            _ownedHud = FullRectControl("RemotePlayableHUD");
            hudLayer.AddChild(_ownedHud);
            _uiHost = FullRectControl("RemoteServerUi");
            _ownedHud.AddChild(_uiHost);
            _session = new RemotePlayableSession(
                peer,
                applicationSessionEpoch,
                new RemoteRequiredContentBinding(
                    realm, _cache!, ContentBaseCatalog.Empty, _source),
                RemoteConceptDemoWorldProfile.CreateRequiredBinding(),
                new GodotUiRenderer(_uiHost),
                new ClientChatLimits(64, 256, 128),
                standardUiDefinitionResolver: ClientStandardUiLibrary.Resolve);
            _settings = settings;
            _smoke = smoke;
            BuildBootstrapHud();
            _configured = true;
        }
        catch
        {
            _source?.Dispose();
            _cache?.Dispose();
            _source = null;
            _cache = null;
            throw;
        }
    }

    public RemoteConceptDemoClientState State => _state;

    public void ApplySettings(ResolvedClientSettings settings)
    {
        EnsureOperational();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _orbit.Reset();
        _direct.Reset();
        _independent.Reset();
        _primaryDown = false;
        _primaryPressed = false;
        _primaryReleased = false;
        _secondaryPressed = false;
        ApplyCameraProfile();
    }

    public bool TrySubmitGuest(in GameGuestAdmissionRequest request)
    {
        EnsureOperational();
        if (_state != RemoteConceptDemoClientState.AwaitingGuest ||
            !_session!.TrySubmitGuest(in request))
            return false;
        _state = RemoteConceptDemoClientState.AdmissionPending;
        UpdateHud("Guest admission pending…");
        return true;
    }

    public void Pump(bool gameplayInputAllowed)
    {
        EnsureOperational();
        _gameplayInputAllowed = gameplayInputAllowed &&
            _state == RemoteConceptDemoClientState.Active;
        if (_state == RemoteConceptDemoClientState.Faulted) return;
        try
        {
            _ = _session!.ProcessReceived();
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            ReportSmokeProgress(in snapshot);
            if (snapshot.StructuralWorld.State == RemoteStructuralWorldState.Rejected ||
                snapshot.Content.State == RemoteContentClientState.Rejected ||
                snapshot.Playable.State is RemotePlayableClientState.Rejected or
                    RemotePlayableClientState.Faulted)
            {
                Fault(snapshot.Playable.Detail ?? snapshot.Content.Detail ??
                    $"Remote world failed: {snapshot.StructuralWorld.Resolution.Status}/" +
                    snapshot.StructuralWorld.PublicationError);
                return;
            }
            if (_state == RemoteConceptDemoClientState.AwaitingContent &&
                snapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready)
            {
                _state = RemoteConceptDemoClientState.AwaitingGuest;
                UpdateHud("Required world and UI content applied; choose a guest identity.");
            }
            if (_state == RemoteConceptDemoClientState.AdmissionPending &&
                snapshot.Playable.State == RemotePlayableClientState.Active &&
                snapshot.Playable.StateSequence != 0)
            {
                BuildWorldPresentation(snapshot.Playable);
                _state = RemoteConceptDemoClientState.Active;
                UpdateHud("Remote Concept Demo active");
            }
            if (_state != RemoteConceptDemoClientState.Active) return;

            if (!gameplayInputAllowed) _actorPresenter?.SetRevealHeld(false);

            SynchronizePresentation(snapshot.Playable);
            if (!_smoke)
            {
                UpdatePointerWorldPoint();
                UpdateWorldHover();
                SubmitPhysicalInput(gameplayInputAllowed);
            }
            else
                ProgressSmoke(snapshot);
            UpdateChat();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fault(exception.ToString());
        }
    }

    public RemoteConceptDemoClientSnapshot GetSnapshot()
    {
        EnsureOperational();
        GodotWorldRendererSnapshot renderer = _renderer?.GetSnapshot() ?? default;
        return new RemoteConceptDemoClientSnapshot(
            _state, _session!.GetSnapshot(), renderer, _player,
            renderer.ActiveRenderChunks, _terrainTriangles,
            _session.UiClient.OpenViewCount,
            _actorPresenter?.GetSnapshot() ?? default,
            true,
            _smokePhase == 6,
            _fault);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_state != RemoteConceptDemoClientState.Active || _settings is null) return;
        if (TryHandleFocusedChatInput(inputEvent)) return;
        if (!_gameplayInputAllowed) return;
        if (inputEvent is InputEventKey chat && chat.Pressed && !chat.Echo &&
            GodotWorldInput.Matches(chat, _settings, ClientSemanticAction.Chat))
        {
            _chatInput?.GrabFocus();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey reveal && !reveal.Echo && IsControlKey(reveal))
        {
            _actorPresenter?.SetRevealHeld(reveal.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo &&
            GodotWorldInput.Matches(key, _settings, ClientSemanticAction.OpenInventory))
        {
            if (!TryRequestCloseOpenInventory() && _session!.UiClient.OpenViewCount == 0)
                _inventoryRequested = true;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventMouseButton mouse)
        {
            if (GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Select) ||
                GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.Destination))
            {
                _primaryDown = mouse.Pressed;
                _primaryPressed |= mouse.Pressed;
                _primaryReleased |= !mouse.Pressed;
            }
            else if (mouse.Pressed &&
                GodotWorldInput.Matches(mouse, _settings, ClientSemanticAction.ContextMenu))
                _secondaryPressed = true;
            else if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                _cameraDistance = Math.Max(MinimumCameraDistanceMeters, _cameraDistance - 2f);
                UpdateCamera();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                _cameraDistance = Math.Min(55f, _cameraDistance + 2f);
                UpdateCamera();
            }
        }
        else if (inputEvent is InputEventMouseMotion motion &&
            (motion.ButtonMask & MouseButtonMask.Middle) != 0)
        {
            float sensitivity = _settings.PointerSensitivity * 0.006f;
            float invert = _settings.InvertY ? -1f : 1f;
            _cameraYaw -= motion.Relative.X * sensitivity;
            _cameraPitch = Math.Clamp(
                _cameraPitch - motion.Relative.Y * sensitivity * invert, 0.2f, 1.35f);
            UpdateCamera();
        }
    }

    public override void _ExitTree() => DisposeComposition();

    public void DisposeComposition()
    {
        if (_disposed) return;
        _disposed = true;
        _actorPresenter?.Dispose();
        _renderer?.Dispose();
        _ = _session?.TryRequestDetach();
        _session?.Dispose();
        _source?.Dispose();
        _cache?.Dispose();
        _renderer = null;
        _actorPresenter = null;
        _actorState = null;
        _session = null;
        _source = null;
        _cache = null;
        if (_ownedHud is not null && GodotObject.IsInstanceValid(_ownedHud))
        {
            _ownedHud.GetParent()?.RemoveChild(_ownedHud);
            _ownedHud.QueueFree();
        }
        _ownedHud = null;
        _hoverTooltip = null;
    }

    private void BuildWorldPresentation(in RemotePlayableClientSnapshot playable)
    {
        _player = playable.Position;
        ClientPublicPresentationMetadataSnapshot metadata =
            _session!.PublicPresentationState.GetSnapshot();
        if (metadata.ServerGenerationId != playable.ServerGenerationId)
            _session.PublicPresentationState.ResetServerGeneration(playable.ServerGenerationId);
        _actorState = new ClientActorPresentationState(
            ClientActorDefinitionCatalog.ConceptDemo, 64);
        _actorPresenter = new GodotActorPresenter(
            this, ClientActorDefinitionCatalog.ConceptDemo, 64);
        _actorPresenter.SetControlledActorPose(
            playable.PublicActor, playable.Position, playable.Orientation, Time.GetTicksMsec());
        _renderer = new GodotWorldRenderer(
            this,
            _session.WorldCache,
            GodotWorldRendererLimits.ForStructuralWindow(RenderRadiusChunks, 5, 32),
            assets: null,
            GodotWorldFallbackMaterialMode.Neutral,
            _actorPresenter);
        _window = new GodotSlidingRenderWindow(
            _session.WorldCache, _renderer, RenderRadiusChunks);
        if (!_window.TryTrackPlayer(
                _player, _presentationChanges, out _, out _,
                out ClientPresentationWindowError windowError,
                out GodotWorldRenderQueueError rendererError))
            throw new InvalidOperationException(
                $"The initial remote render window was rejected: {windowError}/{rendererError}.");
        DrainRenderer();
        SynchronizeActors(in playable);
        _terrainTriangles = CountTerrainTriangles();
        _smokeStart = _player;
        _lastOwnerState = playable.StateSequence;

        _camera = new Camera3D
        {
            Name = "RemoteCamera",
            Current = true,
            Near = 0.1f,
            Far = 500f,
            Fov = _settings?.CameraFovDegrees ?? 70f,
        };
        AddChild(_camera);
        ApplyCameraProfile();
        AddChild(new DirectionalLight3D
        {
            Name = "RemoteSun",
            RotationDegrees = new Vector3(-55, -35, 0),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        });
        var environment = new WorldEnvironment { Name = "RemoteEnvironment" };
        environment.Environment = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.055f, 0.075f, 0.06f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.65f, 0.72f, 0.68f),
            AmbientLightEnergy = 0.65f,
        };
        AddChild(environment);
    }

    private void SynchronizePresentation(in RemotePlayableClientSnapshot playable)
    {
        _player = playable.Position;
        if (!_window!.TryTrackPlayer(
                _player, _presentationChanges, out _, out _,
                out ClientPresentationWindowError windowError,
                out GodotWorldRenderQueueError rendererError) &&
            windowError != ClientPresentationWindowError.None)
            throw new InvalidOperationException(
                $"Remote render tracking failed: {windowError}/{rendererError}.");
        if (_window.TryCopyBindings(_presentationBindings, out int count, out _))
        {
            for (int index = 0; index < count; index++)
                _ = _renderer!.TryQueuePublicRefresh(
                    _presentationBindings[index].SemanticHandle, out _);
        }
        _renderer!.ProcessFrame(GodotWorldFrameBudget.Default);
        SynchronizeActors(in playable);
        _lastOwnerState = playable.StateSequence;
        UpdateHud(
            $"Remote world active — {_renderer.GetSnapshot().ActiveRenderChunks} chunks — " +
            $"position {_player}");
    }

    private void SubmitPhysicalInput(bool allowed)
    {
        ClientWorldInputSample sample = GodotWorldInput.Capture(
            _settings!, allowed && GetWindow().HasFocus() && _chatInput?.HasFocus() != true);
        ClientHorizontalCameraBasis basis = CameraBasis();
        ClientControlProfileDecision decision = EvaluateProfile(in sample, in basis);
        if (decision.SelectionRequested)
            _selectedTarget = _pointerTarget;
        RemotePlayableCommandFlags flags = _inventoryRequested
            ? RemotePlayableCommandFlags.OpenInventoryRequested
            : RemotePlayableCommandFlags.None;
        long faceX = 0;
        long faceZ = 0;
        if (TryResolveFacing(in decision, out ClientControlWorldPoint facing,
                out bool requiresLocomotion))
        {
            flags |= RemotePlayableCommandFlags.FaceRequested;
            if (requiresLocomotion)
                flags |= RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion;
            faceX = facing.XMillimeters;
            faceZ = facing.ZMillimeters;
        }
        if (_session!.Playable.TrySubmitCommand(
                decision.Command,
                target: _selectedTarget,
                flags: flags,
                faceXMillimeters: faceX,
                faceZMillimeters: faceZ))
            _inventoryRequested = false;
        _primaryPressed = false;
        _primaryReleased = false;
        _secondaryPressed = false;
    }

    private ClientControlProfileDecision EvaluateProfile(
        in ClientWorldInputSample physical,
        in ClientHorizontalCameraBasis basis)
    {
        return _settings!.ControlGrammar switch
        {
            ClientControlGrammar.OrbitCommand => _orbit.Evaluate(new ClientOrbitCommandSample(
                physical.HeldActions & ~GamePlayerActions.Jump,
                physical.HasFocus,
                _player.GlobalXMillimeters,
                _player.GlobalZMillimeters,
                _primaryDown,
                _primaryPressed,
                _primaryReleased,
                _secondaryPressed,
                CancelPressed: false,
                _pointerPointValid,
                _pointerPoint,
                checked((long)Time.GetTicksMsec()))),
            ClientControlGrammar.ThirdPersonDirect => new ClientControlProfileDecision(
                new ClientWorldCommandIntent(
                    _direct.Evaluate(physical.MoveX, physical.MoveZ, basis),
                    physical.HeldActions,
                    physical.HasFocus),
                false, default, false, default),
            ClientControlGrammar.IndependentMoveLook => _independent.Evaluate(
                physical.MoveX, physical.MoveZ, physical.HeldActions, physical.HasFocus,
                _pointerPointValid, _pointerPoint),
            _ => throw new InvalidOperationException(
                "The resolved remote control grammar is unsupported."),
        };
    }

    private bool TryResolveFacing(
        in ClientControlProfileDecision decision,
        out ClientControlWorldPoint point,
        out bool requiresLocomotion)
    {
        switch (_settings!.ControlGrammar)
        {
            case ClientControlGrammar.OrbitCommand when decision.LookRequested &&
                decision.Command.Locomotion.Kind is GameLocomotionUpdateKind.Direct or
                    GameLocomotionUpdateKind.Destination:
                point = decision.LookPoint;
                requiresLocomotion = true;
                return true;
            case ClientControlGrammar.ThirdPersonDirect when
                decision.Command.Locomotion.Kind == GameLocomotionUpdateKind.Direct:
                point = new ClientControlWorldPoint(
                    checked(_player.GlobalXMillimeters + decision.Command.Locomotion.DirectX),
                    checked(_player.GlobalZMillimeters + decision.Command.Locomotion.DirectZ));
                requiresLocomotion = true;
                return true;
            case ClientControlGrammar.IndependentMoveLook when decision.LookRequested:
                point = decision.LookPoint;
                requiresLocomotion = false;
                return true;
            default:
                point = default;
                requiresLocomotion = false;
                return false;
        }
    }

    private void ProgressSmoke(in RemotePlayableSessionSnapshot snapshot)
    {
        switch (_smokePhase)
        {
            case 0 when _renderer!.GetSnapshot().ActiveRenderChunks == 9 &&
                _terrainTriangles > 0 && snapshot.World.AppliedSnapshots != 0 &&
                _actorPresenter!.GetSnapshot().ActiveActors >= 2 &&
                snapshot.PublicPresentation.Metadata.CurrentRecords != 0:
                _actorPresenter.SetRevealHeld(true);
                _smokeStart = snapshot.Playable.Position;
                if (!_session!.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                        GameLocomotionUpdate.CreateDirect(0, -GamePlayerCommand.MaximumAxisMagnitude),
                        GamePlayerActions.None, true)))
                    return;
                _smokePhase = 1;
                break;
            case 1 when snapshot.Playable.Position != _smokeStart &&
                snapshot.Playable.LastProcessedCommandSequence != 0:
                if (!_session!.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop, GamePlayerActions.None, true)))
                    return;
                _smokePhase = 2;
                break;
            case 2 when !snapshot.Playable.CommandRetainedLocally:
                if (!_session!.Playable.TrySubmitCommand(
                        new ClientWorldCommandIntent(
                            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
                        flags: RemotePlayableCommandFlags.OpenInventoryRequested))
                    return;
                _smokePhase = 3;
                break;
            case 3 when _session!.UiClient.OpenViewCount == 1:
                if (!_session.Chat.TrySubmit("A0.8 native remote speech")) return;
                _smokePhase = 4;
                break;
            case 4 when _session!.ChatState.GetSnapshot().Applied > _lastChatApplied:
                _smokePhase = 5;
                break;
            case 5 when _renderer!.GetSnapshot().PendingBuilds == 0 &&
                _actorPresenter!.GetSnapshot() is
                { VisibleNameplates: >= 2, VisibleOverheadLabels: >= 1 }:
                _smokePhase = 6;
                UpdateHud("A0.8 native smoke complete");
                break;
        }
    }

    private void ReportSmokeProgress(in RemotePlayableSessionSnapshot snapshot)
    {
        if (!_smoke) return;
        ulong now = Time.GetTicksMsec();
        if (_lastSmokeDiagnosticMilliseconds != 0 &&
            now - _lastSmokeDiagnosticMilliseconds < 5_000)
            return;
        _lastSmokeDiagnosticMilliseconds = now;
        GodotActorPresenterSnapshot actors = _actorPresenter?.GetSnapshot() ?? default;
        GD.Print(
            $"A0.8 remote progress state={_state} phase={_smokePhase} " +
            $"content={snapshot.Content.State}/{snapshot.Content.ProofsQueued} " +
            $"world={snapshot.StructuralWorld.State}/{snapshot.World.AppliedSnapshots} " +
            $"playable={snapshot.Playable.State}/{snapshot.Playable.StateSequence} " +
            $"semantic={snapshot.SemanticConsumersActive} " +
            $"metadata={snapshot.PublicPresentation.Metadata.CurrentRecords}/" +
            $"{snapshot.PublicPresentation.Applied}/{snapshot.PublicPresentation.ProofsQueued} " +
            $"actors={actors.ActiveActors} names={actors.VisibleNameplates} " +
            $"overhead={actors.VisibleOverheadLabels}");
    }

    private void BuildBootstrapHud()
    {
        var panel = new PanelContainer
        {
            OffsetLeft = 16,
            OffsetTop = 16,
            OffsetRight = 680,
            OffsetBottom = 190,
        };
        _ownedHud!.AddChild(panel);
        var stack = new VBoxContainer();
        panel.AddChild(stack);
        stack.AddChild(new Label
        {
            Text = "UltraSim — Contentless Online",
            ThemeTypeVariation = "HeaderMedium",
        });
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        stack.AddChild(_status);
        stack.AddChild(new Label
        {
            Text = "Remote content supplies World/UI. Unresolved models and materials use the " +
                "always-available primitive substrate. Movement and camera follow the accepted " +
                "server profile; I opens Inventory.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _chatFeed = new RichTextLabel
        {
            OffsetLeft = 16,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -180,
            OffsetRight = 680,
            OffsetBottom = -52,
            BbcodeEnabled = false,
        };
        _ownedHud.AddChild(_chatFeed);
        _chatInput = new LineEdit
        {
            OffsetLeft = 16,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -46,
            OffsetRight = 680,
            OffsetBottom = -16,
            PlaceholderText = "Say…",
            MaxLength = 256,
        };
        _chatInput.TextSubmitted += SubmitChat;
        _ownedHud.AddChild(_chatInput);
        _hoverTooltip = new GodotWorldHoverTooltip(_ownedHud);
        UpdateHud("Authenticated session established; acquiring required content…");
    }

    private void SubmitChat(string text)
    {
        if (_chatInput is null || _state != RemoteConceptDemoClientState.Active ||
            _session?.Chat.TrySubmit(text) != true)
            return;
        _chatInput.Clear();
        _chatInput.ReleaseFocus();
    }

    private bool TryHandleFocusedChatInput(InputEvent inputEvent)
    {
        if (_chatInput?.HasFocus() != true) return false;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (IsChatSubmitKey(key))
            {
                SubmitChat(_chatInput.Text);
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == global::Godot.Key.Escape ||
                     key.PhysicalKeycode == global::Godot.Key.Escape)
            {
                _chatInput.ReleaseFocus();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (inputEvent is InputEventMouseButton mouse && mouse.Pressed &&
            !_chatInput.GetGlobalRect().HasPoint(mouse.Position))
        {
            _chatInput.ReleaseFocus();
        }
        return true;
    }

    private void UpdateChat()
    {
        ClientChatStateSnapshot snapshot = _session!.ChatState.GetSnapshot();
        if (snapshot.Applied == _lastChatApplied || _chatFeed is null)
        {
            _actorPresenter?.UpdateOriginAndLifetimes(_player, Time.GetTicksMsec());
            return;
        }
        int count = _session.ChatState.CopyRecent(_chatRows);
        _chatFeed.Text = string.Join('\n', _chatRows.AsSpan(0, count).ToArray()
            .Select(static row => $"{row.DisplayLabel}: {row.Text}"));
        if (count != 0)
        {
            ClientChatOccurrence newest = _chatRows[count - 1];
            _ = _actorPresenter?.TryPresentOverhead(
                in newest,
                _session.Playable.GetSnapshot().ServerGenerationId,
                checked(Time.GetTicksMsec() + 4_000));
        }
        _lastChatApplied = snapshot.Applied;
        _actorPresenter?.UpdateOriginAndLifetimes(_player, Time.GetTicksMsec());
    }

    private void SynchronizeActors(in RemotePlayableClientSnapshot playable)
    {
        if (_window is null || _actorState is null || _actorPresenter is null) return;
        if (!_window.TryCopyBindings(_presentationBindings, out int count, out _))
            throw new InvalidOperationException("Remote actor bindings exceeded capacity.");
        for (int index = 0; index < count; index++)
            _actorPresentedChunks[index] = _presentationBindings[index].SemanticHandle;
        ClientActorPresentationSyncStatus status = _actorState.TrySynchronize(
            _session!.WorldCache,
            _actorPresentedChunks.AsSpan(0, count),
            _session.PublicPresentationState,
            playable.ServerGenerationId,
            _actorChanges,
            out int changed,
            out _);
        if (status is not (ClientActorPresentationSyncStatus.Applied or
                ClientActorPresentationSyncStatus.NoChanges))
            throw new InvalidOperationException(
                $"Remote actor presentation synchronization failed: {status}.");
        _actorPresenter.SetControlledActorPose(
            playable.PublicActor, playable.Position, playable.Orientation, Time.GetTicksMsec());
        if (_actorState.TryGet(
                playable.PublicActor, out ClientActorPresentationRecord controlled))
            _cameraPivotMeters =
                controlled.Definition.CameraPivotHeightMillimeters * 0.001f;
        _actorPresenter.Apply(
            _actorChanges.AsSpan(0, changed), _player, Time.GetTicksMsec());
        if (playable.LastProcessedCommandSequence != 0 &&
            playable.LastProcessedCommandSequence != _lastPrimaryCommandSequence)
        {
            _lastPrimaryCommandSequence = playable.LastProcessedCommandSequence;
            if (playable.Primary == RemotePlayableActionStatus.Accepted)
                _ = _actorPresenter.TryPresentPrimaryAction(
                    playable.PublicActor,
                    playable.LastProcessedCommandSequence,
                    Time.GetTicksMsec());
        }
    }

    private void DrainRenderer()
    {
        for (int frame = 0; frame < 64; frame++)
        {
            GodotWorldRendererSnapshot snapshot = _renderer!.GetSnapshot();
            if (snapshot.PendingBuilds == 0 && snapshot.PendingRetirements == 0) return;
            _renderer.ProcessFrame(new GodotWorldFrameBudget(16, 16));
        }
        throw new InvalidOperationException("The initial remote render window did not converge.");
    }

    private int CountTerrainTriangles()
    {
        int triangles = 0;
        if (!_window!.TryCopyBindings(_presentationBindings, out int count, out _)) return 0;
        for (int index = 0; index < count; index++)
        {
            if (_renderer!.TryGetChunkDiagnostics(
                    _presentationBindings[index].SemanticHandle,
                    out GodotWorldRenderChunkDiagnostics diagnostics))
                triangles = checked(triangles + diagnostics.TerrainTriangleCount +
                    diagnostics.AdditionalTerrainTriangleCount);
        }
        return triangles;
    }

    private ClientHorizontalCameraBasis CameraBasis()
    {
        if (_camera is null) return ClientHorizontalCameraBasis.WorldAxes;
        Vector3 right = _camera.GlobalTransform.Basis.X;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        right.Y = 0;
        forward.Y = 0;
        right = right.Normalized();
        forward = forward.Normalized();
        return new ClientHorizontalCameraBasis(right.X, right.Z, forward.X, forward.Z);
    }

    private void UpdatePointerWorldPoint()
    {
        if (_camera is null || _session is null) return;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 origin = _camera.ProjectRayOrigin(mouse);
        Vector3 direction = _camera.ProjectRayNormal(mouse);
        if (TryPickPresentedPublicObject(origin, direction, out ClientControlWorldPoint objectPoint,
                out WorldPublicEntityId target))
        {
            _pointerPoint = objectPoint;
            _pointerPointValid = true;
            _pointerTarget = target;
            return;
        }
        _pointerTarget = default;
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
        long x = checked(_player.GlobalXMillimeters +
            (long)Math.Round(hit.X * 1_000d, MidpointRounding.AwayFromZero));
        long z = checked(_player.GlobalZMillimeters +
            (long)Math.Round(hit.Z * 1_000d, MidpointRounding.AwayFromZero));
        _pointerPoint = new ClientControlWorldPoint(x, z);
        _pointerPointValid = WorldPosition.TryFromGlobalMillimeters(
                x, _player.YMillimeters, z, out WorldPosition candidate) &&
            _session.WorldCache.PreparedWorld.TryFindNearestSupport(
                candidate, _player.YMillimeters, out WorldTerrainColumnSurface support) ==
                WorldTerrainColumnQueryStatus.Complete && support.IsValid && support.IsSupport;
    }

    private void UpdateWorldHover()
    {
        if (_hoverTooltip is null || _session is null || !_pointerTarget.IsValid ||
            !_session.PublicPresentationState.TryGet(
                _pointerTarget, out ClientPublicPresentationMetadata item) ||
            (item.Flags & ClientPublicPresentationFlags.ItemProperties) == 0)
        {
            _hoverTooltip?.Hide();
            return;
        }
        _hoverTooltip.Show(
            item.DisplayLabel,
            item.DetailText,
            GetViewport().GetMousePosition(),
            GetViewport().GetVisibleRect().Size);
    }

    private bool TryPickPresentedPublicObject(
        in Vector3 rayOrigin,
        in Vector3 rayDirection,
        out ClientControlWorldPoint point,
        out WorldPublicEntityId target)
    {
        point = default;
        target = default;
        if (_session is null || _window is null ||
            !_window.TryCopyBindings(_presentationBindings, out int count, out _))
            return false;
        float nearest = float.PositiveInfinity;
        WorldPosition selected = default;
        for (int index = 0; index < count; index++)
        {
            if (!_session.WorldCache.TryGetResidentView(
                    _presentationBindings[index].SemanticHandle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
            {
                if (!GodotPublicPointerPicker.IsPointerTarget(
                        record.PrefabId, record.StateFlags))
                    continue;
                Vector3 basePosition = new(
                    checked(record.Position.GlobalXMillimeters - _player.GlobalXMillimeters) * 0.001f,
                    checked(record.Position.YMillimeters - _player.YMillimeters) * 0.001f,
                    checked(record.Position.GlobalZMillimeters - _player.GlobalZMillimeters) * 0.001f);
                if (!GodotPublicPointerPicker.TryIntersect(
                        record.PrefabId, rayOrigin, rayDirection, basePosition, out float distance) ||
                    distance >= nearest)
                    continue;
                nearest = distance;
                selected = record.Position;
                target = record.Id;
            }
        }
        if (!float.IsFinite(nearest))
        {
            target = default;
            return false;
        }
        point = new ClientControlWorldPoint(
            selected.GlobalXMillimeters,
            selected.GlobalZMillimeters);
        return true;
    }

    private void ApplyCameraProfile()
    {
        if (_settings is null) return;
        switch (_settings.CameraProfile)
        {
            case ClientCameraProfile.Orbit:
                _cameraYaw = -0.7f;
                _cameraPitch = MathF.PI * 0.25f;
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
                throw new InvalidOperationException(
                    "The resolved remote camera profile is unsupported.");
        }
        _cameraDistance = _settings.CameraDistance;
        if (_camera is not null) _camera.Fov = _settings.CameraFovDegrees;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (_camera is null) return;
        float horizontal = MathF.Cos(_cameraPitch) * _cameraDistance;
        Vector3 pivot = new(0, _cameraPivotMeters, 0);
        _camera.Position = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance + _cameraPivotMeters,
            MathF.Cos(_cameraYaw) * horizontal);
        _camera.LookAt(pivot, Vector3.Up);
    }

    private void Fault(string detail)
    {
        _fault = detail;
        _state = RemoteConceptDemoClientState.Faulted;
        UpdateHud("Remote session failed — see Godot output");
        GD.PushError($"Remote Concept Demo failed: {detail}");
    }

    private void UpdateHud(string text)
    {
        if (_status is not null) _status.Text = text;
    }

    private bool TryRequestCloseOpenInventory()
    {
        if (_uiHost is null) return false;
        foreach (Node child in _uiHost.GetChildren())
        {
            if (child is not Control candidate ||
                candidate.FindChild("Element_items", recursive: true, owned: false) is null)
                continue;
            Button? close = candidate.FindChild(
                "Element_close", recursive: true, owned: false) as Button;
            if (close is null || close.Disabled)
                return false;
            close.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }
        return false;
    }

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_configured) throw new InvalidOperationException("Remote client is not configured.");
    }

    private static bool IsControlKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta ||
        input.PhysicalKeycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta;

    private static bool IsChatSubmitKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Enter or global::Godot.Key.KpEnter ||
        input.PhysicalKeycode is global::Godot.Key.Enter or global::Godot.Key.KpEnter;

    private static Control FullRectControl(string name)
    {
        var result = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        result.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return result;
    }
}
#endif
