namespace UltraSim.Server.Accounting;

public readonly record struct LoginRateLimitResult(bool Allowed, TimeSpan? RetryAfter)
{
    public static LoginRateLimitResult Allow() => new(true, null);

    public static LoginRateLimitResult Deny(TimeSpan retryAfter) => new(false, retryAfter);
}
