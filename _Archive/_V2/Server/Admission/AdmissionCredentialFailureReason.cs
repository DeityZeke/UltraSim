namespace UltraSim.Server.Admission;

public enum AdmissionCredentialFailureReason : byte
{
    None = 0,
    InvalidCredentials = 1,
    Banned = 2,
    Suspended = 3,
    PendingVerification = 4,
    LockedOut = 5,
    AlreadyLoggedIn = 6,
    RateLimited = 7,
    UnsupportedCredential = 8,
    ExternalProviderUnavailable = 9,
    Rejected = 10
}
