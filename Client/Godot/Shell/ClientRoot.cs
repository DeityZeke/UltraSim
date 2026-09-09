using Godot;
using UltraSim.Client.Core;

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
    bool DebugLayerPresent);

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
    int DebugChildren,
    bool ServerProfileActive,
    ClientControlGrammar ControlGrammar,
    ClientCameraProfile CameraProfile,
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
    private Control? _menuHost;
    private Label? _hudStatus;
    private Label? _debugStatus;

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

    protected ResolvedClientSettings EffectiveSettings => _resolved;

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
        return new GodotClientSessionBoundarySnapshot(
            ControllerRoot.GetChildCount(),
            ContentCatalogRoot.GetChildCount(),
            WorldRoot.GetChildCount(),
            WorldUiLayer.GetChildCount(),
            HudLayer.GetChildCount(),
            _debugLayer?.GetChildCount() ?? 0,
            _activeSettingsScope.IsValid || _activeProfileRecommendation is not null ||
                _activeProfileMode != ClientProfileApplicationMode.Ignore,
            _resolved.ControlGrammar,
            _resolved.CameraProfile,
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
            current.DebugChildren != baseline.DebugChildren ||
            current.ServerProfileActive ||
            current.ControlGrammar != baseline.ControlGrammar ||
            current.CameraProfile != baseline.CameraProfile ||
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

    protected void BeginSessionLoading(string status)
    {
        _shell.BeginLoading(status);
        RenderCurrentSurface();
    }

    protected void BeginAdmission(string status)
    {
        _shell.BeginAdmission(status);
        RenderCurrentSurface();
    }

    protected void CommitSession(string status)
    {
        _shell.EnterSession(status);
        RenderCurrentSurface();
    }

    protected void FailSession(string message)
    {
        _shell.Fail(message);
        RenderCurrentSurface();
    }

    protected void ReturnToMainMenu(string? status = null)
    {
        _shell.ReturnToMainMenu(status);
        RenderCurrentSurface();
    }

    public override void _Ready()
    {
        try
        {
            _composition = Composition;
            Validate(_composition);
            LoadSettings();
            BuildPersistentRoots();
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
            if (OS.GetCmdlineUserArgs().Contains("--composition-smoke", StringComparer.Ordinal))
            {
                ValidateShellSmoke();
                GD.Print(
                    $"F0.1 CLIENT SHELL SMOKE PASS edition={_composition.Edition} " +
                    $"capabilities={capabilities} layers=World,HUD,Menus,Debug");
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Client shell initialization failed: {exception}");
            GetTree().Quit(1);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (Matches(key, ClientSemanticAction.ExitApplication))
        {
            GetTree().Quit(0);
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
            _debugLayer is not null);
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
    protected virtual void OnEffectiveSettingsChanged(ResolvedClientSettings settings) { }

    private void LoadSettings()
    {
        string path = ProjectSettings.GlobalizePath("user://settings/client.json");
        _settingsStore = new ClientSettingsFileStore(path);
        ClientSettingsLoadResult loaded = _settingsStore.Load();
        _settingsLoadStatus = loaded.Status;
        _settings = loaded.Document;
        ResolveEffectiveSettings(notify: false);
    }

    private void BuildPersistentRoots()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _controllerRoot = NamedNode("Controller");
        _contentCatalogRoot = NamedNode("ContentCatalogs");
        _worldRoot = NamedNode("World");
        AddChild(_controllerRoot);
        AddChild(_contentCatalogRoot);
        AddChild(_worldRoot);

        Node map = NamedNode("Map");
        map.AddChild(NamedNode("Terrain"));
        map.AddChild(NamedNode("FrozenStatics"));
        map.AddChild(NamedNode("Environment"));
        _worldRoot.AddChild(map);
        _worldRoot.AddChild(NamedNode("DynamicEntities"));
        _worldRoot.AddChild(NamedNode("Effects"));
        _worldRoot.AddChild(NamedNode("CameraRig"));

        Node layers = NamedNode("UILayers");
        AddChild(layers);
        _worldUiLayer = Layer("World", 10);
        _hudLayer = Layer("HUD", 20);
        _menuLayer = Layer("Menus", 30);
        _debugLayer = Layer("Debug", 40);
        layers.AddChild(_worldUiLayer);
        layers.AddChild(_hudLayer);
        layers.AddChild(_menuLayer);
        layers.AddChild(_debugLayer);

        _menuHost = FullRectControl("MenuSurface");
        _menuLayer.AddChild(_menuHost);
        _hudStatus = new Label { Name = "SessionStatus", Visible = false };
        _hudStatus.SetAnchorsPreset(LayoutPreset.TopWide);
        _hudLayer.AddChild(_hudStatus);
        _debugStatus = new Label
        {
            Name = "Diagnostics",
            Visible = false,
            Text = "Client diagnostics",
            Position = new Vector2(12, 44),
        };
        _debugLayer.AddChild(_debugStatus);
    }

    protected void RenderCurrentSurface()
    {
        if (_menuHost is null) return;
        ClearChildren(_menuHost);
        ClientShellSnapshot snapshot = _shell.Snapshot();
        _menuHost.Visible = snapshot.Screen != ClientShellScreen.InSession;
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
                BuildMessageSurface("Loading", snapshot.Status ?? "Preparing client session…", "Back", () =>
                {
                    RetireCurrentSession();
                    _shell.ReturnToMainMenu();
                    RenderCurrentSurface();
                });
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
                    ConnectDeferred(button, () => GetTree().Quit(0));
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
        VBoxContainer column = CreateScrollableCenteredColumn(420);
        bool scoped = _activeSettingsScope.IsValid && _activeProfileRecommendation is not null;
        column.AddChild(Title(scoped ? "Server-Specific Settings" : "Client Settings"));
        if (scoped)
            column.AddChild(CenteredLabel(
                "These user-owned overrides apply only to the verified application realm. " +
                "The server recommendation is not copied into global settings."));

        HSlider uiScale = Slider(0.5, 3, 0.05, _resolved.UiScale);
        HSlider volume = Slider(0, 1, 0.05, _resolved.MasterVolume);
        HSlider sensitivity = Slider(0.1, 5, 0.1, _resolved.PointerSensitivity);
        CheckBox invert = new() { Text = "Invert camera Y", ButtonPressed = _resolved.InvertY };
        OptionButton grammar = EnumOptions(_resolved.ControlGrammar);
        OptionButton camera = EnumOptions(_resolved.CameraProfile);
        HSlider distance = Slider(2, 80, 1, _resolved.CameraDistance);
        HSlider fov = Slider(40, 120, 1, _resolved.CameraFovDegrees);
        HSlider renderDistance = Slider(
            ClientSettingsResolver.MinimumRenderDistanceChunks,
            ClientSettingsResolver.MaximumRenderDistanceChunks,
            1,
            _resolved.RenderDistanceChunks);
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

        Button save = MenuButton("Save");
        save.Name = "SettingsSave";
        ConnectDeferred(save, () =>
        {
            try
            {
                ClientSettingsLayer saved = new(
                    (float)uiScale.Value,
                    (float)volume.Value,
                    (float)sensitivity.Value,
                    invert.ButtonPressed,
                    (ClientControlGrammar)grammar.GetSelectedId(),
                    (ClientCameraProfile)camera.GetSelectedId(),
                    (float)distance.Value,
                    (float)fov.Value,
                    scoped && _settings.TryGetScope(_activeSettingsScope, out ClientSettingsLayer existing)
                        ? existing.Bindings
                        : _settings.Global.Bindings,
                    (int)renderDistance.Value);
                _settings = scoped
                    ? _settings.WithScope(_activeSettingsScope, saved)
                    : _settings.WithGlobal(saved);
                _settingsStore!.Save(_settings);
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
            _settings = scoped
                ? _settings.WithScope(_activeSettingsScope, ClientSettingsLayer.Empty)
                : new ClientSettingsDocument(ClientSettingsLayer.Empty, _settings.Scopes);
            ResolveEffectiveSettings();
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

    private void ValidateShellSmoke()
    {
        GodotClientRootSnapshot snapshot = GetSnapshot();
        if (!snapshot.ControllerRootPresent || !snapshot.ContentCatalogRootPresent ||
            !snapshot.WorldRootPresent || !snapshot.WorldUiLayerPresent || !snapshot.HudLayerPresent ||
            !snapshot.MenuLayerPresent || !snapshot.DebugLayerPresent ||
            snapshot.Shell.Screen != ClientShellScreen.MainMenu ||
            snapshot.Shell.Focus.Owner != ClientInputFocusOwner.ModalMenu ||
            snapshot.Shell.Focus.GameplayInputAllowed ||
            snapshot.CapabilityCount != _composition.Capabilities.Count)
            throw new InvalidOperationException("The persistent client shell smoke contract failed.");
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
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"Client shell UI smoke failed: {exception}");
            RetireCurrentSession();
            GetTree().Quit(1);
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

    protected static void ConnectDeferred(Button button, Action pressed)
    {
        Error result = button.Connect(
            BaseButton.SignalName.Pressed,
            Callable.From(pressed),
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

    private static void Validate(in ClientCompositionManifest composition)
    {
        if (string.IsNullOrWhiteSpace(composition.Edition) || composition.Capabilities is null ||
            composition.Capabilities.Count == 0 ||
            composition.Capabilities[^1] != ClientCapability.Exit ||
            composition.Capabilities.Distinct().Count() != composition.Capabilities.Count)
            throw new InvalidOperationException("The executable composition manifest is invalid.");
    }
}
