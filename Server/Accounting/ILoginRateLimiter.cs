namespace UltraSim.Server.Accounting;

public interface ILoginRateLimiter
{
    LoginRateLimitResult Check(string username, string remoteAddress, DateTimeOffset nowUtc);

    void RecordFailure(string username, string remoteAddress, DateTimeOffset nowUtc);

    void RecordSuccess(string username, string remoteAddress);
}
