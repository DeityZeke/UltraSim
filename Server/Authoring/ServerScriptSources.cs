using System.Security.Cryptography;
using UltraSim.Framework.RuntimeHost;

namespace UltraSim.Server.Authoring;

public sealed record ServerScriptSource(string RelativePath, byte[] Bytes, string Hash);
public sealed record ServerScriptPackage(RuntimePackageManifest Manifest, byte[] ManifestBytes,
    string ManifestHash, IReadOnlyList<ServerScriptSource> Sources);

/// <summary>Server-owned, bounded, shallow-recursive Scripts/package source admission.</summary>
public static class ServerScriptSources
{
    public const int MaximumSourceFilesPerPackage = 64;
    public const int MaximumSourceDirectoryDepth = 4;
    public const int MaximumSourceBytesPerPackage = 256 * 1024;
    public const int MaximumSourceBytesTotal = 2 * 1024 * 1024;

    public static IReadOnlyList<ServerScriptPackage> Discover(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        RejectReparseAncestors(fullRoot);
        var directories = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(fullRoot))
        {
            RejectReparse(entry);
            if (!Directory.Exists(entry)) throw new InvalidDataException($"Unexpected Scripts root file: {Path.GetFileName(entry)}.");
            if (directories.Count == RuntimeCompositionPlanner.MaximumPackages)
                throw new InvalidDataException("Scripts package capacity exceeded.");
            directories.Add(entry);
        }
        CheckCaseConflicts(directories.Select(Path.GetFileName));
        var packages = new List<ServerScriptPackage>(directories.Count);
        int totalBytes = 0;
        foreach (string directory in directories.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            RejectReparse(directory);
            string packageName = Path.GetFileName(directory);
            RuntimePackageId pathId;
            try { pathId = new RuntimePackageId(packageName); }
            catch (ArgumentException error) { throw new InvalidDataException($"Invalid package directory {packageName}.", error); }
            List<(string Path, string Relative)> files = CollectFiles(directory, pathId);
            CheckCaseConflicts(files.Select(x => x.Relative));
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!files.Any(x => x.Path == manifestPath))
                throw new InvalidDataException($"Missing canonical manifest.json for {pathId}.");
            byte[] manifestBytes = ReadBounded(manifestPath, RuntimePackageManifestJson.MaximumBytes);
            RuntimePackageManifest manifest;
            try { manifest = RuntimePackageManifestJson.Parse(manifestBytes); }
            catch (Exception error) when (error is FormatException or ArgumentException)
            { throw new InvalidDataException($"Manifest invalid for {pathId}: {error.Message}", error); }
            if (manifest.Id != pathId)
                throw new InvalidDataException($"Manifest identity {manifest.Id} does not match package directory {pathId}.");
            var sources = new List<ServerScriptSource>();
            int packageBytes = 0;
            foreach ((string file, string relative) in files.OrderBy(x => x.Relative, StringComparer.Ordinal))
            {
                if (file == manifestPath) continue;
                string name = Path.GetFileName(file);
                if (!name.EndsWith(".cs", StringComparison.Ordinal) || name.Length <= 3)
                    throw new InvalidDataException($"Unexpected source input {pathId}/{relative}.");
                byte[] bytes = ReadBounded(file, MaximumSourceBytesPerPackage);
                packageBytes = checked(packageBytes + bytes.Length);
                totalBytes = checked(totalBytes + bytes.Length);
                if (packageBytes > MaximumSourceBytesPerPackage || totalBytes > MaximumSourceBytesTotal)
                    throw new InvalidDataException($"Source byte bound exceeded at {pathId}/{relative}.");
                // Strict UTF-8 avoids platform-specific source decoding.
                _ = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                sources.Add(new ServerScriptSource(relative, bytes, Hash(bytes)));
            }
            if (sources.Count == 0) throw new InvalidDataException($"Package {pathId} has no C# source.");
            packages.Add(new ServerScriptPackage(manifest, manifestBytes, Hash(manifestBytes), sources));
        }
        return packages;
    }

    private static List<(string Path, string Relative)> CollectFiles(string packageRoot, RuntimePackageId package)
    {
        var files = new List<(string Path, string Relative)>();
        var stack = new Stack<(string Directory, string Relative, int Depth)>();
        stack.Push((packageRoot, "", 0));
        int directories = 0;
        while (stack.Count != 0)
        {
            (string current, string prefix, int depth) = stack.Pop();
            if (++directories > MaximumSourceFilesPerPackage)
                throw new InvalidDataException($"Source directory count exceeded for {package}.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                RejectReparse(entry);
                string name = Path.GetFileName(entry);
                if (!CanonicalSourceName(name))
                    throw new InvalidDataException($"Noncanonical source path segment for {package}: {name}.");
                if (!names.Add(name)) throw new InvalidDataException("Case-conflicting source paths are not admitted.");
                string relative = prefix.Length == 0 ? name : prefix + "/" + name;
                if (relative.Length > 384)
                    throw new InvalidDataException($"Source relative path length exceeded for {package}.");
                if (Directory.Exists(entry))
                {
                    if (depth == MaximumSourceDirectoryDepth)
                        throw new InvalidDataException($"Source directory depth exceeded at {package}/{relative}.");
                    stack.Push((entry, relative, depth + 1));
                    if (stack.Count + directories > MaximumSourceFilesPerPackage)
                        throw new InvalidDataException($"Source directory count exceeded for {package}.");
                }
                else
                {
                    if (files.Count == MaximumSourceFilesPerPackage + 1)
                        throw new InvalidDataException($"Source file count exceeded for {package}.");
                    if (relative != "manifest.json" && (!name.EndsWith(".cs", StringComparison.Ordinal) || name.Length <= 3))
                        throw new InvalidDataException($"Unexpected source input {package}/{relative}.");
                    files.Add((entry, relative));
                }
            }
        }
        return files;
    }

    private static bool CanonicalSourceName(string name) => name.Length is >= 1 and <= 96 &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name[^1] != '.' && !name.Contains("..", StringComparison.Ordinal) &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

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
