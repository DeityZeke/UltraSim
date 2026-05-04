using System.Globalization;
using System.Text.Json;

namespace UltraSim.Server.Persistence;

public sealed class ServerPersistenceCoordinator
{
    private readonly string _rootDirectory;
    private readonly IServerPersistenceParticipant[] _participants;
    private readonly ServerPersistenceSaveSetOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private ulong _nextSaveSequence;

    public ServerPersistenceCoordinator(
        string rootDirectory,
        IEnumerable<IServerPersistenceParticipant> participants,
        ServerPersistenceSaveSetOptions? options = null,
        ulong initialSaveSequence = 0)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Persistence root directory is required.", nameof(rootDirectory));

        ArgumentNullException.ThrowIfNull(participants);
        _rootDirectory = rootDirectory;
        _participants = participants.ToArray();
        if (_participants.Length == 0)
            throw new ArgumentException("At least one persistence participant is required.", nameof(participants));

        _options = options ?? new ServerPersistenceSaveSetOptions();
        _nextSaveSequence = initialSaveSequence;
    }

    public string CurrentSaveSetRoot => ResolveCurrentSaveSetRoot();

    public ServerPersistenceSaveResult Save(DateTimeOffset savedUtc)
    {
        ulong sequence = checked(++_nextSaveSequence);
        return Save(new ServerPersistenceMetadata(sequence, savedUtc));
    }

    public ServerPersistenceSaveResult Save(ServerPersistenceMetadata metadata)
    {
        string pendingRoot = CreatePendingSaveSetRoot(metadata);
        var artifacts = new List<ServerPersistenceArtifact>(_participants.Length);
        var failures = new List<ServerPersistenceSaveFailure>();

        try
        {
            Directory.CreateDirectory(pendingRoot);
            for (int i = 0; i < _participants.Length; i++)
            {
                IServerPersistenceParticipant participant = _participants[i];
                try
                {
                    artifacts.Add(participant.Save(pendingRoot, metadata));
                }
                catch (Exception ex)
                {
                    failures.Add(new ServerPersistenceSaveFailure(participant.Name, ex.Message));
                    break;
                }
            }

            if (failures.Count > 0)
            {
                DeletePendingSaveSet(pendingRoot);
                return ServerPersistenceSaveResult.Failure(metadata, failures);
            }

            var manifest = new ServerPersistenceSaveManifest(
                ServerPersistenceSaveManifest.CurrentVersion,
                metadata.SaveSequence,
                metadata.SavedUtc,
                artifacts);
            WriteManifest(pendingRoot, manifest);
            PromotePendingSaveSet(pendingRoot, metadata);
            return ServerPersistenceSaveResult.Success(metadata, manifest);
        }
        catch (Exception ex)
        {
            DeletePendingSaveSet(pendingRoot);
            failures.Add(new ServerPersistenceSaveFailure("coordinator", ex.Message));
            return ServerPersistenceSaveResult.Failure(metadata, failures);
        }
    }

    public bool TryLoadCurrentManifest(out ServerPersistenceSaveManifest manifest)
    {
        string path = Path.Combine(CurrentSaveSetRoot, "save_manifest.json");
        return TryReadManifest(path, out manifest, out _);
    }

    public ServerPersistenceLoadResult Load()
    {
        var warnings = new List<string>();
        CleanupPendingSaveSets(warnings);

        List<SaveSetCandidate> candidates = CollectLoadCandidates(warnings);
        if (candidates.Count == 0)
            return ServerPersistenceLoadResult.Fresh(warnings);

        SaveSetCandidate? current = candidates.FirstOrDefault(static candidate => !candidate.IsBackup);
        if (current is not null)
        {
            ulong highestBackupSequence = candidates
                .Where(static candidate => candidate.IsBackup)
                .Select(static candidate => candidate.Manifest.SaveSequence)
                .DefaultIfEmpty(0UL)
                .Max();
            if (highestBackupSequence > current.Manifest.SaveSequence)
            {
                warnings.Add($"A backup save-set has sequence {highestBackupSequence}, newer than current sequence {current.Manifest.SaveSequence}.");
            }
        }

        foreach (SaveSetCandidate candidate in candidates)
        {
            if (TryLoadCandidate(candidate, warnings, out ServerPersistenceLoadResult result))
            {
                _nextSaveSequence = Math.Max(_nextSaveSequence, candidate.Manifest.SaveSequence);
                return result;
            }
        }

        return ServerPersistenceLoadResult.Failure(
            [new ServerPersistenceSaveFailure("coordinator", "No valid save-set could be loaded.")],
            warnings);
    }

    private string CreatePendingSaveSetRoot(ServerPersistenceMetadata metadata)
    {
        string pendingDirectory = PendingDirectory;
        Directory.CreateDirectory(pendingDirectory);
        string name = $"save-{metadata.SaveSequence:D20}-{metadata.SavedUtc:yyyyMMddHHmmssfffffff}";
        string path = Path.Combine(pendingDirectory, name);
        DeleteDirectoryIfExists(path);
        return path;
    }

    private void PromotePendingSaveSet(string pendingRoot, ServerPersistenceMetadata metadata)
    {
        Directory.CreateDirectory(_rootDirectory);
        string current = CurrentSaveSetRoot;
        if (CurrentSaveSetDirectoryHasEntries())
        {
            if (_options.ClampedBackupCount == 0)
            {
                DeleteCurrentSaveSet();
            }
            else
            {
                string backupRoot = BackupRootDirectory;
                Directory.CreateDirectory(backupRoot);
                MoveCurrentSaveSetToBackup(CreateBackupPath(backupRoot, metadata));
            }
        }

        PromotePendingSaveSetToCurrent(pendingRoot);
        DeleteEmptyParentDirectory(pendingRoot);
    }

    private void WriteManifest(string saveSetRoot, ServerPersistenceSaveManifest manifest)
    {
        string path = Path.Combine(saveSetRoot, "save_manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, _jsonOptions));
        _ = ReadManifest(path);
    }

    private ServerPersistenceSaveManifest ReadManifest(string path)
    {
        ServerPersistenceSaveManifest? manifest = JsonSerializer.Deserialize<ServerPersistenceSaveManifest>(
            File.ReadAllText(path),
            _jsonOptions);
        if (manifest is null)
            throw new InvalidOperationException($"Save manifest '{path}' was empty.");
        if (manifest.Version != ServerPersistenceSaveManifest.CurrentVersion)
            throw new InvalidOperationException($"Unsupported save manifest version '{manifest.Version}'.");

        return manifest;
    }

    private bool TryReadManifest(
        string path,
        out ServerPersistenceSaveManifest manifest,
        out string error)
    {
        manifest = default!;
        error = string.Empty;
        if (!File.Exists(path))
        {
            error = $"Save manifest '{path}' does not exist.";
            return false;
        }

        try
        {
            manifest = ReadManifest(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private List<SaveSetCandidate> CollectLoadCandidates(List<string> warnings)
    {
        var candidates = new List<SaveSetCandidate>();
        string currentManifestPath = Path.Combine(CurrentSaveSetRoot, "save_manifest.json");
        if (TryReadManifest(currentManifestPath, out ServerPersistenceSaveManifest currentManifest, out string currentError))
        {
            candidates.Add(new SaveSetCandidate(CurrentSaveSetRoot, currentManifest, IsBackup: false));
        }
        else if (CurrentSaveSetDirectoryHasEntries())
        {
            warnings.Add($"Current save-set is not loadable: {currentError}");
        }

        string backupRoot = BackupRootDirectory;
        if (!Directory.Exists(backupRoot))
            return candidates;

        foreach (string backupDirectory in Directory.EnumerateDirectories(backupRoot))
        {
            string manifestPath = Path.Combine(backupDirectory, "save_manifest.json");
            if (TryReadManifest(manifestPath, out ServerPersistenceSaveManifest backupManifest, out string backupError))
            {
                candidates.Add(new SaveSetCandidate(backupDirectory, backupManifest, IsBackup: true));
            }
            else
            {
                warnings.Add($"Backup save-set '{backupDirectory}' is not loadable: {backupError}");
            }
        }

        return candidates
            .OrderBy(static candidate => candidate.IsBackup ? 1 : 0)
            .ThenByDescending(static candidate => candidate.Manifest.SaveSequence)
            .ToList();
    }

    private bool TryLoadCandidate(
        SaveSetCandidate candidate,
        List<string> warnings,
        out ServerPersistenceLoadResult result)
    {
        Dictionary<string, ServerPersistenceArtifact> artifactsByName;
        try
        {
            artifactsByName = candidate.Manifest.Artifacts.ToDictionary(
                static artifact => artifact.Name,
                StringComparer.Ordinal);
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"Save-set '{candidate.Root}' has duplicate participant artifacts: {ex.Message}");
            result = default!;
            return false;
        }

        for (int i = 0; i < _participants.Length; i++)
        {
            IServerPersistenceParticipant participant = _participants[i];
            if (!artifactsByName.ContainsKey(participant.Name))
            {
                warnings.Add($"Save-set '{candidate.Root}' is missing participant artifact '{participant.Name}'.");
                result = default!;
                return false;
            }
        }

        for (int i = 0; i < _participants.Length; i++)
        {
            IServerPersistenceParticipant participant = _participants[i];
            ServerPersistenceArtifact artifact = artifactsByName[participant.Name];

            try
            {
                participant.Load(candidate.Root, candidate.Manifest.Metadata, artifact);
            }
            catch (Exception ex)
            {
                warnings.Add($"Save-set '{candidate.Root}' failed to load participant '{participant.Name}': {ex.Message}");
                result = default!;
                return false;
            }
        }

        result = ServerPersistenceLoadResult.Loaded(
            candidate.Root,
            candidate.Manifest,
            candidate.IsBackup,
            warnings);
        return true;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void DeletePendingSaveSet(string pendingRoot)
    {
        DeleteDirectoryIfExists(pendingRoot);
        DeleteEmptyParentDirectory(pendingRoot);
    }

    private static void DeleteEmptyParentDirectory(string childPath)
    {
        string? parent = Path.GetDirectoryName(childPath);
        if (!string.IsNullOrWhiteSpace(parent) &&
            Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }

    private void CleanupPendingSaveSets(List<string> warnings)
    {
        string pendingDirectory = PendingDirectory;
        if (!Directory.Exists(pendingDirectory))
            return;

        Directory.Delete(pendingDirectory, recursive: true);
        warnings.Add("Removed pending save-set directory left by an interrupted save.");
    }

    private sealed record SaveSetCandidate(
        string Root,
        ServerPersistenceSaveManifest Manifest,
        bool IsBackup);

    private string CreateBackupPath(string backupRoot, ServerPersistenceMetadata metadata)
    {
        int backupCount = _options.ClampedBackupCount < 0 ? 3 : _options.ClampedBackupCount;
        string[] slots = GetBackupSlotNames(Math.Max(1, backupCount));
        string timeStamp = FormatBackupTimestamp(metadata.SavedUtc);

        string[] existing = Directory.GetDirectories(backupRoot);
        for (int i = 0; i < slots.Length; i++)
        {
            DirectoryInfo? directory = MatchBackupSlot(existing, slots[i]);
            if (directory is null)
                continue;

            if (i == 0)
            {
                try { directory.Delete(recursive: true); }
                catch { }
                continue;
            }

            string existingTimeStamp = FindBackupTimestamp(directory.Name) ?? timeStamp;
            string target = FormatBackupDirectory(backupRoot, slots[i - 1], existingTimeStamp);
            DeleteDirectoryIfExists(target);
            try { directory.MoveTo(target); }
            catch { }
        }

        return FormatBackupDirectory(backupRoot, slots[^1], timeStamp);
    }

    private static string[] GetBackupSlotNames(int backupCount) =>
        backupCount <= 1
            ? ["Most Recent"]
            : backupCount == 2
                ? ["Second Backup", "Most Recent"]
                : ["Third Backup", "Second Backup", "Most Recent"];

    private static DirectoryInfo? MatchBackupSlot(string[] paths, string slotName)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            var info = new DirectoryInfo(paths[i]);
            if (info.Name.StartsWith(slotName, StringComparison.Ordinal))
                return info;
        }

        return null;
    }

    private static string FormatBackupDirectory(string root, string name, string timeStamp) =>
        Path.Combine(root, $"{name} ({timeStamp})");

    private static string? FindBackupTimestamp(string input)
    {
        int start = input.IndexOf('(');
        if (start < 0)
            return null;

        int end = input.IndexOf(')', start + 1);
        return end >= start
            ? input.Substring(start + 1, end - start - 1)
            : null;
    }

    private static string FormatBackupTimestamp(DateTimeOffset savedUtc)
    {
        DateTimeOffset local = savedUtc.ToLocalTime();
        return $"{local.Day}-{local.Month}-{local.Year} {local.Hour}-{local.Minute:D2}-{local.Second:D2}";
    }

    private string PendingDirectory => Path.Combine(_rootDirectory, _options.PendingDirectoryName);

    private string BackupRootDirectory =>
        !string.IsNullOrWhiteSpace(_options.BackupsRootDirectory)
            ? Path.GetFullPath(_options.BackupsRootDirectory)
            : Path.GetFullPath(Path.Combine(_rootDirectory, _options.BackupsDirectoryName));

    private bool UsesRootAsCurrentSaveSet => string.IsNullOrWhiteSpace(_options.CurrentDirectoryName);

    private string ResolveCurrentSaveSetRoot() =>
        UsesRootAsCurrentSaveSet
            ? _rootDirectory
            : Path.Combine(_rootDirectory, _options.CurrentDirectoryName);

    private bool CurrentSaveSetDirectoryHasEntries()
    {
        string current = CurrentSaveSetRoot;
        if (!Directory.Exists(current))
            return false;

        if (!UsesRootAsCurrentSaveSet)
            return true;

        foreach (string entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (IsCurrentRootInfrastructureEntry(entry))
                continue;

            return true;
        }

        return false;
    }

    private void DeleteCurrentSaveSet()
    {
        string current = CurrentSaveSetRoot;
        if (!Directory.Exists(current))
            return;

        if (!UsesRootAsCurrentSaveSet)
        {
            Directory.Delete(current, recursive: true);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (IsCurrentRootInfrastructureEntry(entry))
                continue;

            DeleteFileSystemEntry(entry);
        }
    }

    private void MoveCurrentSaveSetToBackup(string backupPath)
    {
        string current = CurrentSaveSetRoot;
        if (!UsesRootAsCurrentSaveSet)
        {
            Directory.Move(current, backupPath);
            return;
        }

        Directory.CreateDirectory(backupPath);
        foreach (string entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (IsCurrentRootInfrastructureEntry(entry))
                continue;

            MoveFileSystemEntry(entry, Path.Combine(backupPath, Path.GetFileName(entry)));
        }
    }

    private void PromotePendingSaveSetToCurrent(string pendingRoot)
    {
        string current = CurrentSaveSetRoot;
        if (!UsesRootAsCurrentSaveSet)
        {
            Directory.Move(pendingRoot, current);
            return;
        }

        Directory.CreateDirectory(current);
        foreach (string entry in Directory.EnumerateFileSystemEntries(pendingRoot))
        {
            string target = Path.Combine(current, Path.GetFileName(entry));
            DeleteFileSystemEntry(target);
            MoveFileSystemEntry(entry, target);
        }
        Directory.Delete(pendingRoot);
    }

    private bool IsCurrentRootInfrastructureEntry(string entry)
    {
        string fullEntry = Path.GetFullPath(entry);
        return PathsEqual(fullEntry, PendingDirectory) ||
            IsSameOrChildPath(fullEntry, BackupRootDirectory);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);

    private static bool IsSameOrChildPath(string path, string parent)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(fullPath, fullParent, StringComparison.Ordinal) ||
            fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void DeleteFileSystemEntry(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
            File.Delete(path);
    }

    private static void MoveFileSystemEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
            return;
        }

        File.Move(source, destination);
    }
}
