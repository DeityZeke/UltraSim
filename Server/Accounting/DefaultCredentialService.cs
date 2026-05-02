namespace UltraSim.Server.Accounting;

public sealed class DefaultCredentialService : ICredentialService
{
    private readonly IAccountRegistry _accounts;
    private readonly IPasswordHashService _passwords;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAccountSessionTracker _sessions;
    private readonly IAccountAuditSink? _audit;
    private readonly AccountSessionConflictMode _sessionConflictMode;

    public DefaultCredentialService(
        IAccountRegistry accounts,
        IPasswordHashService passwords,
        ILoginRateLimiter rateLimiter,
        IAccountSessionTracker sessions,
        IAccountAuditSink? audit = null,
        AccountSessionConflictMode sessionConflictMode = AccountSessionConflictMode.DenySecondLogin)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _audit = audit;
        _sessionConflictMode = sessionConflictMode;
    }

    public CredentialValidationResult ValidateLogin(CredentialValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        LoginRateLimitResult limit = _rateLimiter.Check(request.Username, request.RemoteAddress, request.NowUtc);
        if (!limit.Allowed)
            return Reject(request, CredentialValidationResultKind.RateLimited, "Too many login attempts. Try again later.", limit.RetryAfter);

        if (!_accounts.TryGetByUsername(request.Username, out ServerAccountRecord account))
        {
            _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
            return Reject(request, CredentialValidationResultKind.UnknownAccount, "Invalid username or password.");
        }

        CredentialValidationResult? statusResult = ValidateAccountStatus(request, account);
        if (statusResult is not null)
            return statusResult;

        if (!_passwords.VerifyPassword(request.Password, account.PasswordHash))
        {
            _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
            return Reject(request, CredentialValidationResultKind.InvalidPassword, "Invalid username or password.", account.AccountId);
        }

        if (!_sessions.TryBeginSession(account.AccountId, request.SessionKey, _sessionConflictMode, out _))
            return Reject(request, CredentialValidationResultKind.AlreadyLoggedIn, "This account is already logged in.", account.AccountId);

        _rateLimiter.RecordSuccess(request.Username, request.RemoteAddress);
        ServerAccountRecord updated = account.WithLoginSuccess(request.NowUtc, request.RemoteAddress);
        if (_passwords.NeedsRehash(account.PasswordHash))
        {
            updated = updated with { PasswordHash = _passwords.HashPassword(request.Password) };
        }

        _accounts.Update(updated);
        RecordAudit(request, CredentialValidationResultKind.Success, updated.AccountId);
        return CredentialValidationResult.Success(updated);
    }

    private CredentialValidationResult? ValidateAccountStatus(
        CredentialValidationRequest request,
        ServerAccountRecord account)
    {
        return account.Status switch
        {
            AccountStatus.Active => null,
            AccountStatus.Banned => Reject(request, CredentialValidationResultKind.Banned, "This account is banned.", account.AccountId),
            AccountStatus.Suspended => Reject(request, CredentialValidationResultKind.Suspended, "This account is suspended.", account.AccountId),
            AccountStatus.PendingVerification => Reject(request, CredentialValidationResultKind.PendingVerification, "This account is pending verification.", account.AccountId),
            AccountStatus.LockedOut => Reject(request, CredentialValidationResultKind.LockedOut, "This account is locked.", account.AccountId),
            _ => Reject(request, CredentialValidationResultKind.LockedOut, "This account is locked.", account.AccountId)
        };
    }

    private CredentialValidationResult Reject(
        CredentialValidationRequest request,
        CredentialValidationResultKind kind,
        string publicMessage,
        ulong? accountId = null)
    {
        RecordAudit(request, kind, accountId);
        return CredentialValidationResult.Reject(kind, publicMessage);
    }

    private CredentialValidationResult Reject(
        CredentialValidationRequest request,
        CredentialValidationResultKind kind,
        string publicMessage,
        TimeSpan? retryAfter)
    {
        RecordAudit(request, kind, accountId: null);
        return CredentialValidationResult.Reject(kind, publicMessage, retryAfter);
    }

    private void RecordAudit(CredentialValidationRequest request, CredentialValidationResultKind kind, ulong? accountId)
    {
        _audit?.Record(new AccountLoginAuditEntry(
            request.NowUtc,
            request.Username,
            request.RemoteAddress,
            request.SessionKey,
            kind,
            accountId));
    }
}
