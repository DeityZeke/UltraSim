namespace UltraSim.Server.Accounting;

public sealed record CredentialValidationRequest(
    string Username,
    string Secret,
    string RemoteAddress,
    string SessionKey,
    DateTimeOffset NowUtc,
    CredentialMaterialKind MaterialKind = CredentialMaterialKind.PlainPassword)
{
    public string Password => MaterialKind == CredentialMaterialKind.PlainPassword ? Secret : string.Empty;
    public string PortalSessionToken => MaterialKind == CredentialMaterialKind.PortalSessionToken ? Secret : string.Empty;
}
