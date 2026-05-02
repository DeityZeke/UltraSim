namespace UltraSim.Server.Accounting;

public interface IPortalAccountAuthenticator
{
    PortalAccountAuthenticationResult ValidatePortalSession(
        string portalSessionToken,
        string serverInstanceId,
        string remoteAddress,
        DateTimeOffset nowUtc);
}
