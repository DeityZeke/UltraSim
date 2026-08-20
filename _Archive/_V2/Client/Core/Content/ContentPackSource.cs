using System.IO.Compression;

namespace UltraSim.Client.Content;

public interface IContentPackSource : IDisposable
{
    string SourceId { get; }
    bool TryReadText(string relativePath, out string text, out string error);
    bool TryReadBytes(string relativePath, out byte[] bytes, out string error);
}

public sealed class DirectoryContentPackSource : IContentPackSource
{
    public DirectoryContentPackSource(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Content pack directory path is required.", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }
    public string SourceId => RootPath;

    public bool TryReadText(string relativePath, out string text, out string error)
    {
        text = string.Empty;
        if (!TryGetPath(relativePath, out string fullPath, out error))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = $"Content pack file '{relativePath}' does not exist.";
            return false;
        }

        text = File.ReadAllText(fullPath);
        error = string.Empty;
        return true;
    }

    public bool TryReadBytes(string relativePath, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        if (!TryGetPath(relativePath, out string fullPath, out error))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = $"Content pack file '{relativePath}' does not exist.";
            return false;
        }

        bytes = File.ReadAllBytes(fullPath);
        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
    }

    private bool TryGetPath(string relativePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        if (!ContentPathValidation.TryNormalizeRelativePath(relativePath, out string normalizedPath, out error))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(RootPath, normalizedPath));
        string normalizedRoot = RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            error = "Content pack path escapes the source root.";
            return false;
        }

        fullPath = candidate;
        error = string.Empty;
        return true;
    }
}

public sealed class ZipContentPackSource : IContentPackSource
{
    private readonly ZipArchive _archive;

    public ZipContentPackSource(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Content pack archive path is required.", nameof(archivePath));
        }

        ArchivePath = Path.GetFullPath(archivePath);
        _archive = ZipFile.OpenRead(ArchivePath);
    }

    public string ArchivePath { get; }
    public string SourceId => ArchivePath;

    public bool TryReadText(string relativePath, out string text, out string error)
    {
        text = string.Empty;
        if (!TryGetEntry(relativePath, out ZipArchiveEntry? entry, out error))
        {
            return false;
        }

        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream);
        text = reader.ReadToEnd();
        error = string.Empty;
        return true;
    }

    public bool TryReadBytes(string relativePath, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        if (!TryGetEntry(relativePath, out ZipArchiveEntry? entry, out error))
        {
            return false;
        }

        using Stream stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        bytes = memory.ToArray();
        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
        _archive.Dispose();
    }

    private bool TryGetEntry(string relativePath, out ZipArchiveEntry entry, out string error)
    {
        entry = default!;
        if (!ContentPathValidation.TryNormalizeRelativePath(relativePath, out string normalizedPath, out error))
        {
            return false;
        }

        ZipArchiveEntry? found = _archive.GetEntry(normalizedPath);
        if (found is null)
        {
            error = $"Content pack archive entry '{normalizedPath}' does not exist.";
            return false;
        }

        if (found.FullName.EndsWith("/", StringComparison.Ordinal))
        {
            error = $"Content pack archive entry '{normalizedPath}' is a directory.";
            return false;
        }

        entry = found;
        error = string.Empty;
        return true;
    }
}

public static class ContentPackLoader
{
    public static bool TryReadManifest(IContentPackSource source, out ContentPackManifest manifest, out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        manifest = default!;
        if (!source.TryReadText(ContentPackManifest.ManifestPath, out string json, out error))
        {
            return false;
        }

        if (!ContentPackManifest.TryDeserialize(json, out manifest, out error))
        {
            return false;
        }

        return ContentPackManifestValidator.TryValidate(manifest, out error);
    }

    public static bool TryReadAndValidate(IContentPackSource source, out ContentPackManifest manifest, out string error)
    {
        if (!TryReadManifest(source, out manifest, out error))
        {
            return false;
        }

        return ContentPackManifestValidator.TryValidateDeclaredFiles(source, manifest, out error);
    }
}
