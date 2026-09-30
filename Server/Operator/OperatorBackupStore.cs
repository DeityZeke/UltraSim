using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UltraSim.Framework.Persistence;

namespace UltraSim.Server.Operator;

public readonly record struct OperatorBackupResult(bool Succeeded, ulong Sequence, bool Reused,
    string Diagnostic);

/// <summary>Private, validated save-set-shaped copy of one selected backup.</summary>
public sealed class OperatorColdRestoreStage : IDisposable
{
    private readonly string _root;
    private bool _disposed;
    internal OperatorColdRestoreStage(string root) => _root = root;
    public string SaveRoot => _disposed ? throw new ObjectDisposedException(nameof(OperatorColdRestoreStage)) : _root;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

/// <summary>
/// Copies only a complete, hash-validated published save generation into a separate bounded
/// operator root. It never serializes live Game authority or edits CURRENT.
/// </summary>
public sealed class OperatorBackupStore
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private readonly string _saveRoot;
    private readonly string _backupRoot;
    private readonly int _retention;
    private readonly IDurableFileSystem _durable = new DurableFileSystem();

    public OperatorBackupStore(string saveRoot, string backupRoot, int retention)
    {
        _saveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveRoot));
        _backupRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupRoot));
        if (retention is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(retention));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (_saveRoot.Equals(_backupRoot, comparison) ||
            _saveRoot.StartsWith(_backupRoot + Path.DirectorySeparatorChar, comparison) ||
            _backupRoot.StartsWith(_saveRoot + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Live saves and backups require disjoint roots.");
        _retention = retention;
        Directory.CreateDirectory(_backupRoot);
        RejectReparse(_backupRoot);
    }

    public string BackupRoot => _backupRoot;

    /// <summary>
    /// Validates and stages one selected complete backup as a private SaveSet root. The caller
    /// must cold-load through normal Prepare/Resolve/Validate/Commit, then publish a new live
    /// generation before admitting clients. This method never edits the live save root.
    /// </summary>
    public OperatorColdRestoreStage StageSelectedForColdRecovery(ulong sequence)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        string name = sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
        string source = Path.Combine(_backupRoot, name);
        ValidateGeneration(source, name);
        string stage = Path.Combine(_backupRoot, ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            string generations = Path.Combine(stage, "generations");
            string selected = Path.Combine(generations, name);
            Directory.CreateDirectory(generations);
            Directory.CreateDirectory(selected);
            SaveSetManifest manifest = ReadManifest(source);
            CopyDurably(Path.Combine(source, "manifest.json"), Path.Combine(selected, "manifest.json"));
            foreach (SaveSetArtifact artifact in manifest.Artifacts)
                CopyDurably(Path.Combine(source, artifact.FileName),
                    Path.Combine(selected, artifact.FileName));
            ValidateGeneration(selected, name);
            _durable.FlushDirectory(selected);
            _durable.FlushDirectory(generations);
            using (var pointer = new FileStream(Path.Combine(stage, "CURRENT"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                pointer.Write(Encoding.ASCII.GetBytes(name + "\n"));
                pointer.Flush(flushToDisk: true);
            }
            _durable.FlushDirectory(stage);
            return new OperatorColdRestoreStage(stage);
        }
        catch
        {
            Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    public OperatorBackupResult TryBackupCurrent()
    {
        try
        {
            string pointer = Path.Combine(_saveRoot, "CURRENT");
            RejectReparse(_saveRoot);
            RejectReparse(pointer);
            if (new FileInfo(pointer).Length is <= 0 or > 64)
                throw new InvalidDataException("CURRENT pointer size is invalid.");
            string name = File.ReadAllText(pointer).TrimEnd('\r', '\n');
            if (!IsSequenceName(name))
                throw new InvalidDataException("CURRENT is not a canonical save sequence.");
            string source = Path.Combine(_saveRoot, "generations", name);
            ValidateGeneration(source, name);
            string final = Path.Combine(_backupRoot, name);
            if (Directory.Exists(final))
            {
                ValidateGeneration(final, name);
                return new(true, ulong.Parse(name, System.Globalization.CultureInfo.InvariantCulture), true, "Complete snapshot already exists.");
            }

            string pending = Path.Combine(_backupRoot, name + ".pending." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pending);
            try
            {
                SaveSetManifest manifest = ReadManifest(source);
                CopyDurably(Path.Combine(source, "manifest.json"), Path.Combine(pending, "manifest.json"));
                foreach (SaveSetArtifact artifact in manifest.Artifacts)
                    CopyDurably(Path.Combine(source, artifact.FileName), Path.Combine(pending, artifact.FileName));
                ValidateGeneration(pending, name);
                _durable.FlushDirectory(pending);
                Directory.Move(pending, final);
                _durable.FlushDirectory(_backupRoot);
                TrimOldSnapshots();
            }
            finally
            {
                if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
            }
            return new(true, ulong.Parse(name, System.Globalization.CultureInfo.InvariantCulture), false, "Complete snapshot published.");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException)
        {
            return new(false, 0, false, Bounded(failure.Message));
        }
    }

    public int CopyCompleteSequences(Span<ulong> destination)
    {
        string[] paths = Directory.GetDirectories(_backupRoot);
        if (paths.Length > 128) throw new InvalidDataException("Backup root has too many entries.");
        var sequences = paths.Select(Path.GetFileName).Where(IsSequenceName)
            .OrderByDescending(static name => name, StringComparer.Ordinal).ToArray();
        if (sequences.Length > destination.Length) return -1;
        for (int index = 0; index < sequences.Length; index++)
        {
            ValidateGeneration(Path.Combine(_backupRoot, sequences[index]!), sequences[index]!);
            destination[index] = ulong.Parse(sequences[index]!, System.Globalization.CultureInfo.InvariantCulture);
        }
        return sequences.Length;
    }

    private void TrimOldSnapshots()
    {
        string[] snapshots = Directory.GetDirectories(_backupRoot)
            .Where(path => IsSequenceName(Path.GetFileName(path)))
            .OrderByDescending(static path => path, StringComparer.Ordinal).ToArray();
        if (snapshots.Length > 128)
            throw new InvalidDataException("Backup root has too many entries for bounded retention.");
        for (int index = _retention; index < snapshots.Length; index++)
        {
            RejectReparse(snapshots[index]);
            Directory.Delete(snapshots[index], recursive: true);
        }
        _durable.FlushDirectory(_backupRoot);
    }

    private static void CopyDurably(string source, string target)
    {
        RejectReparse(source);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static SaveSetManifest ValidateGeneration(string path, string name)
    {
        RejectReparse(path);
        SaveSetManifest manifest = ReadManifest(path);
        if (manifest.FormatVersion != 1 || manifest.Sequence == 0 ||
            !string.Equals(manifest.Sequence.ToString("D20"), name, StringComparison.Ordinal) ||
            manifest.Artifacts is null || manifest.Artifacts.Length is < 1 or > 64)
            throw new InvalidDataException("Backup generation manifest is invalid.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (SaveSetArtifact artifact in manifest.Artifacts)
        {
            if (artifact is null || !IsArtifactKey(artifact.Key) ||
                artifact.FileName != artifact.Key + ".bin" || !names.Add(artifact.FileName) ||
                artifact.ByteCount < 0 || artifact.ByteCount > Array.MaxLength ||
                artifact.Sha256?.Length != 64)
                throw new InvalidDataException("Backup artifact metadata is invalid.");
            string file = Path.Combine(path, artifact.FileName);
            RejectReparse(file);
            if (new FileInfo(file).Length != artifact.ByteCount)
                throw new InvalidDataException("Backup artifact length is invalid.");
            using var stream = File.OpenRead(file);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(artifact.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Backup artifact hash is invalid.");
        }
        return manifest;
    }

    private static SaveSetManifest ReadManifest(string path)
    {
        string file = Path.Combine(path, "manifest.json");
        RejectReparse(file);
        var info = new FileInfo(file);
        if (info.Length is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("Backup manifest size is invalid.");
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize<SaveSetManifest>(stream) ??
            throw new InvalidDataException("Backup manifest is empty.");
    }

    private static bool IsSequenceName(string? name) => name is { Length: 20 } &&
        name != "00000000000000000000" && name.All(static c => c is >= '0' and <= '9') &&
        ulong.TryParse(name, out _);

    private static bool IsArtifactKey(string? key) => key is { Length: > 0 and <= 64 } &&
        key.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Operator storage path is a reparse point.");
    }

    private static string Bounded(string message) => message.Length <= 256 ? message : message[..256];
}
