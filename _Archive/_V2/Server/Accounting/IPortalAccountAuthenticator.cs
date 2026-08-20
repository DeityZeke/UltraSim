namespace UltraSim.Server.Accounting;

public interface IPortalAccountAuthenticator
{
    /// <summary>
    /// Validates an opaque portal credential against the platform authority.
    /// Implementations must cryptographically verify the token or validate it
    /// with the portal service; accepting client-provided token text without
    /// verification is never a valid production implementation.
    /// </summary>
    PortalAccountAuthenticationResult ValidatePortalSession(
        string portalSessionToken,
        string serverInstanceId,
        string remoteAddress,
        DateTimeOffset nowUtc);
}
