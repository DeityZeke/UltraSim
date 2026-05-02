using Godot;
using UltraSim.Client;
using UltraSim.Client.UI;
using UltraSim.Discovery;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class MenuCanvasManager : CanvasLayer
{
    private readonly ClientAppShellNavigation _navigation = new();
    private readonly ClientServerBrowserService _serverBrowser = CreateDefaultServerBrowser();
    private readonly Dictionary<string, Control> _serverDrivenPanels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _actionMenus = new(StringComparer.Ordinal);
    private readonly Dictionary<UiSurfaceTarget, Control> _serverDrivenSurfaceHosts = new();
    private readonly HashSet<string> _blockingServerDrivenPanels = new(StringComparer.Ordinal);

    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? PreviewRequested;
    public event Action<string>? ChatSubmitted;
    public event Action<ClientUiActionRouteResult>? ServerDrivenActionRouted;
    public event Action<UiActionMenuDescriptor, UiActionDescriptor>? ActionMenuActionPressed;
    public event Action<ClientUiPanelInstance, UiActionDescriptor>? TrustedCredentialActionRequested;

    private ClientAppShell? _appShell;
    private ClientChatPanel? _chatPanel;
    private long _nextServerDrivenActionSequence;

    public bool IsMenuOpen => _appShell?.Visible == true;
    public bool HasServerDrivenPanels => _serverDrivenPanels.Count > 0;
    public bool BlocksGameplayInput => IsMenuOpen || _blockingServerDrivenPanels.Count > 0 || _chatPanel?.HasInputFocus == true;
    public ClientUiPanelRegistry? ServerDrivenPanelRegistry { get; set; }

    public override void _Ready()
    {
        _appShell = GetNodeOrNull<ClientAppShell>("AppShell") ?? CreateAppShell();
        EnsureServerDrivenSurfaceHosts();
        _chatPanel = GetNodeOrNull<ClientChatPanel>("ChatPanel") ?? CreateChatPanel();
        _chatPanel.ChatSubmitted += text => ChatSubmitted?.Invoke(text);

        WireAppShell(_appShell);
        ApplyNavigation();
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

        CloseServerDrivenPanel(panel.PanelInstanceId);
        _serverDrivenPanels[panel.PanelInstanceId] = renderedPanel;
        if (PanelBlocksGameplayInput(panel.Metadata))
        {
            _blockingServerDrivenPanels.Add(panel.PanelInstanceId);
        }

        surfaceHost.AddChild(renderedPanel);
        ApplyServerPanelPlacement(renderedPanel, panel.Metadata);
        error = string.Empty;
        return true;
    }

    public bool CloseServerDrivenPanel(string panelInstanceId)
    {
        if (!_serverDrivenPanels.Remove(panelInstanceId, out Control? renderedPanel))
        {
            return false;
        }

        _blockingServerDrivenPanels.Remove(panelInstanceId);
        renderedPanel.QueueFree();
        return true;
    }

    public void ClearServerDrivenPanels()
    {
        foreach (Control panel in _serverDrivenPanels.Values)
        {
            panel.QueueFree();
        }

        _serverDrivenPanels.Clear();
        _blockingServerDrivenPanels.Clear();
        ClearActionMenus();
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

            ToggleInSessionMenu();
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
        appShell.ServerBrowserRefreshRequested += HandleServerBrowserRefreshRequested;
        appShell.PreviewRequested += HandlePreviewRequested;
        appShell.ResumeRequested += HandleResumeRequested;
        appShell.BackRequested += HandleBackRequested;
    }

    public void ShowSessionStatus()
    {
        _navigation.EnterSession();
        _navigation.ShowInSessionOptions();
        ApplyNavigation();
    }

    public void ShowConnectionError(string? message = null)
    {
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
        DirectConnectRequested?.Invoke(request);
    }

    private void HandleServerBrowserRefreshRequested()
    {
        _ = RefreshServerBrowserAsync();
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
            TrustedCredentialActionRequested?.Invoke(panel, action);
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
            FieldValues: []);
        ServerDrivenActionRouted?.Invoke(ServerDrivenPanelRegistry.ValidateActionRequest(request));
    }

    private void RouteActionMenuAction(UiActionMenuDescriptor menu, UiActionDescriptor action)
    {
        ActionMenuActionPressed?.Invoke(menu, action);
        CloseActionMenu(menu.MenuKey);
    }

    private void ApplyNavigation()
    {
        EnsureServerDrivenSurfaceHosts();

        if (_appShell is not null)
        {
            _appShell.Visible = _navigation.IsMenuVisible;
            _appShell.SetServerBrowserSnapshot(_serverBrowser.GetSnapshot());
            _appShell.ShowScreen(_navigation.CurrentScreen);
        }

        if (_chatPanel is not null)
        {
            _chatPanel.Visible = _navigation.IsInSession;
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

    private static void ApplyServerPanelPlacement(Control panel, UiPanelMetadata metadata)
    {
        panel.Position = new Vector2(24.0f + metadata.ZOrderClass * 8.0f, 96.0f + metadata.ZOrderClass * 8.0f);
        panel.ZIndex = metadata.ZOrderClass;
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
        if (!_navigation.IsInSession || _navigation.IsMenuVisible || _chatPanel?.HasInputFocus == true)
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
        ServerBrowserSnapshot snapshot = await _serverBrowser.RefreshAsync();
        _appShell?.SetServerBrowserSnapshot(snapshot);
    }

    private static ClientServerBrowserService CreateDefaultServerBrowser()
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

        return new ClientServerBrowserService(
        [
            new ServerBrowserProviderDefinition(
                "manual",
                "Manual",
                ServerDiscoveryProviderKind.Manual,
                new InMemoryServerDiscoveryQuery([localRow]))
        ]);
    }
}
