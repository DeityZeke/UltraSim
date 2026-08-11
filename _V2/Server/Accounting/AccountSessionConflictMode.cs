namespace UltraSim.Server.Accounting;

public enum AccountSessionConflictMode : byte
{
    DenySecondLogin = 0,
    KickExisting = 1
}
