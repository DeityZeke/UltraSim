using Godot;
using UltraSim.Client;
using UltraSim.Client.UI;
using UltraSim.Discovery;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class MenuCanvasManager : CanvasLayer
{
    private const string ClientSettingsPath = "user://client_settings.cfg";
    private const string SavedServersPath = "user://saved_servers.json";
    private const string WorldUiSettingsSection = "world_ui";
    private const string ChatSettingsSection = "chat";

    private readonly ClientAppShellNavigation _navigation = new();
    private readonly Dictionary<string, Control> _serverDrivenPanels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiPanelMetadata> _serverDrivenPanelMetadata = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _actionMenus = new(StringComparer.Ordinal);
    private readonly Dictionary<UiSurfaceTarget, Control> _serverDrivenSurfaceHosts = new();
    private readonly HashSet<string> _blockingServerDrivenPanels = new(StringComparer.Ordinal);

    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? PreviewRequested;
    public event Action<string>? ChatSubmitted;
    public event Action<ClientUiActionRouteResult>? ServerDrivenActionRouted;
    public event Action<UiActionMenuDescriptor, UiActionDescriptor>? ActionMenuActionPressed;
    public event Action<LoginCredentialSubmitPacket>? LoginCredentialSubmitRequested;
    public event Action? AdmissionLogoutRequested;

    private ClientAppShell? _appShell;
    private ClientChatPanel? _chatPanel;
    private WorldCanvasManager? _worldCanvas;
    private ClientSavedServerList _savedServers = new();
    private ClientSavedServerListStore? _savedServerStore;
    private ClientServerBrowserService _serverBrowser = CreateDefaultServerBrowser(new ClientSavedServerList());
    private ClientSavedServerEntry? _pendingDirectConnectHistoryEntry;
    private long _nextServerDrivenActionSequence;
    private bool _showChatPanel = true;
    private bool _suppressSettingsSave;

    public bool IsMenuOpen => _appShell?.Visible == true;
    public bool HasServerDrivenPanels => _serverDrivenPanels.Count > 0;
    public bool BlocksGameplayInput => IsMenuOpen || _blockingServerDrivenPanels.Count > 0 || _chatPanel?.HasInputFocus == true;
    public ClientUiPanelRegistry? ServerDrivenPanelRegistry { get; set; }

    public override void _Ready()
    {
        LoadSavedServers();
        _appShell = GetNodeOrNull<ClientAppShell>("AppShell") ?? CreateAppShell();
        EnsureServerDrivenSurfaceHosts();
        _chatPanel = GetNodeOrNull<ClientChatPanel>("ChatPanel") ?? CreateChatPanel();
        _chatPanel.ChatSubmitted += text => ChatSubmitted?.Invoke(text);

        WireAppShell(_appShell);
        ApplyNavigation();
    }

    public void SetWorldCanvas(WorldCanvasManager? worldCanvas)
    {
        _worldCanvas = worldCanvas;
        LoadPresentationSettings();
        SyncAppShellWorldUiSettings();
        ApplyChatVisibility();
    }

    public bool TryMountServerDrivenPanel(ClientUiPanelInstance panel, out string error)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (panel.Metadata.SurfaceTarget is not (UiSurfaceTarget.Menu or UiSurfaceTarget.Modal or UiSurfaceTarget.Exclusive))
        {
            error = $"Panel surface '{panel.Metadata.SurfaceTarget}' does not belong on the menu canvas.";
            return false;
        }

        Control? surfaceHost = GetOrCreateServerDrivenSurfaceHost(panel.Metadata.SurfaceTarget);
        if (surfaceHost is null)
        {
            error = $"Server-driven surface host '{panel.Metadata.SurfaceTarget}' is not available.";
            return false;
        }

        if (!ServerDrivenPanelRenderer.TryBuild(
            panel,
            (action, contextId) => RouteServerDrivenAction(panel, action, contextId),
            out Control renderedPanel,
            out error))
        {
            return false;
        }

        ClosePanelsReplacedBy(panel);
        CloseMountedServerDrivenPanel(panel.PanelInstanceId);
        _serverDrivenPanels[panel.PanelInstanceId] = renderedPanel;
        _serverDrivenPanelMetadata[panel.PanelInstanceId] = panel.Metadata;
        if (PanelBlocksGameplayInput(panel.Metadata))
        {
            _blockingServerDrivenPanels.Add(panel.PanelInstanceId);
        }

        surfaceHost.AddChild(renderedPanel);
        ApplyServerPanelPlacement(renderedPanel, panel);
        WireTrustedCredentialSubmitShortcuts(panel, renderedPanel);
        ApplyChatVisibility();
        error = string.Empty;
        return true;
    }

    public bool CloseServerDrivenPanel(string panelInstanceId)
    {
        return CloseMountedServerDrivenPanel(panelInstanceId, closeRegistry: true);
    }

    private bool CloseMountedServerDrivenPanel(string panelInstanceId, bool closeRegistry = false)
    {
        if (!_serverDrivenPanels.Remove(panelInstanceId, out Control? renderedPanel))
        {
            return false;
        }

        _serverDrivenPanelMetadata.Remove(panelInstanceId);
        _blockingServerDrivenPanels.Remove(panelInstanceId);
        renderedPanel.QueueFree();
        if (closeRegistry)
        {
            ServerDrivenPanelRegistry?.ClosePanel(panelInstanceId);
        }

        ApplyChatVisibility();
        return true;
    }

    public void ClearServerDrivenPanels()
    {
        foreach (Control panel in _serverDrivenPanels.Values)
        {
            panel.QueueFree();
        }

        _serverDrivenPanels.Clear();
        _serverDrivenPanelMetadata.Clear();
        _blockingServerDrivenPanels.Clear();
        ServerDrivenPanelRegistry?.Clear();
        ClearActionMenus();
        ApplyChatVisibility();
    }

    public void ClearAdmissionPanels()
    {
        var admissionPanelIds = new List<string>();
        foreach (string panelId in _serverDrivenPanels.Keys)
        {
            if (panelId.StartsWith("admission/", StringComparison.Ordinal))
            {
                admissionPanelIds.Add(panelId);
            }
        }

        for (int i = 0; i < admissionPanelIds.Count; i++)
        {
            CloseMountedServerDrivenPanel(admissionPanelIds[i], closeRegistry: true);
        }
    }

    public bool TryShowActionMenu(UiActionMenuDescriptor menu, out string error)
    {
        ArgumentNullException.ThrowIfNull(menu);

        Control? surfaceHost = GetOrCreateServerDrivenSurfaceHost(UiSurfaceTarget.Modal);
        if (surfaceHost is null)
        {
            error = "Action menu surface host is not available.";
            return false;
        }

        if (!ActionMenuRenderer.TryBuild(
            menu,
            action => RouteActionMenuAction(menu, action),
            out Control renderedMenu,
            out error))
        {
            return false;
        }

        CloseActionMenu(menu.MenuKey);
        _actionMenus[menu.MenuKey] = renderedMenu;
        surfaceHost.AddChild(renderedMenu);
        renderedMenu.Position = menu.PresentationKind == UiActionMenuPresentationKind.RadialMenu
            ? new Vector2(320.0f, 160.0f)
            : new Vector2(280.0f, 140.0f);
        renderedMenu.ZIndex = 1000 + _actionMenus.Count;
        error = string.Empty;
        return true;
    }

    public bool CloseActionMenu(string menuKey)
    {
        if (!_actionMenus.Remove(menuKey, out Control? renderedMenu))
        {
            return false;
        }

        renderedMenu.QueueFree();
        return true;
    }

    public void ClearActionMenus()
    {
        foreach (Control menu in _actionMenus.Values)
        {
            menu.QueueFree();
        }

        _actionMenus.Clear();
    }

    public bool TryPressActionMenuButton(string menuKey, string actionId, out string error)
    {
        if (!_actionMenus.TryGetValue(menuKey, out Control? renderedMenu))
        {
            error = $"Action menu '{menuKey}' is not mounted.";
            return false;
        }

        Button? button = FindActionButton(renderedMenu, actionId);
        if (button is null)
        {
            error = $"Action menu '{menuKey}' does not contain action button '{actionId}'.";
            return false;
        }

        button.EmitSignal(BaseButton.SignalName.Pressed);
        error = string.Empty;
        return true;
    }

    public bool TryFindServerDrivenText(string panelInstanceId, string expectedText, out string error)
    {
        if (!_serverDrivenPanels.TryGetValue(panelInstanceId, out Control? renderedPanel))
        {
            error = $"Server-driven panel '{panelInstanceId}' is not mounted.";
            return false;
        }

        if (ContainsLabelText(renderedPanel, expectedText))
        {
            error = string.Empty;
            return true;
        }

        error = $"Server-driven panel '{panelInstanceId}' does not contain text '{expectedText}'.";
        return false;
    }

    public bool TryFindServerDrivenSlotControl(string panelInstanceId, string slotId, out string error)
    {
        if (!_serverDrivenPanels.TryGetValue(panelInstanceId, out Control? renderedPanel))
        {
            error = $"Server-driven panel '{panelInstanceId}' is not mounted.";
            return false;
        }

        if (ContainsSlotControl(renderedPanel, slotId))
        {
            error = string.Empty;
            return true;
        }

        error = $"Server-driven panel '{panelInstanceId}' does not contain slot control '{slotId}'.";
        return false;
    }

    public bool TryPressServerDrivenActionButton(string panelInstanceId, string actionId, out string error)
    {
        if (!_serverDrivenPanels.TryGetValue(panelInstanceId, out Control? renderedPanel))
        {
            error = $"Server-driven panel '{panelInstanceId}' is not mounted.";
            return false;
        }

        Button? button = FindActionButton(renderedPanel, actionId);
        if (button is null)
        {
            error = $"Server-driven panel '{panelInstanceId}' does not contain action button '{actionId}'.";
            return false;
        }

        button.EmitSignal(BaseButton.SignalName.Pressed);
        error = string.Empty;
        return true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_chatPanel?.HasInputFocus == true)
            {
                _chatPanel.ReleaseInputFocus();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_navigation.IsInSession)
            {
                ToggleInSessionMenu();
            }
            else
            {
                HandleBackRequested();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (ShouldFocusChat(@event))
        {
            _chatPanel?.FocusInput();
            GetViewport().SetInputAsHandled();
        }
    }

    public void EnterPreviewSession()
    {
        _navigation.EnterSession();
        ApplyNavigation();
    }

    public void EnterConnectedSession()
    {
        _navigation.EnterSession();
        ApplyNavigation();
    }

    public void ToggleInSessionMenu()
    {
        if (!_navigation.IsInSession)
        {
            _navigation.NavigateTo(ClientAppScreen.Start, pushCurrent: false);
            ApplyNavigation();
            return;
        }

        if (_navigation.IsMenuVisible)
        {
            _navigation.HideMenuIfInSession();
        }
        else
        {
            _navigation.ShowInSessionOptions();
        }

        ApplyNavigation();
    }

    private ClientAppShell CreateAppShell()
    {
        var appShell = new ClientAppShell
        {
            Name = "AppShell"
        };
        AddChild(appShell);
        return appShell;
    }

    private ClientChatPanel CreateChatPanel()
    {
        var chatPanel = new ClientChatPanel
        {
            Name = "ChatPanel",
            Visible = false
        };
        AddChild(chatPanel);
        return chatPanel;
    }

    private Control CreateServerDrivenSurfaceHost(UiSurfaceTarget surface)
    {
        var host = new Control
        {
            Name = $"ServerDriven{surface}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = GetServerDrivenSurfaceZIndex(surface),
        };
        AddChild(host);
        MoveChild(host, 0);
        PlaceFullScreen(host);
        return host;
    }

    private void WireAppShell(ClientAppShell appShell)
    {
        appShell.ScreenRequested += HandleScreenRequested;
        appShell.DirectConnectRequested += HandleDirectConnectRequested;
        appShell.ServerBrowserRowSubmitted += HandleServerBrowserRowSubmitted;
        appShell.ManualDirectConnectSubmitted += HandleManualDirectConnectSubmitted;
        appShell.ServerBrowserFavoriteRequested += HandleServerBrowserFavoriteRequested;
        appShell.ServerBrowserUnfavoriteRequested += HandleServerBrowserUnfavoriteRequested;
        appShell.ServerBrowserClearHistoryRequested += HandleServerBrowserClearHistoryRequested;
        appShell.ServerBrowserRefreshRequested += HandleServerBrowserRefreshRequested;
        appShell.ServerBrowserProviderRequested += HandleServerBrowserProviderRequested;
        appShell.PreviewRequested += HandlePreviewRequested;
        appShell.ResumeRequested += HandleResumeRequested;
        appShell.BackRequested += HandleBackRequested;
        appShell.NameplateVisibilityChanged += enabled =>
        {
            if (_worldCanvas is not null)
            {
                _worldCanvas.ShowNameplates = enabled;
                SavePresentationSettings();
            }
        };
        appShell.OverheadHealthBarVisibilityChanged += enabled =>
        {
            if (_worldCanvas is not null)
            {
                _worldCanvas.ShowOverheadHealthBars = enabled;
                SavePresentationSettings();
            }
        };
        appShell.ChatPanelVisibilityChanged += enabled =>
        {
            _showChatPanel = enabled;
            SavePresentationSettings();
            ApplyChatVisibility();
        };
        appShell.MaxVisibleWorldUiChanged += maxVisible =>
        {
            if (_worldCanvas is not null)
            {
                _worldCanvas.MaxVisibleWorldUi = maxVisible;
                SavePresentationSettings();
            }
        };
        appShell.WorldUiScaleChanged += scale =>
        {
            if (_worldCanvas is not null)
            {
                _worldCanvas.WorldUiScale = scale;
                SavePresentationSettings();
            }
        };
        appShell.OverheadChatVisibilityChanged += enabled =>
        {
            if (_worldCanvas is not null)
            {
                _worldCanvas.ShowOverheadChatBubbles = enabled;
                SavePresentationSettings();
            }
        };
    }

    public void ShowSessionStatus()
    {
        _navigation.EnterSession();
        _navigation.ShowInSessionOptions();
        ApplyNavigation();
    }

    public void ShowServerBrowser()
    {
        ClearServerDrivenPanels();
        _navigation.LeaveSession(ClientAppScreen.ServerBrowser);
        ApplyNavigation();
        _ = RefreshServerBrowserAsync();
    }

    public void ShowConnectionError(string? message = null)
    {
        ClearServerDrivenPanels();
        _navigation.LeaveSession(ClientAppScreen.Error);
        ApplyNavigation();
        if (!string.IsNullOrWhiteSpace(message))
        {
            _appShell?.ShowError(message);
        }
    }

    public void AppendSystemChatLine(string text)
    {
        _chatPanel?.AppendSystemLine(text);
    }

    public void AppendChatLine(string senderName, string text)
    {
        _chatPanel?.AppendChatLine(senderName, text);
    }

    private void HandleScreenRequested(ClientAppScreen screen)
    {
        if (screen == ClientAppScreen.Start)
        {
            _navigation.NavigateTo(ClientAppScreen.Start, pushCurrent: false);
        }
        else if (screen == ClientAppScreen.SessionStatus)
        {
            _navigation.EnterSession();
            _navigation.ShowInSessionOptions();
        }
        else if (screen == ClientAppScreen.Disconnected)
        {
            DisconnectRequested?.Invoke();
            _navigation.LeaveSession();
        }
        else
        {
            _navigation.NavigateTo(screen);
        }

        ApplyNavigation();

        if (screen == ClientAppScreen.ServerBrowser)
        {
            _ = RefreshServerBrowserAsync();
        }
    }

    private void HandleDirectConnectRequested(ClientDirectConnectRequest request)
    {
        RecordPendingHistory(request);
        DirectConnectRequested?.Invoke(request);
    }

    private void HandleServerBrowserRowSubmitted(ServerDiscoveryRow row)
    {
        _pendingDirectConnectHistoryEntry = ClientSavedServerEntry.FromRow(row);
    }

    private void HandleManualDirectConnectSubmitted(ClientDirectConnectRequest request)
    {
        string endpoint = $"{request.Host}:{request.Port}";
        _pendingDirectConnectHistoryEntry = new ClientSavedServerEntry(
            ServerId: endpoint,
            DisplayName: endpoint,
            Host: request.Host,
            Port: request.Port,
            LastSeenUtc: DateTimeOffset.UtcNow);
    }

    private void HandleServerBrowserFavoriteRequested(ServerDiscoveryRow row)
    {
        if (!_savedServers.UpsertFavorite(ClientSavedServerEntry.FromRow(row)))
            return;

        SaveSavedServers();
        _ = RefreshServerBrowserAsync();
    }

    private void HandleServerBrowserUnfavoriteRequested(ServerDiscoveryRow row)
    {
        if (!_savedServers.RemoveFavorite(row.Host, row.Port))
            return;

        SaveSavedServers();
        _ = RefreshServerBrowserAsync();
    }

    private void HandleServerBrowserClearHistoryRequested()
    {
        _savedServers.ClearHistory();
        SaveSavedServers();
        _ = RefreshServerBrowserAsync();
    }

    private void HandleServerBrowserRefreshRequested()
    {
        _ = RefreshServerBrowserAsync();
    }

    private void HandleServerBrowserProviderRequested(string providerId)
    {
        if (_serverBrowser.SelectProvider(providerId))
        {
            ApplyNavigation();
            _ = RefreshServerBrowserAsync();
        }
    }

    private void HandlePreviewRequested()
    {
        EnterPreviewSession();
        PreviewRequested?.Invoke();
    }

    private void HandleResumeRequested()
    {
        _navigation.HideMenuIfInSession();
        ApplyNavigation();
    }

    private void HandleBackRequested()
    {
        if (!_navigation.TryGoBack())
        {
            _navigation.NavigateTo(ClientAppScreen.Start, pushCurrent: false);
        }

        ApplyNavigation();
    }

    private void RouteServerDrivenAction(ClientUiPanelInstance panel, UiActionDescriptor action, string? contextId)
    {
        if (IsAdmissionLogoutAction(panel, action))
        {
            AdmissionLogoutRequested?.Invoke();
            return;
        }

        if (ServerDrivenPanelRegistry is null)
        {
            ServerDrivenActionRouted?.Invoke(ClientUiActionRouteResult.Rejected("Server-driven UI action registry is not attached."));
            return;
        }

        if (action.ContextKind == UiActionContextKind.Row && string.IsNullOrWhiteSpace(contextId))
        {
            ServerDrivenActionRouted?.Invoke(ClientUiActionRouteResult.Rejected($"Row action '{action.ActionId}' did not include a row context id."));
            return;
        }

        if (action.ContextKind == UiActionContextKind.TrustedCredential)
        {
            HandleTrustedCredentialAction(panel, action);
            return;
        }

        string? resolvedContextId = action.ContextKind == UiActionContextKind.Panel ? null : contextId;
        var request = new UiActionRequestDocument(
            panel.PanelInstanceId,
            panel.SchemaIdentity,
            panel.CatalogIdentity,
            ++_nextServerDrivenActionSequence,
            action.ActionId,
            action.ContextKind,
            resolvedContextId,
            FieldValues: CollectServerDrivenFieldValues(panel));
        ServerDrivenActionRouted?.Invoke(ServerDrivenPanelRegistry.ValidateActionRequest(request));
    }

    private static bool IsAdmissionLogoutAction(ClientUiPanelInstance panel, UiActionDescriptor action)
    {
        return
            (string.Equals(panel.SchemaIdentity.PanelType, "admission.login", StringComparison.Ordinal) &&
             string.Equals(action.ActionId, "login.cancel", StringComparison.Ordinal)) ||
            (string.Equals(panel.SchemaIdentity.PanelType, "admission.character_select", StringComparison.Ordinal) &&
             string.Equals(action.ActionId, "character_select.back", StringComparison.Ordinal));
    }

    private void HandleTrustedCredentialAction(ClientUiPanelInstance panel, UiActionDescriptor action)
    {
        if (!TryBuildLoginCredentialPacket(panel, action, out LoginCredentialSubmitPacket packet, out string error))
        {
            AppendSystemChatLine(error);
            GD.PushWarning(error);
            return;
        }

        if (LoginCredentialSubmitRequested is null)
        {
            Array.Clear(packet.MaterialBytes);
            AppendSystemChatLine("Login is not connected yet.");
            return;
        }

        LoginCredentialSubmitRequested.Invoke(packet);
    }

    private bool TryBuildLoginCredentialPacket(
        ClientUiPanelInstance panel,
        UiActionDescriptor action,
        out LoginCredentialSubmitPacket packet,
        out string error)
    {
        packet = default;
        if (!_serverDrivenPanels.TryGetValue(panel.PanelInstanceId, out Control? renderedPanel))
        {
            error = $"Login panel '{panel.PanelInstanceId}' is not mounted.";
            return false;
        }

        if (!TryReadLineEditSlot(renderedPanel, "login.username", out string username) &&
            !TryReadFirstStandardLineEdit(renderedPanel, out username))
        {
            error = "Login panel does not contain a username field.";
            return false;
        }

        if (!TryReadLineEditSlot(renderedPanel, "login.password", out string password) &&
            !TryReadTrustedLineEdit(renderedPanel, out password))
        {
            error = "Login panel does not contain a password field.";
            return false;
        }

        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            error = "Username and password are required.";
            return false;
        }

        packet = new LoginCredentialSubmitPacket(
            panel.PanelInstanceId,
            action.ActionId,
            username,
            LoginCredentialMaterialKind.PlainPassword,
            System.Text.Encoding.UTF8.GetBytes(password));
        error = string.Empty;
        return true;
    }

    private void RouteActionMenuAction(UiActionMenuDescriptor menu, UiActionDescriptor action)
    {
        ActionMenuActionPressed?.Invoke(menu, action);
        CloseActionMenu(menu.MenuKey);
    }

    private static void WireTrustedCredentialSubmitShortcuts(ClientUiPanelInstance panel, Control renderedPanel)
    {
        UiActionDescriptor? trustedAction = null;
        for (int i = 0; i < panel.Schema.Actions.Count; i++)
        {
            UiActionDescriptor action = panel.Schema.Actions[i];
            if (action.ContextKind == UiActionContextKind.TrustedCredential)
            {
                trustedAction = action;
                break;
            }
        }

        if (trustedAction is null)
        {
            return;
        }

        Button? button = FindActionButton(renderedPanel, trustedAction.ActionId);
        LineEdit? input = FindTrustedLineEdit(renderedPanel) ?? FindLineEditSlot(renderedPanel, "login.password");
        if (button is null || input is null)
        {
            return;
        }

        input.TextSubmitted += _ => button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void ApplyNavigation()
    {
        EnsureServerDrivenSurfaceHosts();

        if (_appShell is not null)
        {
            _appShell.Visible = _navigation.IsMenuVisible;
            SyncAppShellWorldUiSettings();
            _appShell.SetServerBrowserSnapshot(_serverBrowser.GetSnapshot());
            _appShell.ShowScreen(_navigation.CurrentScreen);
        }

        if (_chatPanel is not null)
        {
            ApplyChatVisibility();
        }
    }

    private void SyncAppShellWorldUiSettings()
    {
        if (_appShell is null || _worldCanvas is null)
        {
            return;
        }

        _appShell.SetPresentationSettings(
            _worldCanvas.ShowOverheadChatBubbles,
            _worldCanvas.ShowNameplates,
            _worldCanvas.ShowOverheadHealthBars,
            _showChatPanel,
            _worldCanvas.MaxVisibleWorldUi,
            _worldCanvas.WorldUiScale);
    }

    private void ApplyChatVisibility()
    {
        if (_chatPanel is null)
        {
            return;
        }

        bool shouldShowChat = _showChatPanel && _navigation.IsInSession && _blockingServerDrivenPanels.Count == 0;
        _chatPanel.Visible = shouldShowChat;
        if (!shouldShowChat && _chatPanel.HasInputFocus)
        {
            _chatPanel.ReleaseInputFocus();
        }
    }

    private void LoadPresentationSettings()
    {
        if (_worldCanvas is null)
        {
            return;
        }

        var config = new ConfigFile();
        if (config.Load(ClientSettingsPath) != Error.Ok)
        {
            return;
        }

        _suppressSettingsSave = true;
        _worldCanvas.ShowOverheadChatBubbles = (bool)config.GetValue(
            WorldUiSettingsSection,
            "show_overhead_chat",
            _worldCanvas.ShowOverheadChatBubbles);
        _worldCanvas.ShowNameplates = (bool)config.GetValue(
            WorldUiSettingsSection,
            "show_nameplates",
            _worldCanvas.ShowNameplates);
        _worldCanvas.ShowOverheadHealthBars = (bool)config.GetValue(
            WorldUiSettingsSection,
            "show_overhead_health_bars",
            _worldCanvas.ShowOverheadHealthBars);
        _worldCanvas.MaxVisibleWorldUi = Math.Clamp(
            (int)config.GetValue(WorldUiSettingsSection, "max_visible_world_ui", _worldCanvas.MaxVisibleWorldUi),
            1,
            256);
        _worldCanvas.WorldUiScale = Mathf.Clamp(
            (float)config.GetValue(WorldUiSettingsSection, "world_ui_scale", _worldCanvas.WorldUiScale),
            0.5f,
            3.0f);
        _showChatPanel = (bool)config.GetValue(ChatSettingsSection, "show_chat_panel", _showChatPanel);
        _suppressSettingsSave = false;
    }

    private void SavePresentationSettings()
    {
        if (_suppressSettingsSave || _worldCanvas is null)
        {
            return;
        }

        var config = new ConfigFile();
        _ = config.Load(ClientSettingsPath);
        config.SetValue(WorldUiSettingsSection, "show_overhead_chat", _worldCanvas.ShowOverheadChatBubbles);
        config.SetValue(WorldUiSettingsSection, "show_nameplates", _worldCanvas.ShowNameplates);
        config.SetValue(WorldUiSettingsSection, "show_overhead_health_bars", _worldCanvas.ShowOverheadHealthBars);
        config.SetValue(WorldUiSettingsSection, "max_visible_world_ui", _worldCanvas.MaxVisibleWorldUi);
        config.SetValue(WorldUiSettingsSection, "world_ui_scale", _worldCanvas.WorldUiScale);
        config.SetValue(ChatSettingsSection, "show_chat_panel", _showChatPanel);
        Error saved = config.Save(ClientSettingsPath);
        if (saved != Error.Ok)
        {
            GD.PushWarning($"UltraSim client: failed to save presentation settings ({saved}).");
        }
    }

    private void EnsureServerDrivenSurfaceHosts()
    {
        _ = GetOrCreateServerDrivenSurfaceHost(UiSurfaceTarget.Menu);
        _ = GetOrCreateServerDrivenSurfaceHost(UiSurfaceTarget.Modal);
        _ = GetOrCreateServerDrivenSurfaceHost(UiSurfaceTarget.Exclusive);
    }

    private Control? GetOrCreateServerDrivenSurfaceHost(UiSurfaceTarget surface)
    {
        if (surface is not (UiSurfaceTarget.Menu or UiSurfaceTarget.Modal or UiSurfaceTarget.Exclusive))
        {
            return null;
        }

        if (!_serverDrivenSurfaceHosts.TryGetValue(surface, out Control? host))
        {
            host = GetNodeOrNull<Control>($"ServerDriven{surface}") ?? CreateServerDrivenSurfaceHost(surface);
            _serverDrivenSurfaceHosts[surface] = host;
        }

        PlaceFullScreen(host);
        return host;
    }

    private void ApplyServerPanelPlacement(Control renderedPanel, ClientUiPanelInstance panel)
    {
        UiPanelMetadata metadata = panel.Metadata;
        renderedPanel.ZIndex = metadata.ZOrderClass;
        if (metadata.SurfaceTarget is UiSurfaceTarget.Modal or UiSurfaceTarget.Exclusive)
        {
            Vector2 preferredSize = ResolvePreferredServerPanelSize(renderedPanel, panel);
            renderedPanel.CustomMinimumSize = preferredSize;
            renderedPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            renderedPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            CenterServerPanel(renderedPanel, preferredSize);
            renderedPanel.Resized += () => CenterServerPanel(renderedPanel, preferredSize);
            return;
        }

        renderedPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        renderedPanel.Position = new Vector2(24.0f, 96.0f);
    }

    private void CenterServerPanel(Control panel, Vector2 preferredSize)
    {
        Vector2 minimumSize = panel.GetCombinedMinimumSize();
        Vector2 targetSize = new(
            Mathf.Max(preferredSize.X, minimumSize.X),
            preferredSize.Y > 0.0f ? preferredSize.Y : minimumSize.Y);

        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = targetSize.X * -0.5f;
        panel.OffsetRight = targetSize.X * 0.5f;
        panel.OffsetTop = targetSize.Y * -0.5f;
        panel.OffsetBottom = targetSize.Y * 0.5f;
    }

    private static Vector2 ResolvePreferredServerPanelSize(Control renderedPanel, ClientUiPanelInstance panel)
    {
        return panel.SchemaIdentity.PanelType switch
        {
            "admission.login" => new Vector2(560.0f, 280.0f),
            "admission.character_select" => new Vector2(560.0f, 360.0f),
            "admission.character_create" => new Vector2(560.0f, 400.0f),
            _ => new Vector2(Mathf.Max(renderedPanel.CustomMinimumSize.X, 560.0f), renderedPanel.CustomMinimumSize.Y),
        };
    }

    private void ClosePanelsReplacedBy(ClientUiPanelInstance panel)
    {
        if (panel.Metadata.OpenPolicy != UiOpenPolicy.ReplaceCurrent)
        {
            return;
        }

        var replacedPanelIds = new List<string>();
        foreach ((string panelId, UiPanelMetadata metadata) in _serverDrivenPanelMetadata)
        {
            if (string.Equals(panelId, panel.PanelInstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (ShouldReplaceMountedPanel(metadata, panel.Metadata))
            {
                replacedPanelIds.Add(panelId);
            }
        }

        for (int i = 0; i < replacedPanelIds.Count; i++)
        {
            CloseMountedServerDrivenPanel(replacedPanelIds[i], closeRegistry: true);
        }
    }

    private static bool ShouldReplaceMountedPanel(UiPanelMetadata mounted, UiPanelMetadata incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming.ExclusiveGroup) &&
            string.Equals(mounted.ExclusiveGroup, incoming.ExclusiveGroup, StringComparison.Ordinal))
        {
            return true;
        }

        return mounted.SurfaceTarget == incoming.SurfaceTarget &&
               incoming.FocusMode is UiFocusMode.Modal or UiFocusMode.Exclusive;
    }

    private static bool PanelBlocksGameplayInput(UiPanelMetadata metadata)
    {
        return metadata.BlocksGameplayInput || metadata.FocusMode is UiFocusMode.Normal or UiFocusMode.Modal or UiFocusMode.Exclusive;
    }

    private static int GetServerDrivenSurfaceZIndex(UiSurfaceTarget surface)
    {
        return surface switch
        {
            UiSurfaceTarget.Menu => 100,
            UiSurfaceTarget.Modal => 200,
            UiSurfaceTarget.Exclusive => 300,
            _ => 0,
        };
    }

    private static bool ContainsLabelText(Node root, string expectedText)
    {
        if (root is Label label && string.Equals(label.Text, expectedText, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (Node child in root.GetChildren())
        {
            if (ContainsLabelText(child, expectedText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSlotControl(Node root, string slotId)
    {
        if (root is Control control &&
            control.HasMeta("UiSlotId") &&
            string.Equals(control.GetMeta("UiSlotId").ToString(), slotId, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (Node child in root.GetChildren())
        {
            if (ContainsSlotControl(child, slotId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLineEditSlot(Node root, string slotId, out string text)
    {
        LineEdit? input = FindLineEditSlot(root, slotId);
        if (input is null)
        {
            text = string.Empty;
            return false;
        }

        text = input.Text ?? string.Empty;
        return true;
    }

    private static bool TryReadFirstStandardLineEdit(Node root, out string text)
    {
        LineEdit? input = FindFirstStandardLineEdit(root);
        if (input is null)
        {
            text = string.Empty;
            return false;
        }

        text = input.Text ?? string.Empty;
        return true;
    }

    private static bool TryReadTrustedLineEdit(Node root, out string text)
    {
        LineEdit? input = FindTrustedLineEdit(root);
        if (input is null)
        {
            text = string.Empty;
            return false;
        }

        text = input.Text ?? string.Empty;
        return true;
    }

    private static LineEdit? FindLineEditSlot(Node root, string slotId)
    {
        if (root is LineEdit input &&
            input.HasMeta("UiSlotId") &&
            string.Equals(input.GetMeta("UiSlotId").ToString(), slotId, StringComparison.Ordinal))
        {
            return input;
        }

        foreach (Node child in root.GetChildren())
        {
            LineEdit? found = FindLineEditSlot(child, slotId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static LineEdit? FindFirstStandardLineEdit(Node root)
    {
        if (root is LineEdit input &&
            (!input.HasMeta("UiTrustedInput") || !input.GetMeta("UiTrustedInput").AsBool()))
        {
            return input;
        }

        foreach (Node child in root.GetChildren())
        {
            LineEdit? found = FindFirstStandardLineEdit(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static LineEdit? FindTrustedLineEdit(Node root)
    {
        if (root is LineEdit input &&
            input.HasMeta("UiTrustedInput") &&
            input.GetMeta("UiTrustedInput").AsBool())
        {
            return input;
        }

        foreach (Node child in root.GetChildren())
        {
            LineEdit? found = FindTrustedLineEdit(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private IReadOnlyList<UiModelSlotValue> CollectServerDrivenFieldValues(ClientUiPanelInstance panel)
    {
        if (!_serverDrivenPanels.TryGetValue(panel.PanelInstanceId, out Control? renderedPanel))
        {
            return [];
        }

        var valuesBySlot = new Dictionary<string, UiModelSlotValue>(StringComparer.Ordinal);
        CollectFieldValues(renderedPanel, valuesBySlot);
        return valuesBySlot.Values.ToArray();
    }

    private static void CollectFieldValues(Node node, Dictionary<string, UiModelSlotValue> valuesBySlot)
    {
        if (node is Control control &&
            control.HasMeta("UiSlotId") &&
            control.GetMeta("UiSlotId").ToString() is { Length: > 0 } slotId)
        {
            UiModelSlotValue? value = TryReadControlValue(control, slotId);
            if (value is not null)
            {
                valuesBySlot[slotId] = value;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            CollectFieldValues(child, valuesBySlot);
        }
    }

    private static UiModelSlotValue? TryReadControlValue(Control control, string slotId)
    {
        if (control.HasMeta("UiTrustedInput") && control.GetMeta("UiTrustedInput").AsBool())
        {
            return null;
        }

        return control switch
        {
            LineEdit input => UiModelSlotValue.String(slotId, input.Text ?? string.Empty),
            OptionButton select => UiModelSlotValue.String(slotId, ReadSelectedOptionValue(select)),
            CheckBox checkbox when !checkbox.HasMeta("UiOptionValue") => UiModelSlotValue.Boolean(slotId, checkbox.ButtonPressed),
            SpinBox input => UiModelSlotValue.Number(slotId, input.Value),
            HSlider slider => UiModelSlotValue.Number(slotId, slider.Value),
            _ => TryReadOptionGroupValue(control, slotId)
        };
    }

    private static UiModelSlotValue? TryReadOptionGroupValue(Control control, string slotId)
    {
        var selected = new List<string>();
        bool sawGroupedOption = false;
        foreach (Node child in control.GetChildren())
        {
            if (child is not CheckBox checkbox || !checkbox.HasMeta("UiOptionValue"))
            {
                continue;
            }

            sawGroupedOption |= checkbox.ButtonGroup is not null;
            if (checkbox.ButtonPressed)
            {
                string? optionValue = checkbox.GetMeta("UiOptionValue").ToString();
                if (!string.IsNullOrWhiteSpace(optionValue))
                {
                    selected.Add(optionValue);
                }
            }
        }

        if (selected.Count == 0)
        {
            return null;
        }

        return sawGroupedOption
            ? UiModelSlotValue.String(slotId, selected[0])
            : UiModelSlotValue.StringList(slotId, selected);
    }

    private static string ReadSelectedOptionValue(OptionButton select)
    {
        int selected = select.Selected;
        if (selected >= 0)
        {
            Variant metadata = select.GetItemMetadata(selected);
            if (metadata.VariantType != Variant.Type.Nil)
            {
                return metadata.ToString();
            }
        }

        return string.Empty;
    }

    private static Button? FindActionButton(Node root, string actionId)
    {
        if (root is Button button &&
            button.HasMeta("UiActionId") &&
            string.Equals(button.GetMeta("UiActionId").ToString(), actionId, StringComparison.Ordinal))
        {
            return button;
        }

        foreach (Node child in root.GetChildren())
        {
            Button? found = FindActionButton(child, actionId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void PlaceFullScreen(Control control)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }

    private bool ShouldFocusChat(InputEvent @event)
    {
        if (!_navigation.IsInSession ||
            _navigation.IsMenuVisible ||
            _blockingServerDrivenPanels.Count > 0 ||
            _chatPanel?.Visible != true ||
            _chatPanel.HasInputFocus)
        {
            return false;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return false;
        }

        return key.Keycode is Key.Enter or Key.KpEnter || key.PhysicalKeycode is Key.Enter or Key.KpEnter;
    }

    private async Task RefreshServerBrowserAsync()
    {
        _appShell?.SetServerBrowserSnapshot(_serverBrowser.GetSnapshot());
        if (_navigation.IsMenuVisible && _navigation.CurrentScreen == ClientAppScreen.ServerBrowser)
        {
            ApplyNavigation();
        }

        ServerBrowserSnapshot snapshot = await _serverBrowser.RefreshAsync();
        _appShell?.SetServerBrowserSnapshot(snapshot);
        if (_navigation.IsMenuVisible && _navigation.CurrentScreen == ClientAppScreen.ServerBrowser)
        {
            ApplyNavigation();
        }
    }

    private void LoadSavedServers()
    {
        _savedServerStore = new ClientSavedServerListStore(ProjectSettings.GlobalizePath(SavedServersPath));
        try
        {
            _savedServers = _savedServerStore.LoadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to load saved server browser entries: {ex.Message}");
            _savedServers = new ClientSavedServerList();
        }

        _serverBrowser = CreateDefaultServerBrowser(_savedServers);
    }

    private void RecordPendingHistory(ClientDirectConnectRequest request)
    {
        ClientSavedServerEntry entry = _pendingDirectConnectHistoryEntry ?? new ClientSavedServerEntry(
            ServerId: $"{request.Host}:{request.Port}",
            DisplayName: $"{request.Host}:{request.Port}",
            Host: request.Host,
            Port: request.Port,
            LastSeenUtc: DateTimeOffset.UtcNow);
        _pendingDirectConnectHistoryEntry = null;

        if (!_savedServers.RecordHistory(entry with { LastSeenUtc = DateTimeOffset.UtcNow }))
            return;

        SaveSavedServers();
    }

    private void SaveSavedServers()
    {
        if (_savedServerStore is null)
            return;

        try
        {
            _savedServerStore.SaveAsync(_savedServers).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to save server browser history: {ex.Message}");
        }
    }

    private static ClientServerBrowserService CreateDefaultServerBrowser(ClientSavedServerList savedServers)
    {
        var localRow = new ServerDiscoveryRow(
            ProviderId: "manual",
            ProviderKind: ServerDiscoveryProviderKind.Manual,
            ServerId: "local-dev",
            DisplayName: "Local Dev Server",
            Host: "127.0.0.1",
            Port: 42042,
            Region: "local",
            CurrentUsers: 0,
            MaxUsers: 64,
            CompatibilityId: "current");

        ServerBrowserProviderDefinition officialProvider;
        try
        {
            officialProvider = ClientOfficialRegistryDiscoveryFactory.CreateProvider(
                new ClientOfficialRegistryDiscoveryOptions(ReadOfficialRegistryUrl()));
        }
        catch
        {
            officialProvider = new ServerBrowserProviderDefinition(
                "official",
                "Official",
                ServerDiscoveryProviderKind.Official,
                new InMemoryServerDiscoveryQuery([]));
        }

        return new ClientServerBrowserService(
        [
            savedServers.CreateFavoriteOverlayProvider(officialProvider),
            savedServers.CreateFavoritesProvider(),
            savedServers.CreateHistoryProvider(),
            new ServerBrowserProviderDefinition(
                "lan",
                "LAN",
                ServerDiscoveryProviderKind.Lan,
                new InMemoryServerDiscoveryQuery([])),
            new ServerBrowserProviderDefinition(
                "manual",
                "Manual",
                ServerDiscoveryProviderKind.Manual,
                new ClientSavedServerOverlayDiscoveryQuery(
                    savedServers,
                    new InMemoryServerDiscoveryQuery([localRow])))
        ],
        initialProviderId: "manual");
    }

    private static string ReadOfficialRegistryUrl()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--us-registry-url=", StringComparison.OrdinalIgnoreCase))
                continue;

            string value = arg["--us-registry-url=".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        string? environmentValue = System.Environment.GetEnvironmentVariable("ULTRASIM_REGISTRY_URL");
        return string.IsNullOrWhiteSpace(environmentValue)
            ? new ClientOfficialRegistryDiscoveryOptions().RegistryUrl
            : environmentValue.Trim();
    }
}
