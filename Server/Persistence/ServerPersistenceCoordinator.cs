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

    public string CurrentSaveSetRoot => Path.Combine(_rootDirectory, _options.CurrentDirectoryName);

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
        string pendingDirectory = Path.Combine(_rootDirectory, _options.PendingDirectoryName);
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
        if (Directory.Exists(current))
        {
            if (_options.ClampedBackupCount == 0)
            {
                Directory.Delete(current, recursive: true);
            }
            else
            {
                string backupRoot = Path.Combine(_rootDirectory, _options.BackupsDirectoryName);
                Directory.CreateDirectory(backupRoot);
                string backupName = $"save-before-{metadata.SaveSequence:D20}-{metadata.SavedUtc:yyyyMMddHHmmssfffffff}";
                Directory.Move(current, Path.Combine(backupRoot, backupName));
                PruneBackups(backupRoot);
            }
        }

        Directory.Move(pendingRoot, current);
        DeleteEmptyParentDirectory(pendingRoot);
    }

    private void PruneBackups(string backupRoot)
    {
        int backupCount = _options.ClampedBackupCount;
        if (backupCount < 0)
            return;

        string[] backups = Directory.EnumerateDirectories(backupRoot, "save-*")
            .OrderByDescending(static value => value, StringComparer.Ordinal)
            .ToArray();
        for (int i = backupCount; i < backups.Length; i++)
            Directory.Delete(backups[i], recursive: true);
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
        else if (Directory.Exists(CurrentSaveSetRoot))
        {
            warnings.Add($"Current save-set is not loadable: {currentError}");
        }

        string backupRoot = Path.Combine(_rootDirectory, _options.BackupsDirectoryName);
        if (!Directory.Exists(backupRoot))
            return candidates;

        foreach (string backupDirectory in Directory.EnumerateDirectories(backupRoot, "save-*"))
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
        string pendingDirectory = Path.Combine(_rootDirectory, _options.PendingDirectoryName);
        if (!Directory.Exists(pendingDirectory))
            return;

        Directory.Delete(pendingDirectory, recursive: true);
        warnings.Add("Removed pending save-set directory left by an interrupted save.");
    }

    private sealed record SaveSetCandidate(
        string Root,
        ServerPersistenceSaveManifest Manifest,
        bool IsBackup);
}
