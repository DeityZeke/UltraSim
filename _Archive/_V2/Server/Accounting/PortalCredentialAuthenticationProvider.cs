namespace UltraSim.Server.Accounting;

public sealed class PortalCredentialAuthenticationProvider : IAccountAuthenticationProvider
{
    public const string PortalProviderKey = "ultrasim.portal_accounts";

    private readonly IPortalAccountAuthenticator _portal;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAccountAuditSink? _audit;
    private readonly string _serverInstanceId;

    public PortalCredentialAuthenticationProvider(
        IPortalAccountAuthenticator portal,
        ILoginRateLimiter rateLimiter,
        string serverInstanceId,
        IAccountAuditSink? audit = null)
    {
        _portal = portal ?? throw new ArgumentNullException(nameof(portal));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _serverInstanceId = string.IsNullOrWhiteSpace(serverInstanceId)
            ? throw new ArgumentException("Server instance id is required.", nameof(serverInstanceId))
            : serverInstanceId.Trim();
        _audit = audit;
    }

    public string ProviderKey => PortalProviderKey;

    public AccountAuthenticationMode Mode => AccountAuthenticationMode.UltraSimPortalAccounts;

    public CredentialValidationResult ValidateLogin(CredentialValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaterialKind != CredentialMaterialKind.PortalSessionToken)
            return Reject(request, CredentialValidationResultKind.UnsupportedCredentialMaterial, "Unsupported login credential type.");

        LoginRateLimitResult limit = _rateLimiter.Check(request.Username, request.RemoteAddress, request.NowUtc);
        if (!limit.Allowed)
            return Reject(request, CredentialValidationResultKind.RateLimited, "Too many login attempts. Try again later.", retryAfter: limit.RetryAfter);

        PortalAccountAuthenticationResult portalResult;
        try
        {
            portalResult = _portal.ValidatePortalSession(
                request.PortalSessionToken,
                _serverInstanceId,
                request.RemoteAddress,
                request.NowUtc);
        }
        catch
        {
            return Reject(request, CredentialValidationResultKind.ExternalProviderUnavailable, "Portal authentication is unavailable.");
        }

        if (!portalResult.Accepted)
        {
            _rateLimiter.RecordFailure(request.Username, request.RemoteAddress, request.NowUtc);
            CredentialValidationResultKind kind = portalResult.Status switch
            {
                AccountStatus.Banned => CredentialValidationResultKind.Banned,
                AccountStatus.Suspended => CredentialValidationResultKind.Suspended,
                AccountStatus.PendingVerification => CredentialValidationResultKind.PendingVerification,
                AccountStatus.LockedOut => CredentialValidationResultKind.LockedOut,
                _ => CredentialValidationResultKind.InvalidPassword
            };
            return Reject(request, kind, string.IsNullOrWhiteSpace(portalResult.PublicMessage) ? "Portal login rejected." : portalResult.PublicMessage);
        }

        _rateLimiter.RecordSuccess(request.Username, request.RemoteAddress);
        RecordAudit(request, CredentialValidationResultKind.Success);
        return CredentialValidationResult.Success(new ServerAccountRecord(
            AccountId: 0,
            Username: portalResult.PlatformAccountId,
            PasswordHash: $"external:{ProviderKey}",
            Status: AccountStatus.Active,
            CreatedUtc: request.NowUtc,
            LastLoginUtc: request.NowUtc,
            KnownRemoteAddresses: [request.RemoteAddress]));
    }

    private CredentialValidationResult Reject(
        CredentialValidationRequest request,
        CredentialValidationResultKind kind,
        string publicMessage,
        TimeSpan? retryAfter = null)
    {
        RecordAudit(request, kind);
        return CredentialValidationResult.Reject(kind, publicMessage, retryAfter);
    }

    private void RecordAudit(CredentialValidationRequest request, CredentialValidationResultKind kind)
    {
        _audit?.Record(new AccountLoginAuditEntry(
            request.NowUtc,
            request.Username,
            request.RemoteAddress,
            request.SessionKey,
            kind,
            AccountId: null));
    }
}
