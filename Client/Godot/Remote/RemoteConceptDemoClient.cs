#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using System.Diagnostics;
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
    GodotContentActorVisualResolverSnapshot ActorResources,
    bool UsedPrimitiveSubstrate,
    bool SmokeComplete,
    string? Fault,
    ClientWorldInputScheduleSnapshot InputSchedule,
    GodotNativeTelemetrySnapshot NativeTelemetry,
    ContentCacheSnapshot ContentCacheAtOpen,
    ContentCacheSnapshot ContentCacheCurrent,
    RemoteConceptDemoUiOwnershipSnapshot UiOwnership);

/// <summary>
/// Cold, engine-thread-only observation of the pre-F1 remote HUD ownership tree. This preserves
/// the current single-HUD mismatch as evidence; it is not the future F1.7 window contract.
/// </summary>
public readonly record struct RemoteConceptDemoUiOwnershipSnapshot(
    int HudHostChildren,
    int ServerUiHostChildren,
    int OpenSemanticViews,
    bool ChatFocused,
    bool GameplayInputAllowed);

public readonly record struct RemoteConceptDemoLifecycleResidueSnapshot(
    ulong ApplicationSessionEpoch,
    ulong ServerGenerationId,
    int OpenUiViews,
    int ActiveActors,
    int VisibleNameplates,
    int VisibleOverheadLabels,
    int RenderedChunks,
    int RetainedActorResources,
    bool ChatFocused,
    long RetainedActorResourceBytes,
    int RendererNodes,
    int RendererResources,
    int PrimitiveFallbackActors,
    int AnimatedActors,
    int EquippedPresentations,
    ContentCacheSnapshot ContentCache,
    RemoteConceptDemoUiOwnershipSnapshot Ui);

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
    private readonly GodotNativeTelemetry _nativeTelemetry = new();
    private ContentCache? _cache;
    private ContentCacheSnapshot _contentCacheAtOpen;
    private ContentCacheSnapshot _contentCacheObserved;
    private HttpContentPackageSource? _source;
    private RemotePlayableSession? _session;
    private NetworkProtocolPeer? _networkPeer;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ClientActorPresentationState? _actorState;
    private GodotActorPresenter? _actorPresenter;
    private GodotContentActorVisualResolver? _actorVisuals;
    private ResolvedClientSettings? _settings;
    private ClientWorldInputScheduler? _inputScheduler;
    private Camera3D? _camera;
    private Control? _ownedHud;
    private Control? _uiHost;
    private GodotUiRenderer? _uiRenderer;
    private Label? _status;
    private Label? _ping;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private OptionButton? _chatMode;
    private GodotSystemChatBanner? _systemChatBanner;
    private GodotWorldHoverTooltip? _hoverTooltip;
    private WorldPosition _player;
    private WorldPosition _smokeStart;
    private ulong _lastOwnerState;
    private ulong _lastPrimaryCommandSequence;
    private ulong _lastChatApplied;
    private RemotePlayableOneShotRequests _oneShotRequests;
    private bool _primaryDown;
    private bool _primaryPressed;
    private bool _primaryReleased;
    private bool _secondaryPressed;
    private bool _pointerPointValid;
    private int _telemetryProbePhase;
    private ClientControlWorldPoint _pointerPoint;
    private WorldPublicEntityId _pointerTarget;
    private WorldPublicEntityId _selectedTarget;
    private float _cameraYaw = -0.7f;
    private float _cameraPitch = 0.34f;
    private float _cameraDistance = 8.5f;
    private float _cameraPivotMeters = CameraPivotMeters;
    private bool _smoke;
    private bool _gameplayInputAllowed;
    private bool _gameplayInputAdmittedLastFrame;
    private int _smokePhase;
    private ulong _lastSmokeDiagnosticMilliseconds;
    private int _terrainTriangles;
    private bool _configured;
    private bool _disposed;
    private bool _pf3SustainedInput;
    private bool _telemetryWindowActive;
    private long _pf3SustainedInputStarted;
    private RemoteConceptDemoClientState _state = RemoteConceptDemoClientState.AwaitingContent;
    private NetworkTelemetryLevel _telemetryLevel;
    private string? _fault;

    public void Configure(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        UltraSim.Game.Application.ApplicationRealmId realm,
        string cacheRoot,
        CanvasLayer hudLayer,
        ResolvedClientSettings settings,
        bool allowInsecurePrivateContent,
        bool smoke,
        NetworkTelemetryLevel telemetryLevel = NetworkTelemetryLevel.Off,
        bool pf3SustainedInput = false)
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
        _contentCacheAtOpen = _cache!.GetSnapshot();
        _contentCacheObserved = _contentCacheAtOpen;
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
            _uiRenderer = new GodotUiRenderer(_uiHost);
            _session = new RemotePlayableSession(
                peer,
                applicationSessionEpoch,
                new RemoteRequiredContentBinding(
                    realm, _cache!, ContentBaseCatalog.Empty, _source),
                RemoteConceptDemoWorldProfile.CreateRequiredBinding(),
                _uiRenderer,
                new ClientChatLimits(64, 256, 128),
                standardUiDefinitionResolver: ClientStandardUiLibrary.Resolve);
            _session.ConfigureTelemetry(telemetryLevel);
            _nativeTelemetry.Configure(telemetryLevel);
            _telemetryLevel = telemetryLevel;
            _networkPeer = peer;
            _settings = settings;
            _smoke = smoke;
            _pf3SustainedInput = pf3SustainedInput;
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
        long frameStarted = _nativeTelemetry.BeginFrame();
        try { PumpCore(gameplayInputAllowed); }
        finally { _nativeTelemetry.EndFrame(frameStarted); }
    }

    private void PumpCore(bool gameplayInputAllowed)
    {
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
                _contentCacheObserved = _cache!.GetSnapshot();
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
            _inputScheduler!.ObserveCompleted(snapshot.Playable.LastProcessedCommandSequence);
            _ = _inputScheduler.TryPump(Stopwatch.GetTimestamp(), TrySubmitScheduledCommand);

            // The headless exported-product gate has no physical device events while held at
            // its synchronization barrier. Drive one ordinary bounded movement/stop pair after
            // a diagnostic reset so the measured window proves the native input-to-semantic-
            // command and owner-state-completion path. This is acceptance-client behavior only;
            // it does not manufacture an authoritative result.
            if (_telemetryProbePhase != 0 && !snapshot.Playable.CommandRetainedLocally)
            {
                ClientWorldCommandIntent probe = _telemetryProbePhase == 1
                    ? new ClientWorldCommandIntent(
                        GameLocomotionUpdate.CreateDirect(
                            GamePlayerCommand.MaximumAxisMagnitude, 0),
                        GamePlayerActions.None, true)
                    : new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop, GamePlayerActions.None, true);
                if (TrySubmitObservedCommand(in probe))
                    _telemetryProbePhase = _telemetryProbePhase == 1 ? 2 : 0;
            }
            if (_pf3SustainedInput && _telemetryWindowActive)
                SubmitPf3SustainedInput();

            bool gameplayInputAdmitted = ObserveGameplayInputAdmission(gameplayInputAllowed);

            SynchronizePresentation(snapshot.Playable);
            if (!_smoke)
            {
                UpdatePointerWorldPoint();
                UpdateWorldHover();
                SubmitPhysicalInput(gameplayInputAdmitted);
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
        GodotActorPresenterSnapshot actors = _actorPresenter?.GetSnapshot() ?? default;
        return new RemoteConceptDemoClientSnapshot(
            _state, _session!.GetSnapshot(), renderer, _player,
            renderer.ActiveRenderChunks, _terrainTriangles,
            _session.UiClient.OpenViewCount,
            actors,
            _actorVisuals?.GetSnapshot() ?? default,
            true,
            _smokePhase == 8,
            _fault,
            _inputScheduler?.GetSnapshot() ?? default,
            _nativeTelemetry.Capture(in actors, in renderer),
            _contentCacheAtOpen,
            _contentCacheObserved,
            GetUiOwnershipSnapshot());
    }

    /// <summary>
    /// Explicit cold-path F1.0 census. Unlike <see cref="GetSnapshot"/>, this may scan the
    /// persistent cache store and therefore must never be called from the ordinary frame pump.
    /// </summary>
    public RemoteConceptDemoClientSnapshot CaptureF10BaselineSnapshot()
    {
        EnsureOperational();
        _contentCacheObserved = _cache?.GetSnapshot() ?? default;
        return GetSnapshot();
    }

    private RemoteConceptDemoUiOwnershipSnapshot GetUiOwnershipSnapshot() => new(
        _ownedHud?.GetChildCount() ?? 0,
        _uiHost?.GetChildCount() ?? 0,
        _session?.UiClient.OpenViewCount ?? 0,
        _chatInput?.HasFocus() == true,
        _gameplayInputAllowed);

    /// <summary>Starts a fresh native/client diagnostic window only.</summary>
    public void ResetTelemetryWindow()
    {
        EnsureOperational();
        _session!.ConfigureTelemetry(_telemetryLevel);
        _inputScheduler?.ResetTelemetryWindow();
        _nativeTelemetry.Configure(_telemetryLevel);
        _telemetryProbePhase = _telemetryLevel == NetworkTelemetryLevel.Off ? 0 : 1;
        _telemetryWindowActive = _telemetryLevel != NetworkTelemetryLevel.Off;
        _pf3SustainedInputStarted = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Stops the PF3 synthetic producer and publishes one ordinary semantic stop/release sample.
    /// The caller must continue pumping until <see cref="TelemetryInputIsQuiesced"/> becomes true
    /// before taking closure evidence.
    /// </summary>
    public void BeginTelemetryInputQuiescence()
    {
        EnsureOperational();
        if (!_telemetryWindowActive) return;
        _telemetryWindowActive = false;
        if (!_pf3SustainedInput || _inputScheduler is null) return;
        var stop = new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true);
        if (!TrySubmitObservedCommand(in stop))
            throw new InvalidOperationException(
                "The native PF3 input scheduler could not retain its terminal stop/release.");
    }

    public bool TelemetryInputIsQuiesced()
    {
        EnsureOperational();
        if (_inputScheduler is null) return true;
        ClientWorldInputScheduleSnapshot snapshot = _inputScheduler.GetSnapshot();
        return !snapshot.ContinuousStatePending && snapshot.PendingObligations == 0 &&
            snapshot.SubmittedCompletionDebt == 0 &&
            snapshot.EdgeObligationsGenerated == snapshot.EdgeObligationsCompleted &&
            snapshot.ReleaseOrStopGenerated == snapshot.ReleaseOrStopCompleted;
    }

    /// <summary>
    /// Populates and observes the session-owned surfaces which aggregate F0.7 retirement must
    /// remove. This changes only local presentation/focus state after the ordinary smoke journey.
    /// </summary>
    public RemoteConceptDemoLifecycleResidueSnapshot PrepareF07RetirementCanary()
    {
        EnsureOperational();
        if (_state != RemoteConceptDemoClientState.Active || _smokePhase != 8 ||
            _session is null || _actorPresenter is null || _actorVisuals is null ||
            _renderer is null || _chatInput is null)
            throw new InvalidOperationException(
                "The F0.7 remote retirement canary requires a completed active smoke journey.");
        SetWorldRevealHeld(true);
        _chatInput.Text = "F0.7 remote draft must retire";
        _chatInput.GrabFocus();

        RemotePlayableSessionSnapshot session = _session.GetSnapshot();
        GodotActorPresenterSnapshot actors = _actorPresenter.GetSnapshot();
        GodotWorldRendererSnapshot renderer = _renderer.GetSnapshot();
        GodotContentActorVisualResolverSnapshot actorResources = _actorVisuals.GetSnapshot();
        ContentCacheSnapshot contentCache = _cache?.GetSnapshot() ?? default;
        RemoteConceptDemoUiOwnershipSnapshot ui = GetUiOwnershipSnapshot();
        var result = new RemoteConceptDemoLifecycleResidueSnapshot(
            session.Playable.ApplicationSessionEpoch,
            session.Playable.ServerGenerationId,
            _session.UiClient.OpenViewCount,
            actors.ActiveActors,
            actors.VisibleNameplates,
            actors.VisibleOverheadLabels,
            renderer.ActiveRenderChunks,
            actorResources.RealizedResources,
            _chatInput.HasFocus(),
            actorResources.RetainedArtifactBytes,
            renderer.ActiveNodes,
            renderer.ActiveResources,
            actors.PrimitiveFallbackActors,
            actors.AnimatedActors,
            actors.EquippedMainHands,
            contentCache,
            ui);
        if (result.OpenUiViews != 1 || result.ActiveActors < 2 ||
            result.VisibleNameplates < 2 || result.VisibleOverheadLabels < 1 ||
            result.RenderedChunks <= 0 || result.RetainedActorResources <= 0 ||
            !result.ChatFocused)
            throw new InvalidOperationException(
                $"The F0.7 remote retirement canary did not populate every surface: {result}.");
        return result;
    }

    public override void _Input(InputEvent inputEvent)
    {
        _nativeTelemetry.RecordInputEvent();
        if (_state != RemoteConceptDemoClientState.Active || _settings is null) return;
        if (TryHandleFocusedChatInput(inputEvent)) return;
        if (!_gameplayInputAllowed) return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo &&
            GodotWorldInput.Matches(key, _settings, ClientSemanticAction.OpenInventory))
        {
            if (!TryRequestCloseOpenInventory() && _session!.UiClient.OpenViewCount == 0)
            {
                _oneShotRequests.RequestInventory();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey monitor && monitor.Pressed && !monitor.Echo &&
            GodotWorldInput.Matches(monitor, _settings, ClientSemanticAction.OpenServerMonitor))
        {
            if (!TryRequestCloseOpenServerMonitor() && _session!.UiClient.OpenViewCount == 0)
            {
                _oneShotRequests.RequestServerMonitor();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        GodotUiGameplayInputDisposition uiDisposition =
            _uiRenderer?.ResolveGameplayInput(inputEvent) ??
            GodotUiGameplayInputDisposition.Gameplay;
        if (uiDisposition != GodotUiGameplayInputDisposition.Gameplay)
        {
            if (uiDisposition is GodotUiGameplayInputDisposition.Blocked or
                GodotUiGameplayInputDisposition.OutsideCloseSubmitted)
                GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey chat && chat.Pressed && !chat.Echo &&
            GodotWorldInput.Matches(chat, _settings, ClientSemanticAction.Chat))
        {
            _chatInput?.GrabFocus();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is InputEventKey reveal && !reveal.Echo && IsControlKey(reveal))
        {
            SetWorldRevealHeld(reveal.Pressed);
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
        _nativeTelemetry.Dispose();
        _actorVisuals?.Dispose();
        _systemChatBanner?.Dispose();
        _renderer?.Dispose();
        _ = _session?.TryRequestDetach();
        _session?.Dispose();
        _source?.Dispose();
        _cache?.Dispose();
        _renderer = null;
        _actorPresenter = null;
        _actorVisuals = null;
        _systemChatBanner = null;
        _actorState = null;
        _session = null;
        _inputScheduler = null;
        _networkPeer = null;
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
        RemoteServerPerformanceProfile performance = _session!.Performance.Profile;
        _inputScheduler = new ClientWorldInputScheduler(new ClientInputSubmissionPolicy(
            performance.MaximumInputHz,
            performance.RecommendedInputHz,
            performance.InputBurstCapacity,
            performance.InputBurstWindowMilliseconds));
        _player = playable.Position;
        ClientPublicPresentationMetadataSnapshot metadata =
            _session!.PublicPresentationState.GetSnapshot();
        if (metadata.ServerGenerationId != playable.ServerGenerationId)
            _session.PublicPresentationState.ResetServerGeneration(playable.ServerGenerationId);
        _actorState = new ClientActorPresentationState(
            ClientActorDefinitionCatalog.ConceptDemo, 64);
        _actorVisuals = new GodotContentActorVisualResolver(
            _session.Content ?? throw new InvalidOperationException(
                "Remote actor presentation requires an applied content catalog."));
        _actorPresenter = new GodotActorPresenter(
            this, ClientActorDefinitionCatalog.ConceptDemo, 64, _actorVisuals);
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
        long presentationStarted = _nativeTelemetry.BeginPresentation();
        try { SynchronizePresentationCore(in playable); }
        finally { _nativeTelemetry.EndPresentation(presentationStarted); }
    }

    private void SynchronizePresentationCore(in RemotePlayableClientSnapshot playable)
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
        RemotePlayableCommandFlags flags = _oneShotRequests.Pending;
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
        bool retained = TrySubmitObservedCommand(
            decision.Command, _selectedTarget, flags, faceX, faceZ);
        _oneShotRequests.ObserveSubmission(retained);
        if (retained)
        {
            _primaryPressed = false;
            _primaryReleased = false;
            _secondaryPressed = false;
        }
    }

    private bool ObserveGameplayInputAdmission(bool shellAllowsGameplay)
    {
        bool admitted = shellAllowsGameplay && !(_uiRenderer?.BlocksGameplayInput ?? false);
        bool stopRetained = true;
        if (!admitted)
        {
            _primaryDown = false;
            _primaryPressed = false;
            _primaryReleased = false;
            _secondaryPressed = false;
            SetWorldRevealHeld(false);
            if (_gameplayInputAdmittedLastFrame && _inputScheduler is not null)
            {
                var stop = new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop,
                    GamePlayerActions.None,
                    HasFocus: false);
                stopRetained = TrySubmitObservedCommand(in stop);
            }
        }
        if (admitted || stopRetained)
            _gameplayInputAdmittedLastFrame = admitted;
        return admitted;
    }

    private bool TrySubmitObservedCommand(
        in ClientWorldCommandIntent command,
        WorldPublicEntityId target = default,
        RemotePlayableCommandFlags flags = RemotePlayableCommandFlags.None,
        long faceXMillimeters = 0,
        long faceZMillimeters = 0)
    {
        if (_inputScheduler is null) return false;
        var context = new ClientWorldCommandContext(
            target,
            (ClientWorldCommandRequestFlags)(byte)flags,
            faceXMillimeters,
            faceZMillimeters);
        long now = Stopwatch.GetTimestamp();
        bool retained = _inputScheduler.TryObserve(command, context, now);
        if (retained) _ = _inputScheduler.TryPump(now, TrySubmitScheduledCommand);
        _nativeTelemetry.RecordCommandSample(
            command.Locomotion.Kind != GameLocomotionUpdateKind.Unchanged,
            command.HeldActions != GamePlayerActions.None ||
                flags != RemotePlayableCommandFlags.None);
        return retained;
    }

    private bool TrySubmitScheduledCommand(
        in ClientWorldScheduledCommand scheduled,
        out ulong sequence)
    {
        bool submitted = _session!.Playable.TrySubmitCommand(
            scheduled.Intent,
            scheduled.Context.Target,
            (RemotePlayableCommandFlags)(byte)scheduled.Context.Flags,
            scheduled.Context.FaceXMillimeters,
            scheduled.Context.FaceZMillimeters,
            out sequence);
        if (submitted) _nativeTelemetry.RecordCommandSubmission();
        return submitted;
    }

    private void SubmitPf3SustainedInput()
    {
        long now = Stopwatch.GetTimestamp();
        long elapsedMilliseconds = checked((long)((Int128)Math.Max(
            0, now - _pf3SustainedInputStarted) * 1_000 / Stopwatch.Frequency));
        long phase = elapsedMilliseconds / 500;
        bool stop = phase % 8 == 7;
        bool jump = elapsedMilliseconds != 0 && elapsedMilliseconds % 2_000 < 100;
        short x = checked((short)(phase % 4 switch
        {
            0 => GamePlayerCommand.MaximumAxisMagnitude,
            1 => GamePlayerCommand.MaximumAxisMagnitude / 2,
            2 => -GamePlayerCommand.MaximumAxisMagnitude,
            _ => -GamePlayerCommand.MaximumAxisMagnitude / 2,
        }));
        short z = checked((short)(phase % 4 switch
        {
            0 => GamePlayerCommand.MaximumAxisMagnitude / 4,
            1 => GamePlayerCommand.MaximumAxisMagnitude,
            2 => -GamePlayerCommand.MaximumAxisMagnitude / 4,
            _ => -GamePlayerCommand.MaximumAxisMagnitude,
        }));
        var intent = new ClientWorldCommandIntent(
            stop ? GameLocomotionUpdate.Stop : GameLocomotionUpdate.CreateDirect(x, z),
            jump ? GamePlayerActions.Jump : GamePlayerActions.None,
            true);
        _ = TrySubmitObservedCommand(in intent);
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
        // The exported-product acceptance intentionally keeps Server Monitor open while it
        // verifies overhead speech and nameplates. That diagnostic reveal is not physical
        // gameplay input; keep it asserted after the modal-input gate clears held controls.
        if (_smokePhase >= 5)
            SetWorldRevealHeld(true);

        switch (_smokePhase)
        {
            case 0 when _renderer!.GetSnapshot().ActiveRenderChunks == 9 &&
                _terrainTriangles > 0 && snapshot.World.AppliedSnapshots != 0 &&
                _actorPresenter!.GetSnapshot().ActiveActors >= 2 &&
                snapshot.PublicPresentation.Metadata.CurrentRecords != 0:
                SetWorldRevealHeld(true);
                _smokeStart = snapshot.Playable.Position;
                if (!TrySubmitObservedCommand(new ClientWorldCommandIntent(
                        GameLocomotionUpdate.CreateDirect(0, -GamePlayerCommand.MaximumAxisMagnitude),
                        GamePlayerActions.None, true)))
                    return;
                _smokePhase = 1;
                break;
            case 1 when snapshot.Playable.Position != _smokeStart &&
                snapshot.Playable.LastProcessedCommandSequence != 0:
                if (!TrySubmitObservedCommand(new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop, GamePlayerActions.None, true)))
                    return;
                _smokePhase = 2;
                break;
            case 2 when !snapshot.Playable.CommandRetainedLocally:
                if (!TrySubmitObservedCommand(
                        new ClientWorldCommandIntent(
                            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
                        flags: RemotePlayableCommandFlags.OpenInventoryRequested))
                    return;
                _smokePhase = 3;
                break;
            case 3 when _session!.UiClient.OpenViewCount == 1:
                if (!TryRequestCloseOpenInventory()) return;
                _smokePhase = 4;
                break;
            case 4 when _session!.UiClient.OpenViewCount == 0 &&
                !snapshot.Playable.CommandRetainedLocally:
                if (!TrySubmitObservedCommand(
                        new ClientWorldCommandIntent(
                            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
                        flags: RemotePlayableCommandFlags.OpenServerMonitorRequested))
                    return;
                _smokePhase = 5;
                break;
            case 5 when _session!.UiClient.OpenViewCount == 1 && HasOpenServerMonitor():
                if (!_session.Chat.TrySubmit("A0.8 native remote speech")) return;
                _smokePhase = 6;
                break;
            case 6 when _session!.ChatState.GetSnapshot().Applied > _lastChatApplied:
                _smokePhase = 7;
                break;
            case 7 when _renderer!.GetSnapshot().PendingBuilds == 0 &&
                _actorPresenter!.GetSnapshot() is
                { VisibleNameplates: >= 2, VisibleOverheadLabels: >= 1 }:
                _smokePhase = 8;
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
        _ping = new Label { Text = "Ping: pending" };
        stack.AddChild(_ping);
        stack.AddChild(new Label
        {
            Text = "Remote content supplies World/UI. Unresolved models and materials use the " +
                "always-available primitive substrate. Movement and camera follow the accepted " +
            "server profile; I opens Inventory and F4 opens the read-only Server Monitor.",
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
        _chatMode = new OptionButton
        {
            OffsetLeft = 16,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -46,
            OffsetRight = 124,
            OffsetBottom = -16,
        };
        _chatMode.AddItem("Whisper", (int)ClientChatSpeechMode.Whisper);
        _chatMode.AddItem("Say", (int)ClientChatSpeechMode.Say);
        _chatMode.AddItem("Yell", (int)ClientChatSpeechMode.Yell);
        _chatMode.AddItem("Global", (int)ClientChatSpeechMode.Global);
        _chatMode.Select(1);
        _ownedHud.AddChild(_chatMode);
        _chatInput = new LineEdit
        {
            OffsetLeft = 128,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetTop = -46,
            OffsetRight = 680,
            OffsetBottom = -16,
            PlaceholderText = "Enter chat or /help /where",
            MaxLength = 256,
        };
        _chatInput.TextSubmitted += SubmitChat;
        _ownedHud.AddChild(_chatInput);
        _systemChatBanner = new GodotSystemChatBanner(_ownedHud);
        _hoverTooltip = new GodotWorldHoverTooltip(_ownedHud);
        UpdateHud("Authenticated session established; acquiring required content…");
    }

    private void SubmitChat(string text)
    {
        ClientChatSpeechMode mode = _chatMode is null
            ? ClientChatSpeechMode.Say
            : (ClientChatSpeechMode)_chatMode.GetSelectedId();
        if (_chatInput is null || _state != RemoteConceptDemoClientState.Active ||
            _session?.Chat.TrySubmit(text, mode) != true)
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
            _systemChatBanner?.Update(Time.GetTicksMsec());
            _actorPresenter?.UpdateOriginAndLifetimes(_player, Time.GetTicksMsec());
            return;
        }
        int count = _session.ChatState.CopyRecent(_chatRows);
        _chatFeed.Text = string.Join('\n', _chatRows.AsSpan(0, count).ToArray()
            .Select(static row => row.Kind switch
            {
                ClientChatOccurrenceKind.SpatialSpeech =>
                    $"[{row.SpeechMode}] {row.DisplayLabel}: {row.Text}",
                ClientChatOccurrenceKind.GlobalSpeech =>
                    $"[Global] {row.DisplayLabel}: {row.Text}",
                _ => $"[{row.DisplayLabel}] {row.Text}",
            }));
        if (count != 0)
        {
            ClientChatOccurrence newest = _chatRows[count - 1];
            _ = _actorPresenter?.TryPresentOverhead(
                in newest,
                _session.Playable.GetSnapshot().ServerGenerationId,
                checked(Time.GetTicksMsec() + 4_000));
        }
        _systemChatBanner?.Observe(_chatRows.AsSpan(0, count), Time.GetTicksMsec());
        _lastChatApplied = snapshot.Applied;
        _systemChatBanner?.Update(Time.GetTicksMsec());
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
            item.InspectionProperties,
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
        if (_ping is not null && _networkPeer is not null)
        {
            NetworkProtocolSnapshot network = _networkPeer.GetSnapshot();
            ulong milliseconds = Math.Min(network.SmoothedRoundTripSteps, 60_000UL);
            _ping.Text = !network.Established ||
                milliseconds == 0 && network.ReliableAcknowledgements == 0
                    ? "Ping: pending"
                    : milliseconds == 0 ? "Ping: <1 ms" : $"Ping: {milliseconds} ms";
        }
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

    private bool TryRequestCloseOpenServerMonitor()
    {
        Control? view = FindOpenServerMonitorView();
        Button? close = view?.FindChild(
            "Element_close", recursive: true, owned: false) as Button;
        if (close is null || close.Disabled) return false;
        close.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private bool HasOpenServerMonitor() => FindOpenServerMonitorView() is not null;

    private Control? FindOpenServerMonitorView()
    {
        if (_uiHost is null) return null;
        foreach (Node child in _uiHost.GetChildren())
            if (child is Control candidate &&
                candidate.FindChild("Element_participants", recursive: true, owned: false) is not null)
                return candidate;
        return null;
    }

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_configured) throw new InvalidOperationException("Remote client is not configured.");
    }

    private static bool IsControlKey(InputEventKey input) =>
        input.Keycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta ||
        input.PhysicalKeycode is global::Godot.Key.Ctrl or global::Godot.Key.Meta;

    private void SetWorldRevealHeld(bool held)
    {
        _actorPresenter?.SetRevealHeld(held);
        _renderer?.SetPublicNamesRevealHeld(held);
    }

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
