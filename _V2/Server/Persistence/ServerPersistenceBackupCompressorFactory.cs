namespace UltraSim.Server.Persistence;

public static class ServerPersistenceBackupCompressorFactory
{
    public static IServerPersistenceBackupCompressor? Create(
        ServerPersistenceBackupStorageMode mode,
        int zstdCompressionLevel = 3)
    {
        return mode switch
        {
            ServerPersistenceBackupStorageMode.Directory => null,
            ServerPersistenceBackupStorageMode.Zip => new ZipServerPersistenceBackupCompressor(),
            ServerPersistenceBackupStorageMode.Zstd => new ZstdServerPersistenceBackupCompressor(zstdCompressionLevel),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown backup storage mode.")
        };
    }

    public static bool TryParseMode(string value, out ServerPersistenceBackupStorageMode mode)
    {
        mode = ServerPersistenceBackupStorageMode.Directory;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "dir" or "directory" or "folder" or "folders" => Set(ServerPersistenceBackupStorageMode.Directory, out mode),
            "zip" => Set(ServerPersistenceBackupStorageMode.Zip, out mode),
            "zstd" or "zst" or "tar.zst" or "tar-zst" => Set(ServerPersistenceBackupStorageMode.Zstd, out mode),
            _ => false
        };
    }

    private static bool Set(ServerPersistenceBackupStorageMode value, out ServerPersistenceBackupStorageMode mode)
    {
        mode = value;
        return true;
    }
}
