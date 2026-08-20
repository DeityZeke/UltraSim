namespace UltraSim.Server.Accounting;

public sealed class InMemoryAccountSessionTracker : IAccountSessionTracker
{
    private readonly Dictionary<ulong, string> _sessionByAccountId = new();

    public bool TryBeginSession(
        ulong accountId,
        string sessionKey,
        AccountSessionConflictMode conflictMode,
        out string? displacedSessionKey)
    {
        displacedSessionKey = null;
        if (accountId == 0 || string.IsNullOrWhiteSpace(sessionKey))
            return false;

        string normalized = sessionKey.Trim();
        if (!_sessionByAccountId.TryGetValue(accountId, out string? existing))
        {
            _sessionByAccountId.Add(accountId, normalized);
            return true;
        }

        if (string.Equals(existing, normalized, StringComparison.Ordinal))
            return true;

        if (conflictMode == AccountSessionConflictMode.DenySecondLogin)
            return false;

        displacedSessionKey = existing;
        _sessionByAccountId[accountId] = normalized;
        return true;
    }

    public void EndSession(ulong accountId, string sessionKey)
    {
        if (accountId == 0 || string.IsNullOrWhiteSpace(sessionKey))
            return;

        if (_sessionByAccountId.TryGetValue(accountId, out string? existing) &&
            string.Equals(existing, sessionKey.Trim(), StringComparison.Ordinal))
        {
            _sessionByAccountId.Remove(accountId);
        }
    }
}
