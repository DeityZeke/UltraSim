#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using Godot;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
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
    private readonly ThirdPersonDirectProfile _direct = new();
    private readonly ClientPresentationChange[] _presentationChanges = new ClientPresentationChange[18];
    private readonly ClientPresentationBinding[] _presentationBindings = new ClientPresentationBinding[9];
    private ContentCache? _cache;
    private HttpContentPackageSource? _source;
    private RemotePlayableSession? _session;
    private GodotWorldRenderer? _renderer;
    private GodotSlidingRenderWindow? _window;
    private ResolvedClientSettings? _settings;
    private Camera3D? _camera;
    private Control? _ownedHud;
    private Control? _uiHost;
    private Label? _status;
    private RichTextLabel? _chatFeed;
    private LineEdit? _chatInput;
    private WorldPosition _player;
    private WorldPosition _smokeStart;
    private ulong _lastOwnerState;
    private ulong _lastChatApplied;
    private bool _inventoryRequested;
    private bool _smoke;
    private int _smokePhase;
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
                new ClientChatLimits(64, 256, 128));
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
        if (_state == RemoteConceptDemoClientState.Faulted) return;
        try
        {
            _ = _session!.ProcessReceived();
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
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

            SynchronizePresentation(snapshot.Playable);
            if (!_smoke)
                SubmitPhysicalInput(gameplayInputAllowed);
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
            true,
            _smokePhase == 6,
            _fault);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_state != RemoteConceptDemoClientState.Active || _settings is null) return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo &&
            GodotWorldInput.Matches(key, _settings, ClientSemanticAction.OpenInventory))
        {
            _inventoryRequested = true;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() => DisposeComposition();

    public void DisposeComposition()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer?.Dispose();
        _session?.Dispose();
        _source?.Dispose();
        _cache?.Dispose();
        _renderer = null;
        _session = null;
        _source = null;
        _cache = null;
        if (_ownedHud is not null && GodotObject.IsInstanceValid(_ownedHud))
        {
            _ownedHud.GetParent()?.RemoveChild(_ownedHud);
            _ownedHud.QueueFree();
        }
        _ownedHud = null;
    }

    private void BuildWorldPresentation(in RemotePlayableClientSnapshot playable)
    {
        _player = playable.Position;
        _renderer = new GodotWorldRenderer(
            this,
            _session!.WorldCache,
            GodotWorldRendererLimits.ForStructuralWindow(RenderRadiusChunks, 5, 32),
            assets: null,
            GodotWorldFallbackMaterialMode.Neutral);
        _window = new GodotSlidingRenderWindow(
            _session.WorldCache, _renderer, RenderRadiusChunks);
        if (!_window.TryTrackPlayer(
                _player, _presentationChanges, out _, out _,
                out ClientPresentationWindowError windowError,
                out GodotWorldRenderQueueError rendererError))
            throw new InvalidOperationException(
                $"The initial remote render window was rejected: {windowError}/{rendererError}.");
        DrainRenderer();
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
            Position = new Vector3(-7.5f, 7.5f, 7.5f),
        };
        AddChild(_camera);
        _camera.LookAt(new Vector3(0, CameraPivotMeters, 0), Vector3.Up);
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
        GameLocomotionUpdate locomotion = _direct.Evaluate(sample.MoveX, sample.MoveZ, basis);
        RemotePlayableCommandFlags flags = _inventoryRequested
            ? RemotePlayableCommandFlags.OpenInventoryRequested
            : RemotePlayableCommandFlags.None;
        if (_session!.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(locomotion, sample.HeldActions, sample.HasFocus),
                flags: flags))
            _inventoryRequested = false;
    }

    private void ProgressSmoke(in RemotePlayableSessionSnapshot snapshot)
    {
        switch (_smokePhase)
        {
            case 0 when _renderer!.GetSnapshot().ActiveRenderChunks == 9 &&
                _terrainTriangles > 0 && snapshot.World.AppliedSnapshots != 0:
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
            case 5 when _renderer!.GetSnapshot().PendingBuilds == 0:
                _smokePhase = 6;
                UpdateHud("A0.8 native smoke complete");
                break;
        }
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
                "always-available primitive substrate. WASD moves, Space jumps, I opens Inventory.",
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
        _chatInput.TextSubmitted += text =>
        {
            if (_state == RemoteConceptDemoClientState.Active &&
                _session?.Chat.TrySubmit(text) == true)
                _chatInput.Text = string.Empty;
        };
        _ownedHud.AddChild(_chatInput);
        UpdateHud("Authenticated session established; acquiring required content…");
    }

    private void UpdateChat()
    {
        ClientChatStateSnapshot snapshot = _session!.ChatState.GetSnapshot();
        if (snapshot.Applied == _lastChatApplied || _chatFeed is null) return;
        var rows = new ClientChatOccurrence[64];
        int count = _session.ChatState.CopyRecent(rows);
        _chatFeed.Text = string.Join('\n', rows.AsSpan(0, count).ToArray()
            .Select(static row => $"{row.DisplayLabel}: {row.Text}"));
        _lastChatApplied = snapshot.Applied;
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

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_configured) throw new InvalidOperationException("Remote client is not configured.");
    }

    private static Control FullRectControl(string name)
    {
        var result = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        result.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return result;
    }
}
#endif
