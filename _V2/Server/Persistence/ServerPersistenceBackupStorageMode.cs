namespace UltraSim.Server.Persistence;

public enum ServerPersistenceBackupStorageMode : byte
{
    Directory = 0,
    Zip = 1,
    Zstd = 2,
}
