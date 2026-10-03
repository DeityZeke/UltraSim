using Godot;
using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.Client.Sound;

namespace UltraSim.Client.Godot;

public readonly record struct GodotClientRootSnapshot(
    string Edition,
    ClientShellSnapshot Shell,
    ClientSettingsLoadStatus SettingsLoadStatus,
    int CapabilityCount,
    bool ControllerRootPresent,
    bool ContentCatalogRootPresent,
    bool WorldRootPresent,
    bool WorldUiLayerPresent,
    bool HudLayerPresent,
    bool MenuLayerPresent,
    bool DebugLayerPresent,
    bool ContextLayerPresent,
    bool WorkspaceLayerPresent,
    GodotClientUiCompositionSnapshot UiComposition,
    GodotClientSessionBoundarySnapshot Boundary);

/// <summary>
/// Exact persistent-root shape captured at a session boundary. Capability implementations use
/// this to prove that their transient presentation and active server profile did not leak across
/// a return to the main menu.
/// </summary>
public readonly record struct GodotClientSessionBoundarySnapshot(
    int ControllerChildren,
    int ContentCatalogChildren,
    int WorldChildren,
    int WorldUiChildren,
    int HudChildren,
    int MenuChildren,
    int DebugChildren,
    int MenuHostChildren,
    int WorldHostChildren,
    int HudHostChildren,
    int GameplayGumpHostChildren,
    int ShellMenuHostChildren,
    int ContextChildren,
    int ContextHostChildren,
    int OpenGumpWindows,
    bool GumpPointerCaptured,
    GodotClientDebugPage DebugPage,
    string? FocusedControlType,
    bool ServerProfileActive,
    ClientControlGrammar ControlGrammar,
    ClientCameraProfile CameraProfile,
    float CameraDistance,
    float CameraFovDegrees,
    string PhysicalBindingSignature,
    int RenderDistanceChunks,
    ClientShellSnapshot Shell);

/// <summary>
/// Persistent F0.1 Godot client root. Concrete executables supply only their
/// compile-time capability manifest and later capability activation hooks.
/// </summary>
public abstract partial class ClientRoot : Control
{
    private readonly ClientShellCoordinator _shell = new();
    private ClientCompositionManifest _composition;
    private ClientSettingsDocument _settings = ClientSettingsDocument.Empty;
    private ResolvedClientDisplayConfiguration _display = ClientDisplayResolver.Resolve(ClientDisplaySettings.Default);
    private ResolvedClientSettings _resolved = ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
    private ClientSettingsFileStore? _settingsStore;
    private ClientSettingsLoadStatus _settingsLoadStatus;
    private ClientSettingsScopeKey _activeSettingsScope;
    private ClientProfileRecommendation? _activeProfileRecommendation;
    private ClientProfileApplicationMode _activeProfileMode = ClientProfileApplicationMode.Ignore;
    private Node? _controllerRoot;
    private Node? _contentCatalogRoot;
    private Node? _worldRoot;
    private CanvasLayer? _worldUiLayer;
    private CanvasLayer? _hudLayer;
    private CanvasLayer? _menuLayer;
    private CanvasLayer? _debugLayer;
    private CanvasLayer? _contextLayer;
    private CanvasLayer? _workspaceLayer;
    private Control? _menuHost;
    private Label? _hudStatus;
    private GodotClientUiComposition? _uiComposition;
    private GodotClientAudioAdapter? _audioAdapter;
    private ClientShellScreen _lastSoundScreen;
    private bool _soundScreenObserved;
    private readonly ClientFrameIntervalSampler _frameIntervals = new();

    protected GodotClientAudioAdapter? AudioAdapter => _audioAdapter;

    protected abstract ClientCompositionManifest Composition { get; }

    /// <summary>Persistent capability-owned controller root.</summary>
    protected Node ControllerRoot => _controllerRoot ??
        throw new InvalidOperationException("The client shell is not ready.");

    /// <summary>Persistent owner of active resolved-content catalog markers.</summary>
    protected Node ContentCatalogRoot => _contentCatalogRoot ??
        throw new InvalidOperationException("The client shell is not ready.");

    /// <summary>Persistent world host used by concrete local or remote presentation capabilities.</summary>
    protected Node WorldRoot => _worldRoot ??
        throw new InvalidOperationException("The client shell is not ready.");

    protected CanvasLayer WorldUiLayer => _worldUiLayer ??
        throw new InvalidOperationException("The client shell is not ready.");

    protected CanvasLayer HudLayer => _hudLayer ??
        throw new InvalidOperationException("The client shell is not ready.");

    protected GodotClientUiComposition UiComposition => _uiComposition ??
        throw new InvalidOperationException("The client UI composition is not ready.");

    protected GodotSessionUiLease BeginSessionUi(string name) => UiComposition.BeginSession(name);

    protected ResolvedClientSettings EffectiveSettings => _resolved;
    protected ResolvedClientDisplayConfiguration EffectiveDisplay => _display;

    public override void _Input(InputEvent inputEvent)
    {
        if (!CanStartApplicationWork) return;
        // World lives in an independent SubViewport. Its gameplay input still originates in
        // application coordinates, so session UI policy sees the same events as before.
        if (_uiComposition is not null && _shell.Snapshot().SessionActive)
            _uiComposition.GameplayViewport.PushInput(inputEvent, true);
    }

    protected void ActivateServerProfile(
        ClientSettingsScopeKey scope,
        ClientProfileRecommendation recommendation,
        ClientProfileApplicationMode mode)
    {
        if (!scope.IsValid) throw new ArgumentException("A verified realm settings scope is required.", nameof(scope));
        ArgumentNullException.ThrowIfNull(recommendation);
        if (mode is not (ClientProfileApplicationMode.UseServerDefaults or
            ClientProfileApplicationMode.KeepMyControls or ClientProfileApplicationMode.Customize))
            throw new ArgumentOutOfRangeException(nameof(mode));
        _activeSettingsScope = scope;
        _activeProfileRecommendation = recommendation;
        _activeProfileMode = mode;
        ResolveEffectiveSettings();
    }

    protected void ClearServerProfile()
    {
        _activeSettingsScope = default;
        _activeProfileRecommendation = null;
        _activeProfileMode = ClientProfileApplicationMode.Ignore;
        ResolveEffectiveSettings();
    }

    protected void OpenServerProfileCustomization()
    {
        if (!_activeSettingsScope.IsValid || _activeProfileRecommendation is null)
            throw new InvalidOperationException("No authenticated server profile is active.");
        _shell.OpenAdmissionSettings();
        RenderCurrentSurface();
    }

    protected ClientShellSnapshot ShellSnapshot() => _shell.Snapshot(
        textEntryFocused: GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit);

    protected GodotClientSessionBoundarySnapshot CaptureSessionBoundary()
    {
        GodotClientUiCompositionSnapshot ui = UiComposition.GetSnapshot();
        return new GodotClientSessionBoundarySnapshot(
            ControllerRoot.GetChildCount(),
            ContentCatalogRoot.GetChildCount(),
            WorldRoot.GetChildCount(),
            WorldUiLayer.GetChildCount(),
            HudLayer.GetChildCount(),
            _menuLayer?.GetChildCount() ?? 0,
            _debugLayer?.GetChildCount() ?? 0,
            _menuHost?.GetChildCount() ?? 0,
            ui.WorldHostChildren,
            ui.HudHostChildren,
            ui.GameplayGumpHostChildren,
            ui.ShellMenuHostChildren,
            ui.ContextHostChildren,
            _contextLayer?.GetChildCount() ?? 0,
            ui.Gumps.OpenWindows,
            ui.Gumps.PointerCaptured,
            ui.DebugPage,
            GetViewport().GuiGetFocusOwner()?.GetType().Name,
            _activeSettingsScope.IsValid || _activeProfileRecommendation is not null ||
            _activeProfileMode != ClientProfileApplicationMode.Ignore,
            _resolved.ControlGrammar,
            _resolved.CameraProfile,
            _resolved.CameraDistance,
            _resolved.CameraFovDegrees,
            PhysicalBindingSignature(_resolved),
            _resolved.RenderDistanceChunks,
            ShellSnapshot());
    }

