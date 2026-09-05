namespace UltraSim.Client.Core;

public enum ClientShellScreen : byte
{
    MainMenu = 1,
    Loading = 2,
    InSession = 3,
    InSessionOptions = 4,
    Settings = 5,
    Error = 6,
    Admission = 7,
}

public enum ClientInputFocusOwner : byte
{
    ApplicationMenu = 1,
    World = 2,
    HudText = 3,
    ModalMenu = 4,
    DebugControl = 5,
}

public readonly record struct ClientInputFocusRequest(
    bool SessionActive,
    bool TextEntryFocused,
    bool ModalMenuOpen,
    bool DebugControlFocused,
    bool ExplicitGameplayBlock);

public readonly record struct ClientInputFocusSnapshot(
    ClientInputFocusOwner Owner,
    bool GameplayInputAllowed,
    bool WorldInteractionAllowed,
    bool TextInputActive,
    bool DebugInputActive,
    bool ReservedClientControlsAvailable);

/// <summary>One engine-neutral focus decision shared by every Godot input consumer.</summary>
public static class ClientInputFocusPolicy
{
    public static ClientInputFocusSnapshot Resolve(in ClientInputFocusRequest request)
    {
        ClientInputFocusOwner owner;
        if (request.ModalMenuOpen) owner = ClientInputFocusOwner.ModalMenu;
        else if (request.TextEntryFocused) owner = ClientInputFocusOwner.HudText;
        else if (request.DebugControlFocused) owner = ClientInputFocusOwner.DebugControl;
        else if (request.SessionActive) owner = ClientInputFocusOwner.World;
        else owner = ClientInputFocusOwner.ApplicationMenu;

        bool gameplay = request.SessionActive && owner == ClientInputFocusOwner.World &&
            !request.ExplicitGameplayBlock;
        return new ClientInputFocusSnapshot(
            owner,
            gameplay,
            gameplay,
            owner == ClientInputFocusOwner.HudText,
            owner == ClientInputFocusOwner.DebugControl,
            ReservedClientControlsAvailable: true);
    }
}

public readonly record struct ClientShellSnapshot(
    ClientShellScreen Screen,
    bool SessionActive,
    ulong TransitionSequence,
    string? Status,
    string? Error,
    ClientInputFocusSnapshot Focus);

/// <summary>
/// Owner-thread, engine-neutral application-shell lifecycle. It owns no Game,
/// Network, renderer, or platform settings storage.
/// </summary>
public sealed class ClientShellCoordinator
{
    public const int MaximumMessageCharacters = 1_024;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ClientShellScreen _screen = ClientShellScreen.MainMenu;
    private ClientShellScreen _settingsReturn = ClientShellScreen.MainMenu;
    private bool _sessionActive;
    private ulong _transitionSequence;
    private string? _status;
    private string? _error;

    public ClientShellSnapshot Snapshot(bool textEntryFocused = false, bool debugControlFocused = false)
    {
        EnsureOwner();
        bool modal = _screen is ClientShellScreen.MainMenu or ClientShellScreen.Admission or
            ClientShellScreen.Loading or
            ClientShellScreen.InSessionOptions or ClientShellScreen.Settings or ClientShellScreen.Error;
        return new ClientShellSnapshot(
            _screen,
            _sessionActive,
            _transitionSequence,
            _status,
            _error,
            ClientInputFocusPolicy.Resolve(new ClientInputFocusRequest(
                _sessionActive, textEntryFocused, modal, debugControlFocused,
                _screen == ClientShellScreen.Loading)));
    }

    public void BeginLoading(string status)
    {
        EnsureOwner();
        if (_sessionActive) throw new InvalidOperationException("An active client session must be retired first.");
        _status = ValidateMessage(status, nameof(status));
        _error = null;
        Transition(ClientShellScreen.Loading);
    }

    public void BeginAdmission(string status)
    {
        EnsureOwner();
        if (_sessionActive)
            throw new InvalidOperationException("An active client session cannot enter admission.");
        _status = ValidateMessage(status, nameof(status));
        _error = null;
        Transition(ClientShellScreen.Admission);
    }

    public void EnterSession(string status)
    {
        EnsureOwner();
        if (_screen != ClientShellScreen.Loading)
            throw new InvalidOperationException("A client session can enter only from loading.");
        _sessionActive = true;
        _status = ValidateMessage(status, nameof(status));
        _error = null;
        Transition(ClientShellScreen.InSession);
    }

    public void OpenSettings()
    {
        EnsureOwner();
        if (_screen is ClientShellScreen.Admission or ClientShellScreen.Loading or
            ClientShellScreen.Error)
            throw new InvalidOperationException("Settings are unavailable during a terminal transition surface.");
        _settingsReturn = _sessionActive ? ClientShellScreen.InSessionOptions : ClientShellScreen.MainMenu;
        Transition(ClientShellScreen.Settings);
    }

    public void CloseSettings()
    {
        EnsureOwner();
        if (_screen != ClientShellScreen.Settings)
            throw new InvalidOperationException("Settings are not open.");
        Transition(_settingsReturn);
    }

    public void OpenInSessionOptions()
    {
        EnsureOwner();
        if (!_sessionActive || _screen != ClientShellScreen.InSession)
            throw new InvalidOperationException("There is no active world session to pause locally.");
        Transition(ClientShellScreen.InSessionOptions);
    }

    public void ResumeSession()
    {
        EnsureOwner();
        if (!_sessionActive || _screen != ClientShellScreen.InSessionOptions)
            throw new InvalidOperationException("In-session options are not open.");
        Transition(ClientShellScreen.InSession);
    }

    public void Fail(string message)
    {
        EnsureOwner();
        _error = ValidateMessage(message, nameof(message));
        _status = null;
        Transition(ClientShellScreen.Error);
    }

    public void ReturnToMainMenu(string? status = null)
    {
        EnsureOwner();
        if (status is not null) _status = ValidateMessage(status, nameof(status));
        _error = null;
        _sessionActive = false;
        _settingsReturn = ClientShellScreen.MainMenu;
        Transition(ClientShellScreen.MainMenu);
    }

    private void Transition(ClientShellScreen screen)
    {
        _screen = screen;
        _transitionSequence = checked(_transitionSequence + 1);
    }

    private static string ValidateMessage(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > MaximumMessageCharacters) throw new ArgumentOutOfRangeException(parameter);
        return value;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Client shell lifecycle is owner-thread bound.");
    }
}
