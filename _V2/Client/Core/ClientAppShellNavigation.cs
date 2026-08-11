namespace UltraSim.Client;

public sealed class ClientAppShellNavigation
{
    private readonly Stack<ClientAppScreen> _backStack = new();

    public ClientAppScreen CurrentScreen { get; private set; } = ClientAppScreen.Start;
    public bool IsInSession { get; private set; }
    public bool IsMenuVisible { get; private set; } = true;
    public int BackStackCount => _backStack.Count;

    public void NavigateTo(ClientAppScreen screen, bool pushCurrent = true)
    {
        if (screen == CurrentScreen)
        {
            IsMenuVisible = true;
            return;
        }

        if (pushCurrent)
        {
            _backStack.Push(CurrentScreen);
        }

        CurrentScreen = screen;
        IsMenuVisible = true;
    }

    public bool TryGoBack()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        CurrentScreen = _backStack.Pop();
        IsMenuVisible = true;
        return true;
    }

    public void EnterSession()
    {
        IsInSession = true;
        IsMenuVisible = false;
        _backStack.Clear();
        CurrentScreen = ClientAppScreen.SessionStatus;
    }

    public void ShowInSessionOptions()
    {
        if (!IsInSession)
        {
            NavigateTo(ClientAppScreen.Start, pushCurrent: false);
            return;
        }

        _backStack.Clear();
        CurrentScreen = ClientAppScreen.InSessionOptions;
        IsMenuVisible = true;
    }

    public void HideMenuIfInSession()
    {
        if (IsInSession)
        {
            IsMenuVisible = false;
        }
    }

    public void LeaveSession(ClientAppScreen destination = ClientAppScreen.Disconnected)
    {
        IsInSession = false;
        IsMenuVisible = true;
        _backStack.Clear();
        CurrentScreen = destination;
    }
}
