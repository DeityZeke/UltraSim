using UltraSim.Client.Core;

static class ClientShellStateTests
{
    internal static void Run()
    {
        var shell = new ClientShellCoordinator();
        ClientShellSnapshot initial = shell.Snapshot();
        Equal(ClientShellScreen.MainMenu, initial.Screen);
        Equal(ClientInputFocusOwner.ModalMenu, initial.Focus.Owner);
        True(!initial.Focus.GameplayInputAllowed);
        True(initial.Focus.ReservedClientControlsAvailable);

        shell.OpenSettings();
        Equal(ClientShellScreen.Settings, shell.Snapshot().Screen);
        shell.CloseSettings();
        Equal(ClientShellScreen.MainMenu, shell.Snapshot().Screen);

        shell.BeginAdmission("Choose a guest identity");
        ClientShellSnapshot admission = shell.Snapshot(textEntryFocused: true);
        Equal(ClientShellScreen.Admission, admission.Screen);
        Equal(ClientInputFocusOwner.ModalMenu, admission.Focus.Owner);
        True(!admission.Focus.GameplayInputAllowed);
        shell.ReturnToMainMenu();

        shell.BeginLoading("Entering local world");
        ClientShellSnapshot loading = shell.Snapshot();
        Equal(ClientShellScreen.Loading, loading.Screen);
        True(!loading.Focus.GameplayInputAllowed);
        shell.EnterSession("Local world ready");
        ClientShellSnapshot world = shell.Snapshot();
        Equal(ClientInputFocusOwner.World, world.Focus.Owner);
        True(world.Focus.GameplayInputAllowed);
        True(world.Focus.WorldInteractionAllowed);

        ClientShellSnapshot chat = shell.Snapshot(textEntryFocused: true);
        Equal(ClientInputFocusOwner.HudText, chat.Focus.Owner);
        True(chat.Focus.TextInputActive);
        True(!chat.Focus.GameplayInputAllowed);

        ClientShellSnapshot debug = shell.Snapshot(debugControlFocused: true);
        Equal(ClientInputFocusOwner.DebugControl, debug.Focus.Owner);
        True(debug.Focus.DebugInputActive);
        True(!debug.Focus.GameplayInputAllowed);

        shell.OpenInSessionOptions();
        Equal(ClientInputFocusOwner.ModalMenu, shell.Snapshot().Focus.Owner);
        shell.OpenSettings();
        shell.CloseSettings();
        Equal(ClientShellScreen.InSessionOptions, shell.Snapshot().Screen);
        shell.ResumeSession();
        Equal(ClientShellScreen.InSession, shell.Snapshot().Screen);

        shell.Fail("Connection failed safely");
        ClientShellSnapshot failed = shell.Snapshot();
        Equal(ClientShellScreen.Error, failed.Screen);
        Equal("Connection failed safely", failed.Error);
        shell.ReturnToMainMenu("Session retired");
        ClientShellSnapshot returned = shell.Snapshot();
        Equal(ClientShellScreen.MainMenu, returned.Screen);
        True(!returned.SessionActive);
        True(returned.TransitionSequence >= 9);

        Exception? foreign = null;
        var thread = new Thread(() =>
        {
            try { _ = shell.Snapshot(); }
            catch (Exception exception) { foreign = exception; }
        });
        thread.Start();
        thread.Join();
        True(foreign is InvalidOperationException);
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
