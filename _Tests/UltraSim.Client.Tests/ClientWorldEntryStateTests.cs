using UltraSim.Client.Core;

static class ClientWorldEntryStateTests
{
    internal static void Run()
    {
        var entry = new ClientWorldEntryCoordinator();
        entry.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.Resolving, "Selecting world"));
        entry.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.StartingAuthority, "Starting"));
        entry.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.PreparingMeshes,
            "Mesh", 0, 11));
        entry.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.PreparingMeshes,
            "Mesh", 11, 11));
        RequireThrows(() => entry.Publish(new ClientWorldEntryProgress(
            ClientWorldEntryStage.PreparingContent, "Regressed")));
        RequireThrows(() => entry.Publish(new ClientWorldEntryProgress(
            ClientWorldEntryStage.PreparingMeshes, "Impossible", 12, 11)));
        RequireThrows(() => entry.Publish(new ClientWorldEntryProgress(
            (ClientWorldEntryStage)255, "Invalid")));
        entry.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.Ready,
            "Playable", CanCancel: false));
        RequireThrows(() => entry.Publish(new ClientWorldEntryProgress(
            ClientWorldEntryStage.Ready, "Again", CanCancel: false)));
        var failed = new ClientWorldEntryCoordinator();
        failed.Publish(new ClientWorldEntryProgress(ClientWorldEntryStage.Failed,
            "Unable to enter", CanCancel: false, Failure: "Broken resource"));
        RequireThrows(() => failed.Publish(new ClientWorldEntryProgress(
            ClientWorldEntryStage.Ready, "Not allowed", CanCancel: false)));
    }

    private static void RequireThrows(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid entry transition was accepted.");
    }
}
