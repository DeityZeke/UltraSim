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
    private readonly Dictionary<UiSurfaceTarget, Control> _serverDrivenSurfaceHosts = new();
    private readonly HashSet<string> _blockingServerDrivenPanels = new(StringComparer.Ordinal);

    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? PreviewRequested;
    public event Action<string>? ChatSubmitted;

    private ClientAppShell? _appShell;
    private ClientChatPanel? _chatPanel;

    public bool IsMenuOpen => _appShell?.Visible == true;
    public bool HasServerDrivenPanels => _serverDrivenPanels.Count > 0;
    public bool BlocksGameplayInput => IsMenuOpen || _blockingServerDrivenPanels.Count > 0 || _chatPanel?.HasInputFocus == true;

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

        if (!ServerDrivenPanelRenderer.TryBuild(panel, out Control renderedPanel, out error))
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
