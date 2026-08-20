namespace UltraSim.Server.Accounting;

public sealed record AccountLoginAuditEntry(
    DateTimeOffset TimestampUtc,
    string Username,
    string RemoteAddress,
    string SessionKey,
    CredentialValidationResultKind ResultKind,
    ulong? AccountId);