    protected void RequireCleanSessionBoundary(
        in GodotClientSessionBoundarySnapshot baseline,
        string stage)
    {
        GodotClientSessionBoundarySnapshot current = CaptureSessionBoundary();
        if (current.ControllerChildren != baseline.ControllerChildren ||
            current.ContentCatalogChildren != baseline.ContentCatalogChildren ||
            current.WorldChildren != baseline.WorldChildren ||
            current.WorldUiChildren != baseline.WorldUiChildren ||
            current.HudChildren != baseline.HudChildren ||
            current.MenuChildren != baseline.MenuChildren ||
            current.DebugChildren != baseline.DebugChildren ||
            current.MenuHostChildren != baseline.MenuHostChildren ||
            current.WorldHostChildren != baseline.WorldHostChildren ||
            current.HudHostChildren != baseline.HudHostChildren ||
            current.GameplayGumpHostChildren != baseline.GameplayGumpHostChildren ||
            current.ShellMenuHostChildren != baseline.ShellMenuHostChildren ||
            current.ContextChildren != baseline.ContextChildren ||
            current.ContextHostChildren != baseline.ContextHostChildren ||
            current.OpenGumpWindows != 0 ||
            current.GumpPointerCaptured ||
            current.DebugPage != GodotClientDebugPage.Off ||
            current.ServerProfileActive ||
            current.ControlGrammar != baseline.ControlGrammar ||
            current.CameraProfile != baseline.CameraProfile ||
            !Mathf.IsEqualApprox(current.CameraDistance, baseline.CameraDistance) ||
            !Mathf.IsEqualApprox(current.CameraFovDegrees, baseline.CameraFovDegrees) ||
            !string.Equals(current.PhysicalBindingSignature, baseline.PhysicalBindingSignature,
                StringComparison.Ordinal) ||
            current.RenderDistanceChunks != baseline.RenderDistanceChunks ||
            current.Shell.Screen != ClientShellScreen.MainMenu ||
            current.Shell.SessionActive ||
            current.Shell.Focus.Owner != ClientInputFocusOwner.ModalMenu ||
            current.Shell.Focus.GameplayInputAllowed ||
            current.Shell.Focus.TextInputActive ||
            WorldRoot.FindChild("LocalCabinSession", recursive: true, owned: false) is not null ||
            WorldRoot.FindChild("RemoteConceptDemoSession", recursive: true, owned: false) is not null)
            throw new InvalidOperationException(
                $"The {stage} session boundary retained transient client state: " +
                $"baseline={baseline}; current={current}.");
    }

    /// <summary>Exact canonical profile-owned binding surface used by lifecycle evidence.</summary>
    protected static string PhysicalBindingSignature(ResolvedClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Join(';', settings.Bindings.Select(static binding =>
            $"{(byte)binding.Action}:{(byte)binding.Input.Kind}:" +
            $"{(byte)binding.Input.Modifiers}:{binding.Input.Code}"));
    }

    protected void BeginSessionLoading(string status)
    {
        if (!CanStartApplicationWork) throw new InvalidOperationException("Application is stopping.");
        _entryProgress = null;
        _shell.BeginLoading(status);
        RenderCurrentSurface();
    }

    private ClientWorldEntryProgress? _entryProgress;

    protected void PublishSessionLoading(ClientWorldEntryProgress progress)
    {
        if (!CanStartApplicationWork) return;
        progress.Validate();
        if (_shell.Snapshot().Screen != ClientShellScreen.Loading) return;
        _entryProgress = progress;
        RenderCurrentSurface();
    }

    protected virtual void CancelSessionLoading()
    {
        RetireCurrentSession();
        _shell.ReturnToMainMenu();
        RenderCurrentSurface();
    }

    protected void BeginAdmission(string status)
    {
        if (!CanStartApplicationWork) return;
        _shell.BeginAdmission(status);
        RenderCurrentSurface();
    }

    protected void CommitSession(string status)
    {
        if (!CanStartApplicationWork) throw new InvalidOperationException("Application is stopping.");
        _shell.EnterSession(status);
        _audioAdapter?.BeginSession();
        RenderCurrentSurface();
    }

    protected void FailSession(string message)
    {
        if (!CanStartApplicationWork) return;
        _shell.Fail(message);
        RenderCurrentSurface();
    }

    protected void ReturnToMainMenu(string? status = null)
    {
        if (!CanStartApplicationWork) return;
        _shell.ReturnToMainMenu(status);
        _audioAdapter?.EndSession();
        RenderCurrentSurface();
    }

