namespace UltraSim.Server.Accounting;

public enum AccountOperatorEventKind
{
    AccountCreated = 1,
    LoginSucceeded = 2,
    LoginFailed = 3,
}

public sealed record AccountOperatorEvent(
    DateTimeOffset TimestampUtc,
    string Username,
    string RemoteAddress,
    string SessionKey,
    AccountOperatorEventKind Kind,
    CredentialValidationResultKind ResultKind,
    ulong? AccountId,
    string PublicMessage);
