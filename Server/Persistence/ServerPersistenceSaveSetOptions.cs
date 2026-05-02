namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveSetOptions(
    string CurrentDirectoryName = "current",
    string PendingDirectoryName = ".pending",
    string BackupsDirectoryName = "backups",
    int BackupCount = 3)
{
    public int ClampedBackupCount => BackupCount < 0 ? -1 : BackupCount;
}
