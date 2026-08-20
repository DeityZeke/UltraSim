namespace UltraSim.Server.Accounting;

public sealed record AccountingPersistenceOptions(
    string AccountsDirectory = "accounts",
    int BackupCount = 3)
{
    public int ClampedBackupCount => BackupCount < 0 ? -1 : BackupCount;
}
