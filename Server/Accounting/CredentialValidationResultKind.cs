namespace UltraSim.Server.Accounting;

public enum CredentialValidationResultKind : byte
{
    Success = 0,
    UnknownAccount = 1,
    InvalidPassword = 2,
    Banned = 3,
    Suspended = 4,
    PendingVerification = 5,
    LockedOut = 6,
    AlreadyLoggedIn = 7,
    RateLimited = 8
}
