using Godot;
using UltraSim.Client;
using UltraSim.Discovery;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class MenuCanvasManager : CanvasLayer
{
    private readonly ClientAppShellNavigation _navigation = new();
    private readonly ClientServerBrowserService _serverBrowser = CreateDefaultServerBrowser();

    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? PreviewRequested;
    public event Action<string>? ChatSubmitted;

    private ClientAppShell? _appShell;
    private ClientChatPanel? _chatPanel;

    public bool IsMenuOpen => _appShell?.Visible == true;
    public bool BlocksGameplayInput => IsMenuOpen || _chatPanel?.HasInputFocus == true;

    public override void _Ready()
    {
        _appShell = GetNodeOrNull<ClientAppShell>("AppShell") ?? CreateAppShell();
        _chatPanel = GetNodeOrNull<ClientChatPanel>("ChatPanel") ?? CreateChatPanel();
        _chatPanel.ChatSubmitted += text => ChatSubmitted?.Invoke(text);

        WireAppShell(_appShell);
        ApplyNavigation();
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
