namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveSetOptions(
    string CurrentDirectoryName = "",
    string PendingDirectoryName = ".pending",
    string BackupsDirectoryName = "backups",
    int BackupCount = 3,
    string? BackupsRootDirectory = null,
    ServerPersistenceRecoveryMode RecoveryMode = ServerPersistenceRecoveryMode.CurrentThenBackups)
{
    public int ClampedBackupCount => BackupCount < 0 ? -1 : BackupCount;
}
