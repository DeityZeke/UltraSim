namespace UltraSim.Server.Accounting;

public enum AccountStatus : byte
{
    Active = 0,
    Banned = 1,
    Suspended = 2,
    PendingVerification = 3,
    LockedOut = 4
}
