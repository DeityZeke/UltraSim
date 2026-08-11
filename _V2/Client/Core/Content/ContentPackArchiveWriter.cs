using System.IO.Compression;

namespace UltraSim.Client.Content;

public interface IContentPackArchiveWriter
{
    string ArchiveKind { get; }
    bool TryWritePackage(string sourceDirectory, string archivePath, bool overwrite, out string error);
}

public sealed class ZipContentPackArchiveWriter : IContentPackArchiveWriter
{
    public string ArchiveKind => "zip";

    public bool TryWritePackage(string sourceDirectory, string archivePath, bool overwrite, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            error = "Content pack archive path is required.";
            return false;
        }

        string fullArchivePath = Path.GetFullPath(archivePath);
        if (File.Exists(fullArchivePath))
        {
            if (!overwrite)
            {
                error = $"Content pack archive '{fullArchivePath}' already exists.";
                return false;
            }

            File.Delete(fullArchivePath);
        }

        string? parent = Path.GetDirectoryName(fullArchivePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var source = new DirectoryContentPackSource(sourceDirectory);
        if (!ContentPackLoader.TryReadAndValidate(source, out ContentPackManifest manifest, out error))
        {
            return false;
        }

        string tempPath = fullArchivePath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteSourceEntry(source, archive, ContentPackManifest.ManifestPath);
                foreach (ContentPackFileEntry file in manifest.Files)
                {
                    WriteSourceEntry(source, archive, file.Path);
                }
            }

            File.Move(tempPath, fullArchivePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            error = $"Failed to write content pack archive '{fullArchivePath}': {ex.Message}";
            return false;
        }
    }

    private static void WriteSourceEntry(IContentPackSource source, ZipArchive archive, string relativePath)
    {
        if (!ContentPathValidation.TryNormalizeRelativePath(relativePath, out string normalizedPath, out string error))
        {
            throw new InvalidDataException(error);
        }

        if (!source.TryReadBytes(normalizedPath, out byte[] bytes, out error))
        {
            throw new InvalidDataException(error);
        }

        ZipArchiveEntry entry = archive.CreateEntry(normalizedPath, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
