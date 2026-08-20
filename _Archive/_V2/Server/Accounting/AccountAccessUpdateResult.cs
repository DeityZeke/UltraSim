namespace UltraSim.Server.Accounting;

public sealed record AccountAccessUpdateResult(
    bool Succeeded,
    ServerAccountRecord? Account,
    string Error = "")
{
    public static AccountAccessUpdateResult Success(ServerAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountAccessUpdateResult(true, account);
    }

    public static AccountAccessUpdateResult Reject(string error)
    {
        return new AccountAccessUpdateResult(false, null, error ?? string.Empty);
    }
}
