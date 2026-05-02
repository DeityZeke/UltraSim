namespace UltraSim.Server.Accounting;

public sealed class InMemoryLoginRateLimiter : ILoginRateLimiter
{
    private readonly Dictionary<string, AttemptState> _attemptsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockout;

    public InMemoryLoginRateLimiter(
        int maxFailures = 5,
        TimeSpan? window = null,
        TimeSpan? lockout = null)
    {
        if (maxFailures <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFailures));

        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(5);
        _lockout = lockout ?? TimeSpan.FromMinutes(10);
    }

    public LoginRateLimitResult Check(string username, string remoteAddress, DateTimeOffset nowUtc)
    {
        string key = CreateKey(username, remoteAddress);
        if (!_attemptsByKey.TryGetValue(key, out AttemptState? state) || state.LockedUntilUtc <= nowUtc)
            return LoginRateLimitResult.Allow();

        return LoginRateLimitResult.Deny(state.LockedUntilUtc - nowUtc);
    }

    public void RecordFailure(string username, string remoteAddress, DateTimeOffset nowUtc)
    {
        string key = CreateKey(username, remoteAddress);
        if (!_attemptsByKey.TryGetValue(key, out AttemptState? state) || nowUtc - state.FirstFailureUtc > _window)
        {
            _attemptsByKey[key] = new AttemptState(nowUtc, 1, DateTimeOffset.MinValue);
            return;
        }

        int failures = state.FailureCount + 1;
        DateTimeOffset lockedUntil = failures >= _maxFailures ? nowUtc + _lockout : state.LockedUntilUtc;
        _attemptsByKey[key] = state with
        {
            FailureCount = failures,
            LockedUntilUtc = lockedUntil
        };
    }

    public void RecordSuccess(string username, string remoteAddress)
    {
        _attemptsByKey.Remove(CreateKey(username, remoteAddress));
    }

    private static string CreateKey(string username, string remoteAddress)
    {
        return $"{(username ?? string.Empty).Trim()}|{(remoteAddress ?? string.Empty).Trim()}";
    }

    private sealed record AttemptState(DateTimeOffset FirstFailureUtc, int FailureCount, DateTimeOffset LockedUntilUtc);
}
