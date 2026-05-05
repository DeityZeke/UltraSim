namespace UltraSim.Server.Accounting;

public sealed class AccountAccessAdministrationService
{
    private readonly IAccountRegistry _accounts;
    private readonly IAccountAccessAuditSink? _audit;
    private readonly Func<DateTimeOffset> _clock;

    public AccountAccessAdministrationService(
        IAccountRegistry accounts,
        IAccountAccessAuditSink? audit = null,
        Func<DateTimeOffset>? clock = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _audit = audit;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public AccountAccessUpdateResult ReplaceAccessMetadata(
        ulong targetAccountId,
        IEnumerable<string>? accessRuleKeys = null,
        IEnumerable<string>? directPermissionGrants = null,
        IEnumerable<string>? directPermissionDenies = null,
        ulong? actorAccountId = null,
        string reason = "")
    {
        if (targetAccountId == 0)
            return AccountAccessUpdateResult.Reject("Target account id is required.");
        if (!_accounts.TryGetById(targetAccountId, out ServerAccountRecord account))
            return AccountAccessUpdateResult.Reject($"Account '{targetAccountId}' was not found.");

        ServerAccountRecord updated = account.WithAccessMetadata(
            accessRuleKeys,
            directPermissionGrants,
            directPermissionDenies).Normalize();
        _accounts.Update(updated);
        _audit?.Record(new AccountAccessAuditEntry(
            _clock(),
            actorAccountId,
            updated.AccountId,
            updated.Username,
            AccountAccessAuditAction.MetadataReplaced,
            updated.AccessRuleKeys,
            updated.DirectPermissionGrants,
            updated.DirectPermissionDenies,
            reason?.Trim() ?? string.Empty));

        return AccountAccessUpdateResult.Success(updated);
    }
}
