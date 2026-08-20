namespace UltraSim.Server.Accounting;

public sealed class DefaultCredentialService : ICredentialService
{
    public const string LocalProviderKey = "local.server_accounts";

    private readonly IAccountRegistry _accounts;
    private readonly IPasswordHashService _passwords;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAccountSessionTracker _sessions;
    private readonly IAccountAuditSink? _audit;
    private readonly AccountSessionConflictMode _sessionConflictMode;
    private readonly bool _autoCreateUnknownAccounts;
    private readonly int _maxAutoCreatedAccountsPerRemoteAddress;
    private readonly Action<AccountOperatorEvent>? _operatorEvents;

    public DefaultCredentialService(
        IAccountRegistry accounts,
        IPasswordHashService passwords,
        ILoginRateLimiter rateLimiter,
        IAccountSessionTracker sessions,
        IAccountAuditSink? audit = null,
        AccountSessionConflictMode sessionConflictMode = AccountSessionConflictMode.DenySecondLogin,
        bool autoCreateUnknownAccounts = false,
        int maxAutoCreatedAccountsPerRemoteAddress = 0,
        Action<AccountOperatorEvent>? operatorEvents = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _audit = audit;
        _sessionConflictMode = sessionConflictMode;
        _autoCreateUnknownAccounts = autoCreateUnknownAccounts;
        _maxAutoCreatedAccountsPerRemoteAddress = Math.Max(0, maxAutoCreatedAccountsPerRemoteAddress);
        _operatorEvents = operatorEvents;
    }

    public string ProviderKey => LocalProviderKey;

    public AccountAuthenticationMode Mode => AccountAuthenticationMode.LocalServerAccounts;

    public CredentialValidationResult ValidateLogin(CredentialValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaterialKind != CredentialMaterialKind.PlainPassword)
            return Reject(request, CredentialValidationResultKind.UnsupportedCredentialMaterial, "Unsupported login credential type.");

        LoginRateLimitResult limit = _rateLimiter.Check(request.Username, request.RemoteAddress, request.NowUtc);
        if (!limit.Allowed)
            return Reject(request, CredentialValidationResultKind.RateLimited, "Too many login attempts. Try again later.", limit.RetryAfter);

        if (!_accounts.TryGetByUsername(request.Username, out ServerAccountRecord account))
        {
            if (!_autoCreateUnknownAccounts)
            {
                _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
                return Reject(request, CredentialValidationResultKind.UnknownAccount, "Invalid username or password.");
            }

            if (!TryValidateAutoCreateUsername(request.Username, out string usernameError))
            {
                _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
                return Reject(request, CredentialValidationResultKind.UnknownAccount, usernameError);
            }

            if (_maxAutoCreatedAccountsPerRemoteAddress > 0 &&
                CountAccountsForRemoteAddress(request.RemoteAddress) >= _maxAutoCreatedAccountsPerRemoteAddress)
            {
                _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
                return Reject(
                    request,
                    CredentialValidationResultKind.UnknownAccount,
                    $"This IP address has reached the account creation limit ({_maxAutoCreatedAccountsPerRemoteAddress}).");
            }

            account = _accounts.CreateAccount(
                request.Username,
                _passwords.HashPassword(request.Password),
                request.NowUtc);
            EmitOperatorEvent(
                request,
                AccountOperatorEventKind.AccountCreated,
                CredentialValidationResultKind.Success,
                account.AccountId,
                "Account created.");
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
        EmitOperatorEvent(
            request,
            AccountOperatorEventKind.LoginSucceeded,
            CredentialValidationResultKind.Success,
            updated.AccountId,
            "Login accepted.");
        return CredentialValidationResult.Success(updated);
    }

    private static bool TryValidateAutoCreateUsername(string username, out string error)
    {
        string trimmed = username?.Trim() ?? string.Empty;
        if (trimmed.Length < 3)
        {
            error = "Username must be a minimum of 3 characters.";
            return false;
        }

        if (trimmed.Length > 32)
        {
            error = "Username cannot exceed 32 characters.";
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!char.IsLetterOrDigit(trimmed[i]))
            {
                error = "Username cannot contain special characters. A-Z and 0-9 only.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private int CountAccountsForRemoteAddress(string remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
            return 0;

        string normalized = remoteAddress.Trim();
        int count = 0;
        IReadOnlyList<ServerAccountRecord> accounts = _accounts.GetAccountsSnapshot();
        for (int i = 0; i < accounts.Count; i++)
        {
            string[] known = accounts[i].KnownRemoteAddresses;
            for (int j = 0; j < known.Length; j++)
            {
                if (string.Equals(known[j], normalized, StringComparison.Ordinal))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
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
        EmitOperatorEvent(request, AccountOperatorEventKind.LoginFailed, kind, accountId, publicMessage);
        return CredentialValidationResult.Reject(kind, publicMessage);
    }

    private CredentialValidationResult Reject(
        CredentialValidationRequest request,
        CredentialValidationResultKind kind,
        string publicMessage,
        TimeSpan? retryAfter)
    {
        RecordAudit(request, kind, accountId: null);
        EmitOperatorEvent(request, AccountOperatorEventKind.LoginFailed, kind, accountId: null, publicMessage);
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

    private void EmitOperatorEvent(
        CredentialValidationRequest request,
        AccountOperatorEventKind eventKind,
        CredentialValidationResultKind resultKind,
        ulong? accountId,
        string publicMessage)
    {
        _operatorEvents?.Invoke(new AccountOperatorEvent(
            request.NowUtc,
            request.Username,
            request.RemoteAddress,
            request.SessionKey,
            eventKind,
            resultKind,
            accountId,
            publicMessage));
    }
}
