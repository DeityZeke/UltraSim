using Godot;
using UltraSim.Client;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class MenuCanvasManager : CanvasLayer
{
    private readonly ClientAppShellNavigation _navigation = new();

    private ClientAppShell? _appShell;
    private ClientChatPanel? _chatPanel;

    public bool IsMenuOpen => _appShell?.Visible == true;
    public bool BlocksGameplayInput => IsMenuOpen || _chatPanel?.HasInputFocus == true;

    public override void _Ready()
    {
        _appShell = GetNodeOrNull<ClientAppShell>("AppShell") ?? CreateAppShell();
        _chatPanel = GetNodeOrNull<ClientChatPanel>("ChatPanel") ?? CreateChatPanel();

        WireAppShell(_appShell);
        ApplyNavigation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            ToggleInSessionMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    public void EnterPreviewSession()
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
        appShell.PreviewRequested += EnterPreviewSession;
        appShell.BackRequested += HandleBackRequested;
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
            _navigation.LeaveSession();
        }
        else
        {
            _navigation.NavigateTo(screen);
        }

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
            _appShell.ShowScreen(_navigation.CurrentScreen);
        }

        if (_chatPanel is not null)
        {
            _chatPanel.Visible = _navigation.IsInSession;
        }
    }
}
