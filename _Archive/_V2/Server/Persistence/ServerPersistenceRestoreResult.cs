namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceRestoreResult(
    bool Succeeded,
    string BackupDirectoryName,
    string? RestoredSaveSetRoot,
    string? SafetyBackupRoot,
    ServerPersistenceSaveManifest? Manifest,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ServerPersistenceSaveFailure> Failures)
{
    public static ServerPersistenceRestoreResult Success(
        string backupDirectoryName,
        string restoredSaveSetRoot,
        string? safetyBackupRoot,
        ServerPersistenceSaveManifest manifest,
        IReadOnlyList<string>? warnings = null)
    {
        return new ServerPersistenceRestoreResult(
            Succeeded: true,
            BackupDirectoryName: backupDirectoryName,
            RestoredSaveSetRoot: restoredSaveSetRoot,
            SafetyBackupRoot: safetyBackupRoot,
            Manifest: manifest,
            Warnings: warnings ?? [],
            Failures: []);
    }

    public static ServerPersistenceRestoreResult Failure(
        string backupDirectoryName,
        IReadOnlyList<ServerPersistenceSaveFailure> failures,
        IReadOnlyList<string>? warnings = null)
    {
        return new ServerPersistenceRestoreResult(
            Succeeded: false,
            BackupDirectoryName: backupDirectoryName,
            RestoredSaveSetRoot: null,
            SafetyBackupRoot: null,
            Manifest: null,
            Warnings: warnings ?? [],
            Failures: failures);
    }
}
