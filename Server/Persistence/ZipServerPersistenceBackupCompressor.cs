using System.IO.Compression;

namespace UltraSim.Server.Persistence;

public sealed class ZipServerPersistenceBackupCompressor : IServerPersistenceBackupCompressor
{
    public string FileExtension => ".zip";

    public void CompressDirectory(string sourceDirectory, string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Backup source directory '{sourceDirectory}' was not found.");

        string? archiveDirectory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(archiveDirectory))
            Directory.CreateDirectory(archiveDirectory);

        if (File.Exists(archivePath))
            File.Delete(archivePath);

        ZipFile.CreateFromDirectory(sourceDirectory, archivePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
    }

    public void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Backup archive '{archivePath}' was not found.", archivePath);

        Directory.CreateDirectory(destinationDirectory);
        ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
    }
}
