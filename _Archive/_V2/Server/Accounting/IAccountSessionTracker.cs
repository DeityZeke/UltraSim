namespace UltraSim.Server.Accounting;

public interface IAccountSessionTracker
{
    bool TryBeginSession(
        ulong accountId,
        string sessionKey,
        AccountSessionConflictMode conflictMode,
        out string? displacedSessionKey);

    void EndSession(ulong accountId, string sessionKey);
}
