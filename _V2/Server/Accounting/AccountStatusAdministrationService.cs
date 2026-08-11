namespace UltraSim.Server.Accounting;

public sealed class AccountStatusAdministrationService
{
    private readonly IAccountRegistry _accounts;

    public AccountStatusAdministrationService(IAccountRegistry accounts)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    }

    public AccountStatusUpdateResult ReplaceStatus(
        ulong targetAccountId,
        AccountStatus status,
        string reason = "")
    {
        if (targetAccountId == 0)
            return AccountStatusUpdateResult.Reject("Target account id is required.");
        if (!_accounts.TryGetById(targetAccountId, out ServerAccountRecord account))
            return AccountStatusUpdateResult.Reject($"Account '{targetAccountId}' was not found.");

        ServerAccountRecord updated = account.WithStatus(status, reason).Normalize();
        _accounts.Update(updated);
        return AccountStatusUpdateResult.Success(updated);
    }
}
