namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceBackupSnapshot(
    string DirectoryName,
    string RootPath,
    ServerPersistenceSaveManifest Manifest);