    public override void _Ready()
    {
        if (ApplicationPhase == GodotApplicationPhase.Created) BeginApplicationStartup();
        if (!CanStartApplicationWork) return;
        try
        {
            _composition = Composition;
            Validate(_composition);
            LoadSettings();
            BuildPersistentRoots();
            RegisterAdapterControl();
            RenderCurrentSurface();
            string capabilities = string.Join(',', _composition.Capabilities);
            GD.Print(
                $"ULTRASIM CLIENT READY edition={_composition.Edition} capabilities={capabilities} " +
                $"screen={_shell.Snapshot().Screen} settings={_settingsLoadStatus}");
            if (OS.GetCmdlineUserArgs().Contains("--shell-ui-smoke", StringComparer.Ordinal))
            {
                Callable.From(RunShellUiSmoke).CallDeferred();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--p8-audio-smoke", StringComparer.Ordinal))
            {
                Callable.From(RunP8AudioSmoke).CallDeferred();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--p5-management-smoke", StringComparer.Ordinal))
            {
                Callable.From(() => { _ = RunP5ManagementSmokeAsync(); }).CallDeferred();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--composition-smoke", StringComparer.Ordinal))
            {
                ValidateShellSmoke();
                GD.Print(
                    $"F0.1 CLIENT SHELL SMOKE PASS edition={_composition.Edition} " +
                    $"capabilities={capabilities} " +
                    "layers=Workspace,World,HUD,Menus,Debug,ContextTooltip");
                RequestApplicationExit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Client shell initialization failed: {exception}");
            RequestApplicationExit(1);
        }
    }

    private void RunP8AudioSmoke()
    {
        try
        {
            GodotClientAudioAdapter audio = _audioAdapter ??
                throw new InvalidOperationException("The compiled client has no audio adapter.");
            if (!audio.GetSnapshot().CatalogBacked)
                throw new InvalidOperationException("The product did not resolve owned audio through world.local CAS.");
            audio.SetMasterVolume(.75f);
            audio.BeginSession();
            audio.SetListener(default);
            if (!audio.TryPlay(ClientSoundEvent.UiClick) ||
                !audio.TryPlay(ClientSoundEvent.DoorOpen, 500, 0, 0) ||
                audio.GetSnapshot().ActiveLoops != 1)
                throw new InvalidOperationException("Representative native audio categories did not realize.");
            audio.SetMasterVolume(0);
            if (!audio.GetSnapshot().Muted || audio.GetSnapshot().ActiveSources != 0 ||
                audio.TryPlay(ClientSoundEvent.UiClick))
                throw new InvalidOperationException("Live Master Volume mute did not silence native audio.");
            audio.SetMasterVolume(.75f);
            if (!audio.TryPlay(ClientSoundEvent.UiOpen))
                throw new InvalidOperationException("Unmuting did not restore future cue realization.");
            var principal = new UltraSim.Framework.ControlPlane.ControlPrincipal("p8-native",
                UltraSim.Framework.ControlPlane.ControlPrincipalKind.TestAutomation,
                [GodotAdapterControlProvider.Read]);
            if (_adapterControlPlane.Read(principal, GodotAdapterControlProvider.Id,
                    _adapterControlGeneration, new UltraSim.Framework.ControlPlane.ControlId(
                        "audio-implementation"), out var selected) !=
                    UltraSim.Framework.ControlPlane.ControlResultCode.Accepted ||
                selected?.Effective.AsOption().Value != "godot")
                throw new InvalidOperationException("Control Plane did not publish the native audio adapter.");
            audio.EndSession();
            GD.Print("P8 PRODUCT AUDIO SMOKE PASS catalog=world.local " +
                "categories=SFX,Ambient,UI master=live mute=silent sources=retired");
            RequestApplicationExit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"P8 product audio smoke failed: {error}");
            RequestApplicationExit(1);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!CanStartApplicationWork) return;
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (Matches(key, ClientSemanticAction.ExitApplication))
        {
            RequestApplicationExit(0);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Matches(key, ClientSemanticAction.CycleDebug))
        {
            UiComposition.CycleDebug(BuildDebugPage);
            GetViewport().SetInputAsHandled();
            return;
        }
        ClientShellSnapshot snapshot = _shell.Snapshot(
            textEntryFocused: GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit);
        if (Matches(key, ClientSemanticAction.OpenSettings))
        {
            if (snapshot.Screen == ClientShellScreen.Settings) _shell.CloseSettings();
            else if (snapshot.Screen is not (ClientShellScreen.Admission or
                ClientShellScreen.Loading or ClientShellScreen.Error))
                _shell.OpenSettings();
            RenderCurrentSurface();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Matches(key, ClientSemanticAction.Disconnect) && snapshot.SessionActive)
        {
            RetireCurrentSession();
            _shell.ReturnToMainMenu("Session disconnected.");
            RenderCurrentSurface();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Matches(key, ClientSemanticAction.OpenClientMenu)) return;
        switch (snapshot.Screen)
        {
            case ClientShellScreen.InSession:
                _shell.OpenInSessionOptions();
                break;
            case ClientShellScreen.InSessionOptions:
                _shell.ResumeSession();
                break;
            case ClientShellScreen.Settings:
                _shell.CloseSettings();
                break;
            case ClientShellScreen.Admission:
            case ClientShellScreen.Loading:
            case ClientShellScreen.Error:
                RetireCurrentSession();
                _shell.ReturnToMainMenu();
                break;
        }
        RenderCurrentSurface();
        GetViewport().SetInputAsHandled();
    }

    public GodotClientRootSnapshot GetSnapshot()
    {
        ClientShellSnapshot shell = _shell.Snapshot(
            textEntryFocused: GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit);
        return new GodotClientRootSnapshot(
            _composition.Edition,
            shell,
            _settingsLoadStatus,
            _composition.Capabilities?.Count ?? 0,
            _controllerRoot is not null,
            _contentCatalogRoot is not null,
            _worldRoot is not null,
            _worldUiLayer is not null,
            _hudLayer is not null,
            _menuLayer is not null,
            _debugLayer is not null,
            _contextLayer is not null,
            _workspaceLayer is not null,
            UiComposition.GetSnapshot(),
            CaptureSessionBoundary());
    }

    /// <summary>F0.2/F0.3 composition roots replace this with real session activation.</summary>
    protected virtual void BeginCapability(ClientCapability capability)
    {
        _shell.BeginLoading($"{Label(capability)} capability is ready for its consuming checkpoint.");
        RenderCurrentSurface();
    }

    /// <summary>Capability-specific pre-session intent UI; no authority is active yet.</summary>
    protected virtual void BuildCapabilityAdmissionSurface(in ClientShellSnapshot snapshot) =>
        BuildMessageSurface(
            "Enter application",
            snapshot.Status ?? "Choose how to enter.",
            "Back",
            () => ReturnToMainMenu());

    /// <summary>Later composition roots retire their concrete session-owned presentation here.</summary>
    protected virtual void RetireCurrentSession() { }

    /// <summary>
    /// Gives an active capability newly resolved client-owned settings without granting it
    /// ownership of persistence or profile resolution.
    /// </summary>
    protected virtual void OnEffectiveSettingsChanged(ResolvedClientSettings settings) =>
        _audioAdapter?.SetMasterVolume(settings.MasterVolume);

    private void LoadSettings()
    {
        string path = ProjectSettings.GlobalizePath("user://settings/client.json");
        _settingsStore = new ClientSettingsFileStore(path);
        ClientSettingsLoadResult loaded = _settingsStore.Load();
        _settingsLoadStatus = loaded.Status;
        _settings = loaded.Document;
        // Deterministic native/export acceptance input only. It is not persisted, sent over
        // Network, or accepted from a server profile.
        if (OS.GetCmdlineUserArgs().Contains("--f1-9a-scaled-output", StringComparer.Ordinal))
            _settings = _settings.WithDisplay(ClientDisplaySettings.Default.WithAdvancedFromSimple() with
            {
                AdvancedOutput = ClientPixelSize.Create(1280, 720),
                AdvancedInternalScalePercent = 75,
            });
        _display = ClientDisplayResolver.Resolve(_settings.Display);
        ResolveEffectiveSettings(notify: false);
    }

    private void BuildPersistentRoots()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _controllerRoot = NamedNode("Controller");
        _contentCatalogRoot = NamedNode("ContentCatalogs");
        _display = ClientDisplayResolver.Resolve(_settings.Display);
        _uiComposition = new GodotClientUiComposition(this, _display);
        _uiComposition.ConfigurePerformance(_frameIntervals, CapturePerformanceContext, _composition.Edition);
        _uiComposition.Gumps.WindowChanged += OnGumpWindowChanged;
        ApplyNativeWindow(_display);
        _worldRoot = NamedNode("World");
        AddChild(_controllerRoot);
        AddChild(_contentCatalogRoot);
        _uiComposition.GameplayViewport.AddChild(_worldRoot);

        Node map = NamedNode("Map");
        map.AddChild(NamedNode("Terrain"));
        map.AddChild(NamedNode("FrozenStatics"));
        map.AddChild(NamedNode("Environment"));
        _worldRoot.AddChild(map);
        _worldRoot.AddChild(NamedNode("DynamicEntities"));
        _worldRoot.AddChild(NamedNode("Effects"));
        _worldRoot.AddChild(NamedNode("CameraRig"));
        try
        {
            _audioAdapter = new GodotClientAudioAdapter(_worldRoot);
            _audioAdapter.SetMasterVolume(_resolved.MasterVolume);
        }
        catch (Exception error)
        {
            _audioAdapter = null;
            GD.PushWarning($"Optional native audio is unavailable: {error}");
        }

        _workspaceLayer = GetNode<CanvasLayer>("UILayers/Workspace");
        _worldUiLayer = _uiComposition.WorldLayer;
        _hudLayer = _uiComposition.HudLayer;
        _menuLayer = _uiComposition.MenuLayer;
        _debugLayer = _uiComposition.DebugLayer;
        _contextLayer = _uiComposition.ContextLayer;
        _menuHost = _uiComposition.ShellMenuHost;
        _hudStatus = new Label { Name = "SessionStatus", Visible = false };
        _hudStatus.SetAnchorsPreset(LayoutPreset.TopWide);
        _uiComposition.HudHost.AddChild(_hudStatus);
        Resized += _uiComposition.UpdateLayoutIfNeeded;
        if (OS.GetCmdlineUserArgs().Contains("--f1-9a-scaled-output", StringComparer.Ordinal))
        {
            if (_display.GameplayOutputResolution != ClientPixelSize.Create(1280, 720) ||
                _display.InternalRenderResolution != ClientPixelSize.Create(960, 540) ||
                !ReferenceEquals(_worldRoot.GetParent(), _uiComposition.GameplayViewport))
                throw new InvalidOperationException("The scaled native output acceptance did not compose.");
            GD.Print("F1.9A SCALED OUTPUT PASS app=1920x1080 frame=1920x1080 " +
                "output=1280x720 internal3D=960x540 world=SubViewport");
        }
    }

    protected void ApplyDisplay(ResolvedClientDisplayConfiguration next)
    {
        ResolvedClientDisplayConfiguration previous = _display;
        try
        {
            ApplyNativeWindow(next);
            UiComposition.ApplyDisplay(next);
            _display = next;
        }
        catch
        {
            ApplyNativeWindow(previous);
            UiComposition.ApplyDisplay(previous);
            throw;
        }
    }

    private void ApplyNativeWindow(ResolvedClientDisplayConfiguration display)
    {
        Window window = GetWindow();
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        window.ContentScaleSize = new Vector2I(
            display.ApplicationResolution.Width, display.ApplicationResolution.Height);
        window.Mode = display.DisplayMode switch
        {
            ClientDisplayMode.Windowed => Window.ModeEnum.Windowed,
            ClientDisplayMode.BorderlessFullscreen => Window.ModeEnum.Fullscreen,
            ClientDisplayMode.Fullscreen => Window.ModeEnum.ExclusiveFullscreen,
            _ => throw new ArgumentOutOfRangeException(nameof(display)),
        };
        if (display.DisplayMode == ClientDisplayMode.Windowed &&
            !string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
        {
            Vector2I desktop = DisplayServer.ScreenGetUsableRect().Size;
            window.Size = new Vector2I(
                Math.Min(display.ApplicationResolution.Width, desktop.X),
                Math.Min(display.ApplicationResolution.Height, desktop.Y));
        }
    }

    protected void RenderCurrentSurface()
    {
        if (_menuHost is null) return;
        ClientShellSnapshot snapshot = _shell.Snapshot();
        if (_soundScreenObserved && snapshot.SessionActive &&
            snapshot.Screen != _lastSoundScreen)
        {
            if (_lastSoundScreen == ClientShellScreen.InSession)
                _ = _audioAdapter?.TryPlay(ClientSoundEvent.UiOpen);
            else if (snapshot.Screen == ClientShellScreen.InSession)
                _ = _audioAdapter?.TryPlay(ClientSoundEvent.UiClose);
        }
        _lastSoundScreen = snapshot.Screen;
        _soundScreenObserved = true;
        ClearChildren(_menuHost);
        if (snapshot.Screen == ClientShellScreen.MainMenu && !snapshot.SessionActive)
            UiComposition.ResetDebug();
        _menuHost.Visible = snapshot.Screen != ClientShellScreen.InSession;
        UiComposition.SetShellModalVisible(_menuHost.Visible);
        if (_hudStatus is not null)
        {
            _hudStatus.Visible = snapshot.SessionActive;
            _hudStatus.Text = snapshot.Status ?? "Session active";
        }
        if (!_menuHost.Visible) return;

        switch (snapshot.Screen)
        {
            case ClientShellScreen.MainMenu:
                BuildMainMenu(snapshot);
                break;
            case ClientShellScreen.Settings:
                BuildSettingsMenu();
                break;
            case ClientShellScreen.Admission:
                BuildCapabilityAdmissionSurface(snapshot);
                break;
            case ClientShellScreen.Loading:
                BuildLoadingSurface(snapshot);
                break;
            case ClientShellScreen.InSessionOptions:
                BuildInSessionOptions();
                break;
            case ClientShellScreen.Error:
                BuildMessageSurface("Unable to continue", snapshot.Error ?? "Unknown client error.", "Main Menu", () =>
                {
                    RetireCurrentSession();
                    _shell.ReturnToMainMenu();
                    RenderCurrentSurface();
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported client shell screen {snapshot.Screen}.");
        }
    }

    private void OnGumpWindowChanged(bool opened)
    {
        if (CanStartApplicationWork && _audioAdapter is not null)
            _ = _audioAdapter.TryPlay(opened ? ClientSoundEvent.UiOpen : ClientSoundEvent.UiClose);
    }

    public override void _ExitTree() => RetireOnTreeExit();

    private void BuildMainMenu(in ClientShellSnapshot snapshot)
    {
        VBoxContainer column = CreateCenteredColumn();
        column.AddChild(Title($"UltraSim — {_composition.Edition}"));
        if (!string.IsNullOrWhiteSpace(snapshot.Status))
            column.AddChild(CenteredLabel(snapshot.Status));
        if (_settingsLoadStatus == ClientSettingsLoadStatus.InvalidFallback)
            column.AddChild(CenteredLabel("Invalid settings were ignored; built-in defaults are active."));
        foreach (ClientCapability capability in _composition.Capabilities)
        {
            Button button = MenuButton(Label(capability));
            button.Name = $"Capability_{capability}";
            switch (capability)
            {
                case ClientCapability.Settings:
                    ConnectDeferred(button, () =>
                    {
                        _shell.OpenSettings();
                        RenderCurrentSurface();
                    });
                    break;
                case ClientCapability.Exit:
                    ConnectDeferred(button, () => RequestApplicationExit(0));
                    break;
                default:
                    ClientCapability captured = capability;
                    ConnectDeferred(button, () => BeginCapability(captured));
                    break;
            }
            column.AddChild(button);
        }
        FocusFirstButton(column);
    }

    private void BuildSettingsMenu()
    {
        VBoxContainer column = CreateScrollableCenteredColumn(500);
        bool scoped = _activeSettingsScope.IsValid && _activeProfileRecommendation is not null;
        column.AddChild(Title(scoped ? "Server-Specific Settings" : "Client Settings"));
        if (scoped)
            column.AddChild(CenteredLabel(
                "These user-owned overrides apply only to the verified application realm. " +
                "The server recommendation is not copied into global settings."));

        Func<ClientDisplaySettings> readDisplay = BuildDisplaySettingsControls(column);
        var vegetation = new CheckBox
        {
            Name = "SettingsExperimentalVegetation",
            Text = "Experimental Vegetation / grass (next world entry)",
            ButtonPressed = _settings.ExperimentalVegetation,
            Visible = _settings.Display.SettingsMode == ClientDisplaySettingsMode.Advanced,
        };
        column.AddChild(vegetation);
        var advanced = column.FindChild("DisplayShowAdvanced", true, false) as CheckBox;
        if (advanced is not null)
        {
            // Keep the existing option discoverable instead of below all Advanced dimensions.
            column.MoveChild(vegetation, advanced.GetIndex() + 1);
            advanced.Toggled += enabled => vegetation.Visible = enabled;
        }

        HSlider uiScale = Slider(0.5, 3, 0.05, _resolved.UiScale);
        uiScale.Name = "SettingsUiScale";
        HSlider volume = Slider(0, 1, 0.05, _resolved.MasterVolume);
        HSlider sensitivity = Slider(0.1, 5, 0.1, _resolved.PointerSensitivity);
        CheckBox invert = new() { Text = "Invert camera Y", ButtonPressed = _resolved.InvertY };
        OptionButton grammar = EnumOptions(_resolved.ControlGrammar);
        OptionButton camera = EnumOptions(_resolved.CameraProfile);
        grammar.Name = "SettingsControlGrammar";
        camera.Name = "SettingsCameraProfile";
        HSlider distance = Slider(2, 80, 1, _resolved.CameraDistance);
        HSlider fov = Slider(40, 120, 1, _resolved.CameraFovDegrees);
        ClientCameraProfile initialCamera = (ClientCameraProfile)camera.GetSelectedId();
        float initialDistance = (float)distance.Value;
        float initialFov = (float)fov.Value;
        HSlider renderDistance = Slider(
            ClientSettingsResolver.MinimumRenderDistanceChunks,
            ClientSettingsResolver.MaximumRenderDistanceChunks,
            1,
            _resolved.RenderDistanceChunks);
        renderDistance.Name = "SettingsRenderDistance";
        AddSetting(column, "UI scale", uiScale);
        AddSetting(column, "Master volume", volume);
        AddSetting(column, "Pointer sensitivity", sensitivity);
        column.AddChild(invert);
        AddSetting(column, "Control grammar", grammar);
        AddSetting(column, "Camera profile", camera);
        AddSetting(column, "Camera distance", distance);
        AddSetting(column, "Camera FOV", fov);
        Label renderDistanceValue = new()
        {
            Text = $"Render distance: {(int)renderDistance.Value} chunks (applies on Save)",
        };
        renderDistance.ValueChanged += value =>
            renderDistanceValue.Text = $"Render distance: {(int)value} chunks (applies on Save)";
        column.AddChild(renderDistanceValue);
        column.AddChild(renderDistance);
        column.AddChild(new Label
        {
            Text = "The in-session HUD reports resident/rendered counts. Intentional-void chunks " +
                "contain no mesh, so increasing the window may not change the visible silhouette.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        column.AddChild(new Label
        {
            Text = "Bindings are readable/persisted in settings/client.json. " +
                "The interactive binding editor follows with a consuming gameplay shell.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        // This is a cold, provider-published User surface. The existing display form stays
        // intact for F1.12 compatibility while its owner semantics are migrated incrementally.
        EnsureSettingsControl();
        column.AddChild(Title("User Control Plane"));
        if (_managementStatus.Length != 0)
            column.AddChild(new Label { Text = _managementStatus,
                AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var management = new VBoxContainer { Name = "UserManagementSurface" };
        column.AddChild(management);
        _userManagementSurface!.Build(management);
        // Local read-only diagnostics are an explicit UI choice, never a server-claimed privilege.
        var developerToggle = MenuButton(_showDeveloperManagement ? "Hide developer diagnostics" : "Developer diagnostics");
        developerToggle.Name = "DeveloperManagementToggle";
        ConnectDeferred(developerToggle, () =>
        {
            _showDeveloperManagement = !_showDeveloperManagement;
            RenderCurrentSurface();
        });
        column.AddChild(developerToggle);
        if (_showDeveloperManagement)
        {
            column.AddChild(Title("Developer diagnostics (read-only)"));
            var diagnostics = new VBoxContainer { Name = "DeveloperManagementSurface" };
            column.AddChild(diagnostics);
            _developerManagementSurface!.Build(diagnostics);
        }

        Button save = MenuButton("Save");
        save.Name = "SettingsSave";
        ConnectDeferred(save, () =>
        {
            try
            {
                // The simple stock-preset UI must not silently replace a richer accepted or
                // user-named rig when an unrelated preference is saved. A camera edit is an
                // explicit choice of the displayed stock preset and lens values.
                ClientCameraProfile chosenCamera = (ClientCameraProfile)camera.GetSelectedId();
                float chosenDistance = (float)distance.Value;
                float chosenFov = (float)fov.Value;
                bool cameraEdited = chosenCamera != initialCamera ||
                    !Mathf.IsEqualApprox(chosenDistance, initialDistance) ||
                    !Mathf.IsEqualApprox(chosenFov, initialFov);
                ClientCameraRig chosenRig = cameraEdited
                    ? ClientStockCameraRigs.FromLegacy(chosenCamera)
                        .WithLegacyLens(chosenDistance, chosenFov)
                    : _resolved.CameraRig;
                ClientSettingsLayer saved = new(
                    (float)uiScale.Value,
                    (float)volume.Value,
                    (float)sensitivity.Value,
                    invert.ButtonPressed,
                    (ClientControlGrammar)grammar.GetSelectedId(),
                    chosenCamera,
                    chosenDistance,
                    chosenFov,
                    scoped && _settings.TryGetScope(_activeSettingsScope, out ClientSettingsLayer existing)
                        ? existing.Bindings
                        : _settings.Global.Bindings,
                    (int)renderDistance.Value,
                    chosenRig);
                ClientSettingsDocument candidate = scoped
                    ? _settings.WithScope(_activeSettingsScope, saved)
                    : _settings.WithGlobal(saved);
                candidate = candidate.WithDisplay(readDisplay())
                    .WithExperimentalVegetation(vegetation.ButtonPressed);
                ResolvedClientDisplayConfiguration nextDisplay = ClientDisplayResolver.Resolve(candidate.Display);
                ResolvedClientDisplayConfiguration previousDisplay = _display;
                ApplyDisplay(nextDisplay);
                try { _settingsStore!.Save(candidate); }
                catch
                {
                    ApplyDisplay(previousDisplay);
                    throw;
                }
                _settings = candidate;
                _settingsLoadStatus = ClientSettingsLoadStatus.Loaded;
                ResolveEffectiveSettings();
                _shell.CloseSettings();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _shell.Fail($"Settings could not be saved: {exception.Message}");
            }
            RenderCurrentSurface();
        });
        column.AddChild(save);
        Button reset = MenuButton(scoped ? "Reset Realm Overrides" : "Reset Global Defaults");
        reset.Name = "SettingsReset";
        ConnectDeferred(reset, () =>
        {
            try
            {
                ClientSettingsDocument candidate = scoped
                    ? _settings.WithScope(_activeSettingsScope, ClientSettingsLayer.Empty)
                    : new ClientSettingsDocument(ClientSettingsLayer.Empty, _settings.Scopes,
                        _settings.NamedProfiles, ClientDisplaySettings.Default);
                ResolvedClientDisplayConfiguration previousDisplay = _display;
                ApplyDisplay(ClientDisplayResolver.Resolve(candidate.Display));
                try { _settingsStore!.Save(candidate); }
                catch
                {
                    ApplyDisplay(previousDisplay);
                    throw;
                }
                _settings = candidate;
                _settingsLoadStatus = ClientSettingsLoadStatus.Loaded;
                ResolveEffectiveSettings();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _shell.Fail($"Settings could not be reset: {exception.Message}");
            }
            RenderCurrentSurface();
        });
        column.AddChild(reset);
        Button back = MenuButton("Back");
        back.Name = "SettingsBack";
        ConnectDeferred(back, () =>
        {
            _shell.CloseSettings();
            RenderCurrentSurface();
        });
        column.AddChild(back);
        FocusFirstButton(column);
    }

    private Func<ClientDisplaySettings> BuildDisplaySettingsControls(VBoxContainer column)
    {
        ClientDisplaySettings current = _settings.Display;
        column.AddChild(Title("Display (this device only)"));
        OptionButton displayMode = EnumOptions(current.DisplayMode);
        displayMode.Name = "DisplayWindowMode";
        AddSetting(column, "Display mode", displayMode);
        var advancedToggle = new CheckBox
        {
            Name = "DisplayShowAdvanced",
            Text = "Show Advanced",
            ButtonPressed = current.SettingsMode == ClientDisplaySettingsMode.Advanced,
        };
        column.AddChild(advancedToggle);

        var simplePanel = new VBoxContainer { Name = "SimpleDisplaySettings" };
        column.AddChild(simplePanel);
        OptionButton aspect = EnumOptions(current.SimpleAspect);
        aspect.Name = "DisplayAspect";
        OptionButton resolution = new() { Name = "DisplayResolution" };
        AddSetting(simplePanel, "Aspect ratio", aspect);
        AddSetting(simplePanel, "Resolution", resolution);
        void PopulateResolution(ClientPixelSize preferred)
        {
            resolution.Clear();
            IReadOnlyList<ClientPixelSize> sizes = ClientDisplayPresetCatalog.ForAspect(
                (ClientDisplayAspectPreset)aspect.GetSelectedId());
            int selected = 0;
            for (int index = 0; index < sizes.Count; index++)
            {
                resolution.AddItem($"{sizes[index].Width} × {sizes[index].Height}", index);
                if (sizes[index] == preferred) selected = index;
            }
            resolution.Select(selected);
        }
        PopulateResolution(current.SimpleResolution);
        aspect.ItemSelected += _ => PopulateResolution(default);

        var advancedPanel = new VBoxContainer { Name = "AdvancedDisplaySettings" };
        column.AddChild(advancedPanel);
        static SpinBox Dimension(int value) => new()
        {
            MinValue = 1, MaxValue = ClientDisplayResolver.MaximumDimension,
            Step = 1, Rounded = true, Value = value,
        };
        SpinBox applicationWidth = Dimension(current.AdvancedApplication.Width);
        SpinBox applicationHeight = Dimension(current.AdvancedApplication.Height);
        SpinBox frameWidth = Dimension(current.AdvancedFrameSize.Width);
        SpinBox frameHeight = Dimension(current.AdvancedFrameSize.Height);
        SpinBox outputWidth = Dimension(current.AdvancedOutput.Width);
        SpinBox outputHeight = Dimension(current.AdvancedOutput.Height);
        applicationWidth.Name = "DisplayApplicationWidth";
        applicationHeight.Name = "DisplayApplicationHeight";
        frameWidth.Name = "DisplayFrameWidth";
        frameHeight.Name = "DisplayFrameHeight";
        outputWidth.Name = "DisplayOutputWidth";
        outputHeight.Name = "DisplayOutputHeight";
        OptionButton frameMode = EnumOptions(current.AdvancedFrameMode);
        frameMode.Name = "DisplayFrameMode";
        SpinBox scale = new()
        {
            MinValue = ClientDisplayResolver.MinimumInternalScalePercent,
            MaxValue = ClientDisplayResolver.MaximumInternalScalePercent,
            Step = 1, Rounded = true, Value = current.AdvancedInternalScalePercent,
        };
        scale.Name = "DisplayInternalScale";
        AddSetting(advancedPanel, "Application width", applicationWidth);
        AddSetting(advancedPanel, "Application height", applicationHeight);
        AddSetting(advancedPanel, "Gameplay Frame", frameMode);
        var frameWidthLabel = new Label { Text = "Custom frame width" };
        var frameHeightLabel = new Label { Text = "Custom frame height" };
        advancedPanel.AddChild(frameWidthLabel);
        advancedPanel.AddChild(frameWidth);
        advancedPanel.AddChild(frameHeightLabel);
        advancedPanel.AddChild(frameHeight);
        AddSetting(advancedPanel, "Gameplay Output width", outputWidth);
        AddSetting(advancedPanel, "Gameplay Output height", outputHeight);
        AddSetting(advancedPanel, "Internal 3D render scale (%)", scale);
        var internalSize = new Label { Name = "DisplayInternalResolution" };
        var resolvedFrame = new Label { Name = "DisplayResolvedFrame" };
        advancedPanel.AddChild(resolvedFrame);
        advancedPanel.AddChild(internalSize);
        void UpdateResolvedLabels()
        {
            internalSize.Text =
                $"Internal 3D (computed): {(int)Math.Round(outputWidth.Value * scale.Value / 100d)} × " +
                $"{(int)Math.Round(outputHeight.Value * scale.Value / 100d)}";
            try
            {
                ClientPixelSize application = ClientPixelSize.Create(
                    (int)applicationWidth.Value, (int)applicationHeight.Value);
                ClientPixelSize output = ClientPixelSize.Create((int)outputWidth.Value, (int)outputHeight.Value);
                GameplayFrameLayout frame = (ClientGameplayFrameMode)frameMode.GetSelectedId() switch
                {
                    ClientGameplayFrameMode.Fill => GameplayFrameLayout.Fill(application, output),
                    ClientGameplayFrameMode.MaxFit => GameplayFrameLayout.MaxFitExact(application, output),
                    _ => GameplayFrameLayout.Custom(application, output, ClientPixelSize.Create(
                        (int)frameWidth.Value, (int)frameHeight.Value)),
                };
                resolvedFrame.Text = $"Gameplay Frame size (resolved): " +
                    $"{frame.Frame.Width} × {frame.Frame.Height}";
            }
            catch (ArgumentException)
            {
                resolvedFrame.Text = "Gameplay Frame size: invalid — adjust dimensions";
            }
        }
        foreach (SpinBox spin in new[] { applicationWidth, applicationHeight, frameWidth,
                     frameHeight, outputWidth, outputHeight, scale })
            spin.ValueChanged += _ => UpdateResolvedLabels();
        UpdateResolvedLabels();
        void UpdateVisibility()
        {
            simplePanel.Visible = !advancedToggle.ButtonPressed;
            advancedPanel.Visible = advancedToggle.ButtonPressed;
            bool customFrame = (ClientGameplayFrameMode)frameMode.GetSelectedId() ==
                ClientGameplayFrameMode.Custom;
            frameWidthLabel.Visible = customFrame;
            frameWidth.Visible = customFrame;
            frameHeightLabel.Visible = customFrame;
            frameHeight.Visible = customFrame;
            UpdateResolvedLabels();
        }
        frameMode.ItemSelected += _ => UpdateVisibility();
        advancedToggle.Toggled += enabled =>
        {
            if (enabled && current.SettingsMode == ClientDisplaySettingsMode.Simple)
            {
                IReadOnlyList<ClientPixelSize> sizes = ClientDisplayPresetCatalog.ForAspect(
                    (ClientDisplayAspectPreset)aspect.GetSelectedId());
                ClientPixelSize selected = sizes[resolution.GetSelectedId()];
                applicationWidth.Value = frameWidth.Value = outputWidth.Value = selected.Width;
                applicationHeight.Value = frameHeight.Value = outputHeight.Value = selected.Height;
                frameMode.Select((int)ClientGameplayFrameMode.Fill - 1);
                scale.Value = 100;
            }
            UpdateVisibility();
        };
        UpdateVisibility();
        return () => new ClientDisplaySettings(
            advancedToggle.ButtonPressed ? ClientDisplaySettingsMode.Advanced : ClientDisplaySettingsMode.Simple,
            (ClientDisplayMode)displayMode.GetSelectedId(),
            (ClientDisplayAspectPreset)aspect.GetSelectedId(),
            ClientDisplayPresetCatalog.ForAspect((ClientDisplayAspectPreset)aspect.GetSelectedId())
                [resolution.GetSelectedId()],
            ClientPixelSize.Create((int)applicationWidth.Value, (int)applicationHeight.Value),
            (ClientGameplayFrameMode)frameMode.GetSelectedId(),
            ClientPixelSize.Create((int)frameWidth.Value, (int)frameHeight.Value),
            ClientPixelSize.Create((int)outputWidth.Value, (int)outputHeight.Value),
            (int)scale.Value);
    }

    private void BuildInSessionOptions()
    {
        VBoxContainer column = CreateCenteredColumn();
        column.AddChild(Title("Session Options"));
        Button resume = MenuButton("Resume");
        ConnectDeferred(resume, () =>
        {
            _shell.ResumeSession();
            RenderCurrentSurface();
        });
        column.AddChild(resume);
        Button settings = MenuButton("Settings");
        ConnectDeferred(settings, () =>
        {
            _shell.OpenSettings();
            RenderCurrentSurface();
        });
        column.AddChild(settings);
        BuildAdditionalInSessionOptions(column);
        Button disconnect = MenuButton("Disconnect / Main Menu");
        ConnectDeferred(disconnect, () =>
        {
            RetireCurrentSession();
            _shell.ReturnToMainMenu("Session disconnected.");
            RenderCurrentSurface();
        });
        column.AddChild(disconnect);
        FocusFirstButton(column);
    }

    /// <summary>Capability-owned options inserted without transferring shell lifecycle ownership.</summary>
    protected virtual void BuildAdditionalInSessionOptions(VBoxContainer column)
    {
    }

    protected void ResumeCurrentSession()
    {
        _shell.ResumeSession();
        RenderCurrentSurface();
    }

    private void BuildMessageSurface(
        string title,
        string message,
        string action,
        Action pressed)
    {
        VBoxContainer column = CreateCenteredColumn();
        column.AddChild(Title(title));
        column.AddChild(CenteredLabel(message));
        Button button = MenuButton(action);
        ConnectDeferred(button, pressed);
        column.AddChild(button);
        button.GrabFocus();
    }

    private void BuildLoadingSurface(in ClientShellSnapshot snapshot)
    {
        VBoxContainer column = CreateCenteredColumn(520);
        column.AddChild(Title("Loading world…"));
        column.AddChild(CenteredLabel(snapshot.Status ?? "Preparing client session…"));
        if (_entryProgress is ClientWorldEntryProgress progress)
        {
            column.AddChild(CenteredLabel(progress.Stage switch
            {
                ClientWorldEntryStage.Resolving => "Selecting world",
                ClientWorldEntryStage.StartingAuthority => "Starting simulation",
                ClientWorldEntryStage.PreparingContent => "Loading content",
                ClientWorldEntryStage.PreparingMaterials => "Loading materials",
                ClientWorldEntryStage.PreparingMeshes => "Preparing meshes",
                ClientWorldEntryStage.BuildingWorld => "Building world",
                ClientWorldEntryStage.WarmingPresentation => "Warming presentation",
                ClientWorldEntryStage.Finalizing => "Finalizing",
                ClientWorldEntryStage.Ready => "Ready",
                ClientWorldEntryStage.Cancelled => "Cancelled",
                ClientWorldEntryStage.Failed => "Unable to enter",
                _ => "Preparing world",
            }));
            column.AddChild(CenteredLabel(progress.Detail));
            if (progress.TotalUnits is int total)
                column.AddChild(CenteredLabel($"{progress.CompletedUnits} / {total}"));
            else if (!progress.IsTerminal)
                column.AddChild(CenteredLabel("Working…"));
        }
        if (_entryProgress?.CanCancel != false)
        {
            Button back = MenuButton("Cancel");
            ConnectDeferred(back, CancelSessionLoading);
            column.AddChild(back);
            back.GrabFocus();
        }
    }

    private void ValidateShellSmoke()
    {
        GodotClientRootSnapshot snapshot = GetSnapshot();
        if (!snapshot.ControllerRootPresent || !snapshot.ContentCatalogRootPresent ||
            !snapshot.WorldRootPresent || !snapshot.WorldUiLayerPresent || !snapshot.HudLayerPresent ||
            !snapshot.MenuLayerPresent || !snapshot.DebugLayerPresent ||
            !snapshot.ContextLayerPresent || !snapshot.WorkspaceLayerPresent ||
            snapshot.Shell.Screen != ClientShellScreen.MainMenu ||
            snapshot.Shell.Focus.Owner != ClientInputFocusOwner.ModalMenu ||
            snapshot.Shell.Focus.GameplayInputAllowed ||
            snapshot.CapabilityCount != _composition.Capabilities.Count)
            throw new InvalidOperationException("The persistent client shell smoke contract failed.");
        RequireAdapterControl();
    }

    private async void RunShellUiSmoke()
    {
        try
        {
            Button settings = RequiredMenuButton("Capability_Settings");
            ValidateDeferredPress(settings);
            settings.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            ScrollContainer? scroll = _menuHost?.GetNodeOrNull<ScrollContainer>("SettingsScroll");
            if (_shell.Snapshot().Screen != ClientShellScreen.Settings || scroll is null ||
                !scroll.GetVScrollBar().Visible)
                throw new InvalidOperationException(
                    "The Settings button did not open a vertically scrollable Settings surface.");

            Button back = RequiredMenuButton("SettingsBack");
            ValidateDeferredPress(back);
            back.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_shell.Snapshot().Screen != ClientShellScreen.MainMenu)
                throw new InvalidOperationException("The Settings Back button did not restore the main menu.");

            if (_composition.Capabilities.Contains(ClientCapability.SinglePlayer))
            {
                Button singlePlayer = RequiredMenuButton("Capability_SinglePlayer");
                ValidateDeferredPress(singlePlayer);
                singlePlayer.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_shell.Snapshot().Screen is not (ClientShellScreen.Admission or
                        ClientShellScreen.InSession))
                    throw new InvalidOperationException(
                        "The Single Player button did not enter its installed local-play flow.");
                RetireCurrentSession();
                ReturnToMainMenu("Shell UI smoke passed.");
            }

            if (_composition.Capabilities.Contains(ClientCapability.Multiplayer))
            {
                Button multiplayer = RequiredMenuButton("Capability_Multiplayer");
                ValidateDeferredPress(multiplayer);
                multiplayer.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_shell.Snapshot().Screen != ClientShellScreen.Admission)
                    throw new InvalidOperationException(
                        "The Multiplayer button did not enter its Direct Connect admission flow.");
                Button connect = RequiredMenuButton("DirectConnectSubmit");
                ValidateDeferredPress(connect);
                RetireCurrentSession();
                ReturnToMainMenu("Shell UI smoke passed.");
            }

            GD.Print(
                $"F0.1 CLIENT SHELL UI PASS edition={_composition.Edition} " +
                "settings=scrollable transitions=deferred capabilities=admission");
            RequestApplicationExit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"Client shell UI smoke failed: {exception}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
    }

    private Button RequiredMenuButton(string name)
    {
        if (_menuHost?.FindChild(name, recursive: true, owned: false) is Button button)
            return button;
        throw new InvalidOperationException($"Required shell button '{name}' was not present.");
    }

    private static void ValidateDeferredPress(Button button)
    {
        foreach (global::Godot.Collections.Dictionary connection in
            button.GetSignalConnectionList(BaseButton.SignalName.Pressed))
        {
            if ((connection["flags"].AsUInt32() & (uint)GodotObject.ConnectFlags.Deferred) != 0)
                return;
        }
        throw new InvalidOperationException(
            $"Shell button '{button.Name}' can retire itself from a synchronous signal callback.");
    }

    private bool Matches(InputEventKey key, ClientSemanticAction action)
    {
        if (!_resolved.TryGetBinding(action, out ClientPhysicalInput input) ||
            input.Kind != ClientPhysicalInputKind.Keyboard)
            return false;
        ClientInputModifiers modifiers = ClientInputModifiers.None;
        if (key.ShiftPressed) modifiers |= ClientInputModifiers.Shift;
        if (key.CtrlPressed) modifiers |= ClientInputModifiers.Control;
        if (key.AltPressed) modifiers |= ClientInputModifiers.Alt;
        if (key.MetaPressed) modifiers |= ClientInputModifiers.Meta;
        return modifiers == input.Modifiers &&
            string.Equals(key.Keycode.ToString().ToUpperInvariant(), input.Code, StringComparison.Ordinal);
    }

    private void ResolveEffectiveSettings(bool notify = true)
    {
        _resolved = ClientSettingsResolver.Resolve(
            _settings,
            _activeSettingsScope,
            _activeProfileRecommendation,
            _activeProfileMode);
        if (notify) OnEffectiveSettingsChanged(_resolved);
    }

    protected VBoxContainer CreateCenteredColumn(float width = 360)
    {
        var background = new ColorRect
        {
            Color = new Color(0.035f, 0.05f, 0.045f, 1f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuHost!.AddChild(background);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuHost.AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);
        return column;
    }

    protected VBoxContainer CreateScrollableCenteredColumn(float width)
    {
        var background = new ColorRect
        {
            Color = new Color(0.035f, 0.05f, 0.045f, 1f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuHost!.AddChild(background);

        var scroll = new ScrollContainer
        {
            Name = "SettingsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuHost.AddChild(scroll);

        var margin = new MarginContainer
        {
            Name = "SettingsMargin",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        scroll.AddChild(margin);

        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);
        return column;
    }

    private static void AddSetting(VBoxContainer column, string label, Control control)
    {
        column.AddChild(new Label { Text = label });
        column.AddChild(control);
    }

    protected static Label Title(string text)
    {
        var result = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        result.AddThemeFontSizeOverride("font_size", 24);
        return result;
    }

    protected static Label CenteredLabel(string? text) => new()
    {
        Text = text ?? string.Empty,
        HorizontalAlignment = HorizontalAlignment.Center,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    protected static Button MenuButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(360, 42),
    };

    private static HSlider Slider(double minimum, double maximum, double step, double value) => new()
    {
        MinValue = minimum,
        MaxValue = maximum,
        Step = step,
        Value = value,
    };

    private static OptionButton EnumOptions<T>(T selected) where T : struct, Enum
    {
        var result = new OptionButton();
        int selectedIndex = 0;
        int index = 0;
        foreach (T value in Enum.GetValues<T>())
        {
            int id = Convert.ToInt32(value);
            result.AddItem(value.ToString(), id);
            if (EqualityComparer<T>.Default.Equals(value, selected)) selectedIndex = index;
            index++;
        }
        result.Select(selectedIndex);
        return result;
    }

    private static Node NamedNode(string name) => new() { Name = name };

    private static CanvasLayer Layer(string name, int layer) => new()
    {
        Name = name,
        Layer = layer,
    };

    private static Control FullRectControl(string name)
    {
        var result = new Control { Name = name };
        result.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return result;
    }

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.Free();
        }
    }

    protected void ConnectDeferred(Button button, Action pressed)
    {
        Error result = button.Connect(
            BaseButton.SignalName.Pressed,
            Callable.From(() =>
            {
                if (!CanStartApplicationWork) return;
                _ = _audioAdapter?.TryPlay(ClientSoundEvent.UiClick);
                pressed();
            }),
            (uint)GodotObject.ConnectFlags.Deferred);
        if (result != Error.Ok)
            throw new InvalidOperationException(
                $"Could not connect deferred button action for '{button.Name}': {result}.");
    }

    protected static void FocusFirstButton(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is not Button button) continue;
            button.GrabFocus();
            return;
        }
    }

    private static string Label(ClientCapability capability) => capability switch
    {
        ClientCapability.SinglePlayer => "Single Player",
        ClientCapability.Multiplayer => "Multiplayer / Direct Connect",
        ClientCapability.Settings => "Settings",
        ClientCapability.Exit => "Exit",
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };

    protected virtual string SessionPresentationDiagnostics() => "world renderer=inactive";

    protected virtual GodotClientPerformanceContext CapturePerformanceContext() => new(
        "none", _resolved.RenderDistanceChunks, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        _settings.ExperimentalVegetation,
        _display.GameplayOutputResolution.Width, _display.GameplayOutputResolution.Height);

    private string BuildDebugPage(GodotClientDebugPage page)
    {
        ClientShellSnapshot shell = ShellSnapshot();
        GodotClientUiCompositionSnapshot ui = UiComposition.GetSnapshot();
        GameplayFrameLayout frame = UiComposition.Layout;
        ClientCanonicalViewProjection projection =
            ClientCanonicalViewProjection.ForFrame(_resolved.CameraRig, frame);
        ClientCameraRig rig = _resolved.CameraRig;
        return page switch
        {
            GodotClientDebugPage.Client =>
                $"Client\nedition={_composition.Edition}\nscreen={shell.Screen} session={shell.SessionActive}\n" +
                $"focus={shell.Focus.Owner} gameplay={shell.Focus.GameplayInputAllowed}\n" +
                $"grammar={_resolved.ControlGrammar} camera={rig.Projection}/{rig.YawMode}/{rig.PitchMode}/{rig.Zoom}\n" +
                $"distance={rig.DefaultDistanceMillimeters / 1000f:0.###}m " +
                $"canonicalFov={rig.CanonicalFovTenths / 10f:0.#}° " +
                $"canonicalOrtho={rig.CanonicalOrthographicSizeMillimeters / 1000f:0.###}m",
            GodotClientDebugPage.Presentation =>
                $"Presentation\nphysicalWindow={GetWindow().Size.X}x{GetWindow().Size.Y} " +
                $"mode={_display.DisplayMode} settings={_display.SettingsMode}\n" +
                $"application={ui.Workspace.ApplicationSize.Width}x{ui.Workspace.ApplicationSize.Height}\n" +
                $"frame={ui.Workspace.GameplayFrame.X},{ui.Workspace.GameplayFrame.Y} " +
                $"{ui.Workspace.GameplayFrame.Width}x{ui.Workspace.GameplayFrame.Height}\n" +
                $"output={ui.Workspace.SelectedOutputShape.Width}x{ui.Workspace.SelectedOutputShape.Height} " +
                $"internal3D={ui.Workspace.Internal3DSize.Width}x{ui.Workspace.Internal3DSize.Height} " +
                $"scale={_display.InternalRenderScalePercent}%\n" +
                $"frameAspect={(double)frame.Frame.Width / frame.Frame.Height:0.####} " +
                $"canonicalCrop=16:9\n" +
                $"world/hud/gumps={ui.WorldHostChildren}/{ui.HudHostChildren}/{ui.Gumps.OpenWindows}\n" +
                $"canonical=16:9 targetV={projection.TargetVerticalExtent:0.####} " +
                $"targetH={projection.TargetHorizontalExtent:0.####}\n" + SessionPresentationDiagnostics(),
            GodotClientDebugPage.Content =>
                $"Content\ncatalogMarkers={ContentCatalogRoot.GetChildCount()}\n" +
                $"session={ui.SessionActive} context={ui.ContextHostChildren}\n" +
                "resolution=User→Realm→Local→Primitive",
            _ => string.Empty,
        };
    }

    private static void Validate(in ClientCompositionManifest composition)
    {
        if (string.IsNullOrWhiteSpace(composition.Edition) || composition.Capabilities is null ||
            composition.Capabilities.Count == 0 ||
            composition.Capabilities[^1] != ClientCapability.Exit ||
            composition.Capabilities.Distinct().Count() != composition.Capabilities.Count)
            throw new InvalidOperationException("The executable composition manifest is invalid.");
    }
}
