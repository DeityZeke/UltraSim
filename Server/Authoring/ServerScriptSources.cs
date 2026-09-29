using System.Security.Cryptography;
using UltraSim.Framework.RuntimeHost;

namespace UltraSim.Server.Authoring;

public sealed record ServerScriptSource(string RelativePath, byte[] Bytes, string Hash);
public sealed record ServerScriptPackage(RuntimePackageManifest Manifest, byte[] ManifestBytes,
    string ManifestHash, IReadOnlyList<ServerScriptSource> Sources);

/// <summary>Server-owned, bounded, top-level Scripts/package source admission.</summary>
public static class ServerScriptSources
{
    public const int MaximumSourceFilesPerPackage = 64;
    public const int MaximumSourceBytesPerPackage = 256 * 1024;
    public const int MaximumSourceBytesTotal = 2 * 1024 * 1024;

    public static IReadOnlyList<ServerScriptPackage> Discover(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        RejectReparseAncestors(fullRoot);
        string[] directories = Directory.GetDirectories(fullRoot);
        if (directories.Length > RuntimeCompositionPlanner.MaximumPackages)
            throw new InvalidDataException("Scripts package capacity exceeded.");
        CheckCaseConflicts(directories.Select(Path.GetFileName));
        foreach (string file in Directory.GetFiles(fullRoot))
            throw new InvalidDataException($"Unexpected Scripts root file: {Path.GetFileName(file)}.");
        var packages = new List<ServerScriptPackage>(directories.Length);
        int totalBytes = 0;
        foreach (string directory in directories.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            RejectReparse(directory);
            string packageName = Path.GetFileName(directory);
            RuntimePackageId pathId;
            try { pathId = new RuntimePackageId(packageName); }
            catch (ArgumentException error) { throw new InvalidDataException($"Invalid package directory {packageName}.", error); }
            if (Directory.GetDirectories(directory).Length != 0)
                throw new InvalidDataException($"Nested source directories are not admitted for {pathId}.");
            string[] files = Directory.GetFiles(directory);
            CheckCaseConflicts(files.Select(Path.GetFileName));
            if (files.Length > MaximumSourceFilesPerPackage + 1)
                throw new InvalidDataException($"Source file count exceeded for {pathId}.");
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!files.Contains(manifestPath, StringComparer.Ordinal))
                throw new InvalidDataException($"Missing canonical manifest.json for {pathId}.");
            foreach (string file in files) RejectReparse(file);
            byte[] manifestBytes = ReadBounded(manifestPath, RuntimePackageManifestJson.MaximumBytes);
            RuntimePackageManifest manifest;
            try { manifest = RuntimePackageManifestJson.Parse(manifestBytes); }
            catch (Exception error) when (error is FormatException or ArgumentException)
            { throw new InvalidDataException($"Manifest invalid for {pathId}: {error.Message}", error); }
            if (manifest.Id != pathId)
                throw new InvalidDataException($"Manifest identity {manifest.Id} does not match package directory {pathId}.");
            var sources = new List<ServerScriptSource>();
            int packageBytes = 0;
            foreach (string file in files.OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                if (file == manifestPath) continue;
                string name = Path.GetFileName(file);
                if (!name.EndsWith(".cs", StringComparison.Ordinal) || name.Length <= 3)
                    throw new InvalidDataException($"Unexpected source input {pathId}/{name}.");
                byte[] bytes = ReadBounded(file, MaximumSourceBytesPerPackage);
                packageBytes = checked(packageBytes + bytes.Length);
                totalBytes = checked(totalBytes + bytes.Length);
                if (packageBytes > MaximumSourceBytesPerPackage || totalBytes > MaximumSourceBytesTotal)
                    throw new InvalidDataException($"Source byte bound exceeded at {pathId}/{name}.");
                // Strict UTF-8 avoids platform-specific source decoding.
                _ = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                sources.Add(new ServerScriptSource(name, bytes, Hash(bytes)));
            }
            if (sources.Count == 0) throw new InvalidDataException($"Package {pathId} has no C# source.");
            packages.Add(new ServerScriptPackage(manifest, manifestBytes, Hash(manifestBytes), sources));
        }
        return packages;
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > int.MaxValue || stream.Length > maximum)
            throw new InvalidDataException($"Input byte bound exceeded: {Path.GetFileName(path)}.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("Source changed while being admitted.");
        return bytes;
    }

    private static void CheckCaseConflicts(IEnumerable<string?> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? name in names)
            if (name is null || !seen.Add(name))
                throw new InvalidDataException("Case-conflicting source/package paths are not admitted.");
    }

    private static void RejectReparseAncestors(string path)
    {
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
            RejectReparse(current.FullName);
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse/symlink source path is not admitted: {path}.");
    }
}
