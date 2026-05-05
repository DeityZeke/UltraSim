using System.Formats.Tar;
using ZstdSharp;

namespace UltraSim.Server.Persistence;

public sealed class ZstdServerPersistenceBackupCompressor : IServerPersistenceBackupCompressor
{
    public ZstdServerPersistenceBackupCompressor(int compressionLevel = 3)
    {
        CompressionLevel = compressionLevel;
    }

    public string FileExtension => ".tar.zst";

    public int CompressionLevel { get; }

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

        string tempTarPath = Path.Combine(
            string.IsNullOrWhiteSpace(archiveDirectory) ? Path.GetTempPath() : archiveDirectory,
            $"{Path.GetFileName(archivePath)}.{Guid.NewGuid():N}.tar");
        try
        {
            TarFile.CreateFromDirectory(sourceDirectory, tempTarPath, includeBaseDirectory: false);
            using FileStream tar = File.OpenRead(tempTarPath);
            using FileStream archive = File.Create(archivePath);
            using var zstd = new CompressionStream(archive, CompressionLevel, leaveOpen: false);
            tar.CopyTo(zstd);
        }
        finally
        {
            if (File.Exists(tempTarPath))
                File.Delete(tempTarPath);
        }
    }

    public void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Backup archive '{archivePath}' was not found.", archivePath);

        Directory.CreateDirectory(destinationDirectory);
        string tempTarPath = Path.Combine(
            destinationDirectory,
            $".extract-{Guid.NewGuid():N}.tar");
        try
        {
            using FileStream archive = File.OpenRead(archivePath);
            using var zstd = new DecompressionStream(archive, leaveOpen: false);
            using FileStream tar = File.Create(tempTarPath);
            zstd.CopyTo(tar);
            tar.Dispose();
            TarFile.ExtractToDirectory(tempTarPath, destinationDirectory, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempTarPath))
                File.Delete(tempTarPath);
        }
    }
}
