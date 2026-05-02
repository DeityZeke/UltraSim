namespace UltraSim.Server.Accounting;

public sealed record CredentialValidationRequest(
    string Username,
    string Password,
    string RemoteAddress,
    string SessionKey,
    DateTimeOffset NowUtc);
