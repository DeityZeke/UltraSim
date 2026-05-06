namespace UltraSim.Server;

public interface IServerApplication
{
    ServerApplicationState State { get; }
    bool IsInitialized { get; }
    bool IsRunning { get; }

    void Initialize();
    void Start();
    void Tick(float deltaSeconds);
    void RequestSave();
    bool Save();
    void Log(string message);
    void Stop();
    void Restart();

    ServerStatusSnapshot GetStatusSnapshot();
    ServerMetricsSnapshot GetMetricsSnapshot();
    IReadOnlyList<ServerSystemRuntimeSnapshot> GetSystemRuntimeSnapshots();
    IReadOnlyList<ServerPlayerSessionSnapshot> GetPlayerSessionsSnapshot();
    IReadOnlyList<ServerAccountSummarySnapshot> GetAccountSummariesSnapshot();
    ServerPlayerSessionActionResult KickPlayerSession(string sessionId);
    ServerAccountActionResult BanAccount(string accountId);
    ServerAccountAccessUpdateResult ReplaceAccountAccess(
        string accountId,
        int accessLevel,
        IReadOnlyList<string> directPermissionGrants,
        IReadOnlyList<string> directPermissionDenies);
    bool TryExecuteCommand(ServerControlCommand command, out ServerCommandResult result);

    event Action<ServerApplicationEvent>? EventRaised;
}
