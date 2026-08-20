namespace UltraSim.Server.Accounting;

public sealed record AccountStatusUpdateResult(
    bool Succeeded,
    ServerAccountRecord? Account,
    string Error = "")
{
    public static AccountStatusUpdateResult Success(ServerAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountStatusUpdateResult(true, account);
    }

    public static AccountStatusUpdateResult Reject(string error)
    {
        return new AccountStatusUpdateResult(false, null, error ?? string.Empty);
    }
}
