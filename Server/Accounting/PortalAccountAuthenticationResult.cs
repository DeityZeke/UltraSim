namespace UltraSim.Server.Accounting;

public sealed record PortalAccountAuthenticationResult(
    bool Accepted,
    string PlatformAccountId,
    AccountStatus Status,
    string PublicMessage)
{
    public static PortalAccountAuthenticationResult Accept(string platformAccountId)
    {
        return new PortalAccountAuthenticationResult(true, platformAccountId, AccountStatus.Active, "Portal login accepted.");
    }

    public static PortalAccountAuthenticationResult Reject(AccountStatus status, string publicMessage)
    {
        return new PortalAccountAuthenticationResult(false, string.Empty, status, publicMessage);
    }
}
