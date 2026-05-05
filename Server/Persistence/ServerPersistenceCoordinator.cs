using System.Diagnostics;
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
        var totalStopwatch = Stopwatch.StartNew();
        string pendingRoot = CreatePendingSaveSetRoot(metadata);
        var artifacts = new List<ServerPersistenceArtifact>(_participants.Length);
        var failures = new List<ServerPersistenceSaveFailure>();
        var timings = new List<ServerPersistenceParticipantTiming>(_participants.Length);

        try
        {
            Directory.CreateDirectory(pendingRoot);
            for (int i = 0; i < _participants.Length; i++)
            {
                IServerPersistenceParticipant participant = _participants[i];
                try
                {
                    var participantStopwatch = Stopwatch.StartNew();
                    ServerPersistenceArtifact artifact = participant.Save(pendingRoot, metadata);
                    participantStopwatch.Stop();

                    ServerPersistenceArtifact measuredArtifact = MeasureArtifact(pendingRoot, artifact);
                    artifacts.Add(measuredArtifact);
                    timings.Add(new ServerPersistenceParticipantTiming(
                        participant.Name,
                        TicksToMilliseconds(participantStopwatch.ElapsedTicks),
                        measuredArtifact.SizeBytes,
                        measuredArtifact.FileCount));
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
                totalStopwatch.Stop();
                return ServerPersistenceSaveResult.Failure(
                    metadata,
                    failures,
                    new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), timings));
            }

            var manifest = new ServerPersistenceSaveManifest(
                ServerPersistenceSaveManifest.CurrentVersion,
                metadata.SaveSequence,
                metadata.SavedUtc,
                artifacts);
            WriteManifest(pendingRoot, manifest);
            PromotePendingSaveSet(pendingRoot, metadata);
            totalStopwatch.Stop();
            return ServerPersistenceSaveResult.Success(
                metadata,
                manifest,
                new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), timings));
        }
        catch (Exception ex)
        {
            DeletePendingSaveSet(pendingRoot);
            failures.Add(new ServerPersistenceSaveFailure("coordinator", ex.Message));
            totalStopwatch.Stop();
            return ServerPersistenceSaveResult.Failure(
                metadata,
                failures,
                new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), timings));
        }
    }

    public bool TryLoadCurrentManifest(out ServerPersistenceSaveManifest manifest)
    {
        string path = Path.Combine(CurrentSaveSetRoot, "save_manifest.json");
        return TryReadManifest(path, out manifest, out _);
    }

    public IReadOnlyList<ServerPersistenceBackupSnapshot> ListBackups()
    {
        string backupRoot = BackupRootDirectory;
        if (!Directory.Exists(backupRoot))
            return [];

        var backups = new List<ServerPersistenceBackupSnapshot>();
        foreach (string backupPath in EnumerateBackupEntries(backupRoot))
        {
            if (!TryReadBackupManifest(backupPath, out ServerPersistenceSaveManifest manifest, out _))
                continue;

            backups.Add(new ServerPersistenceBackupSnapshot(
                Path.GetFileName(backupPath),
                backupPath,
                manifest));
        }

        return backups
            .OrderByDescending(static backup => backup.Manifest.SaveSequence)
            .ToArray();
    }

    public ServerPersistenceRestoreResult RestoreBackup(
        string backupDirectoryName,
        DateTimeOffset restoredUtc)
    {
        var warnings = new List<string>();
        string pendingRoot = string.Empty;
        try
        {
            string backupPath = ResolveBackupDirectoryPath(backupDirectoryName);
            if (!TryReadBackupManifest(backupPath, out ServerPersistenceSaveManifest manifest, out string manifestError))
            {
                return ServerPersistenceRestoreResult.Failure(
                    backupDirectoryName,
                    [new ServerPersistenceSaveFailure("coordinator", manifestError)],
                    warnings);
            }

            pendingRoot = CreateRestorePendingRoot();
            if (IsBackupArchivePath(backupPath))
                _options.BackupCompressor!.ExtractToDirectory(backupPath, pendingRoot);
            else
                CopyDirectory(backupPath, pendingRoot);

            ValidateBackupArtifacts(pendingRoot, manifest);

            string? safetyBackupRoot = null;
            if (CurrentSaveSetDirectoryHasEntries())
            {
                Directory.CreateDirectory(BackupRootDirectory);
                safetyBackupRoot = CreatePreRestoreBackupPath(BackupRootDirectory, restoredUtc);
                MoveCurrentSaveSetToBackup(safetyBackupRoot);
            }

            PromotePendingSaveSetToCurrent(pendingRoot);
            pendingRoot = string.Empty;
            _nextSaveSequence = Math.Max(_nextSaveSequence, manifest.SaveSequence);

            return ServerPersistenceRestoreResult.Success(
                backupDirectoryName,
                CurrentSaveSetRoot,
                safetyBackupRoot,
                manifest,
                warnings);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(pendingRoot))
                DeletePendingSaveSet(pendingRoot);

            return ServerPersistenceRestoreResult.Failure(
                backupDirectoryName,
                [new ServerPersistenceSaveFailure("coordinator", ex.Message)],
                warnings);
        }
    }

    public ServerPersistenceLoadResult Load()
    {
        var totalStopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        CleanupPendingSaveSets(warnings);

        bool currentHasEntries = CurrentSaveSetDirectoryHasEntries();
        List<SaveSetCandidate> candidates = CollectLoadCandidates(warnings);
        SaveSetCandidate? current = candidates.FirstOrDefault(static candidate => !candidate.IsBackup);
        SaveSetCandidate[] loadCandidates = ResolveLoadCandidates(candidates, current, currentHasEntries, warnings);
        if (loadCandidates.Length == 0)
        {
            totalStopwatch.Stop();
            if (ShouldFailWhenNoLoadCandidates(currentHasEntries, candidates))
            {
                return ServerPersistenceLoadResult.Failure(
                    [new ServerPersistenceSaveFailure("coordinator", BuildNoLoadCandidateFailureMessage())],
                    warnings,
                    new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), []));
            }

            return ServerPersistenceLoadResult.Fresh(
                warnings,
                new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), []));
        }

        if (_options.RecoveryMode == ServerPersistenceRecoveryMode.CurrentThenBackups && current is not null)
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

        foreach (SaveSetCandidate candidate in loadCandidates)
        {
            if (TryLoadCandidate(candidate, warnings, totalStopwatch, out ServerPersistenceLoadResult result))
            {
                _nextSaveSequence = Math.Max(_nextSaveSequence, candidate.Manifest.SaveSequence);
                return result;
            }
        }

        totalStopwatch.Stop();
        return ServerPersistenceLoadResult.Failure(
            [new ServerPersistenceSaveFailure("coordinator", "No valid save-set could be loaded.")],
            warnings,
            new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), []));
    }

    private SaveSetCandidate[] ResolveLoadCandidates(
        List<SaveSetCandidate> candidates,
        SaveSetCandidate? current,
        bool currentHasEntries,
        List<string> warnings)
    {
        int backupCount = candidates.Count(static candidate => candidate.IsBackup);
        return _options.RecoveryMode switch
        {
            ServerPersistenceRecoveryMode.CurrentOnly or ServerPersistenceRecoveryMode.RequireCurrent
                when current is not null => [current],

            ServerPersistenceRecoveryMode.CurrentOnly => ResolveCurrentOnlyWithoutCurrent(backupCount, warnings),

            ServerPersistenceRecoveryMode.RequireCurrent => [],
            _ => candidates.ToArray(),
        };
    }

    private static SaveSetCandidate[] ResolveCurrentOnlyWithoutCurrent(int backupCount, List<string> warnings)
    {
        if (backupCount > 0)
        {
            warnings.Add(
                $"Recovery mode '{ServerPersistenceRecoveryMode.CurrentOnly}' ignored {backupCount.ToString(CultureInfo.InvariantCulture)} backup save-set(s).");
        }

        return [];
    }

    private bool ShouldFailWhenNoLoadCandidates(
        bool currentHasEntries,
        List<SaveSetCandidate> candidates)
    {
        return _options.RecoveryMode == ServerPersistenceRecoveryMode.RequireCurrent ||
            currentHasEntries ||
            (_options.RecoveryMode == ServerPersistenceRecoveryMode.CurrentOnly &&
                candidates.Any(static candidate => candidate.IsBackup));
    }

    private string BuildNoLoadCandidateFailureMessage()
    {
        return _options.RecoveryMode switch
        {
            ServerPersistenceRecoveryMode.CurrentOnly =>
                "Recovery mode 'CurrentOnly' did not find a loadable current save-set.",
            ServerPersistenceRecoveryMode.RequireCurrent =>
                "Recovery mode 'RequireCurrent' requires a loadable current save-set.",
            _ => "No valid save-set could be loaded.",
        };
    }

    private string ResolveBackupDirectoryPath(string backupDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(backupDirectoryName))
            throw new ArgumentException("Backup directory name is required.", nameof(backupDirectoryName));
        if (Path.IsPathRooted(backupDirectoryName) ||
            backupDirectoryName.Contains(Path.DirectorySeparatorChar) ||
            backupDirectoryName.Contains(Path.AltDirectorySeparatorChar) ||
            backupDirectoryName.Contains('\\') ||
            backupDirectoryName is "." or "..")
        {
            throw new InvalidOperationException($"Backup directory name '{backupDirectoryName}' is not safe.");
        }

        string backupRoot = BackupRootDirectory;
        string backupPath = Path.GetFullPath(Path.Combine(backupRoot, backupDirectoryName));
        if (!IsSameOrChildPath(backupPath, backupRoot) || PathsEqual(backupPath, backupRoot))
            throw new InvalidOperationException($"Backup directory name '{backupDirectoryName}' is not safe.");
        if (!Directory.Exists(backupPath) && !File.Exists(backupPath))
            throw new DirectoryNotFoundException($"Backup save-set '{backupDirectoryName}' was not found.");

        return backupPath;
    }

    private static void ValidateBackupArtifacts(
        string backupRoot,
        ServerPersistenceSaveManifest manifest)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < manifest.Artifacts.Count; i++)
        {
            ServerPersistenceArtifact artifact = manifest.Artifacts[i];
            if (!names.Add(artifact.Name))
                throw new InvalidOperationException($"Backup save-set has duplicate participant artifact '{artifact.Name}'.");

            _ = MeasureArtifact(backupRoot, artifact);
        }
    }

    private string CreateRestorePendingRoot()
    {
        string pendingDirectory = PendingDirectory;
        Directory.CreateDirectory(pendingDirectory);
        string path = Path.Combine(pendingDirectory, $"restore-{Guid.NewGuid():N}");
        DeleteDirectoryIfExists(path);
        return path;
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

    private bool TryReadBackupManifest(
        string backupPath,
        out ServerPersistenceSaveManifest manifest,
        out string error)
    {
        if (IsBackupArchivePath(backupPath))
            return TryReadArchiveManifest(backupPath, out manifest, out error);

        return TryReadManifest(Path.Combine(backupPath, "save_manifest.json"), out manifest, out error);
    }

    private bool TryReadArchiveManifest(
        string archivePath,
        out ServerPersistenceSaveManifest manifest,
        out string error)
    {
        manifest = default!;
        error = string.Empty;
        string extractionRoot = string.Empty;
        try
        {
            extractionRoot = CreateRestorePendingRoot();
            _options.BackupCompressor!.ExtractToDirectory(archivePath, extractionRoot);
            return TryReadManifest(Path.Combine(extractionRoot, "save_manifest.json"), out manifest, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(extractionRoot))
                DeletePendingSaveSet(extractionRoot);
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

        foreach (string backupPath in EnumerateBackupEntries(backupRoot))
        {
            if (TryReadBackupManifest(backupPath, out ServerPersistenceSaveManifest backupManifest, out string backupError))
            {
                candidates.Add(new SaveSetCandidate(backupPath, backupManifest, IsBackup: true));
            }
            else
            {
                warnings.Add($"Backup save-set '{backupPath}' is not loadable: {backupError}");
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
        Stopwatch totalStopwatch,
        out ServerPersistenceLoadResult result)
    {
        var timings = new List<ServerPersistenceParticipantTiming>(_participants.Length);
        Dictionary<string, ServerPersistenceArtifact> artifactsByName;
        string loadRoot = candidate.Root;
        string extractionRoot = string.Empty;
        try
        {
            if (candidate.IsBackup && IsBackupArchivePath(candidate.Root))
            {
                extractionRoot = CreateRestorePendingRoot();
                _options.BackupCompressor!.ExtractToDirectory(candidate.Root, extractionRoot);
                loadRoot = extractionRoot;
            }

            artifactsByName = candidate.Manifest.Artifacts.ToDictionary(
                static artifact => artifact.Name,
                StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(extractionRoot))
                DeletePendingSaveSet(extractionRoot);
            warnings.Add($"Save-set '{candidate.Root}' could not be prepared for load: {ex.Message}");
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
                ServerPersistenceArtifact measuredArtifact = MeasureArtifact(loadRoot, artifact);
                var participantStopwatch = Stopwatch.StartNew();
                participant.Load(loadRoot, candidate.Manifest.Metadata, measuredArtifact);
                participantStopwatch.Stop();
                timings.Add(new ServerPersistenceParticipantTiming(
                    participant.Name,
                    TicksToMilliseconds(participantStopwatch.ElapsedTicks),
                    measuredArtifact.SizeBytes,
                    measuredArtifact.FileCount));
            }
            catch (Exception ex)
            {
                warnings.Add($"Save-set '{candidate.Root}' failed to load participant '{participant.Name}': {ex.Message}");
                result = default!;
                if (!string.IsNullOrWhiteSpace(extractionRoot))
                    DeletePendingSaveSet(extractionRoot);
                return false;
            }
        }

        totalStopwatch.Stop();
        result = ServerPersistenceLoadResult.Loaded(
            candidate.Root,
            candidate.Manifest,
            candidate.IsBackup,
            warnings,
            new ServerPersistenceTiming(TicksToMilliseconds(totalStopwatch.ElapsedTicks), timings));
        if (!string.IsNullOrWhiteSpace(extractionRoot))
            DeletePendingSaveSet(extractionRoot);
        return true;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static ServerPersistenceArtifact MeasureArtifact(string saveSetRoot, ServerPersistenceArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.Name))
            throw new InvalidOperationException("Persistence artifact name is required.");
        if (string.IsNullOrWhiteSpace(artifact.RelativePath))
            throw new InvalidOperationException($"Persistence artifact '{artifact.Name}' has no relative path.");

        string fullPath = ResolveArtifactPath(saveSetRoot, artifact.RelativePath);
        string kind;
        long sizeBytes;
        int fileCount;

        if (File.Exists(fullPath))
        {
            kind = "file";
            sizeBytes = new FileInfo(fullPath).Length;
            fileCount = 1;
        }
        else if (Directory.Exists(fullPath))
        {
            kind = "directory";
            sizeBytes = 0;
            fileCount = 0;
            foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                sizeBytes += new FileInfo(file).Length;
                fileCount++;
            }
        }
        else
        {
            throw new FileNotFoundException(
                $"Persistence artifact '{artifact.Name}' was not found under the save-set.",
                fullPath);
        }

        if (!string.IsNullOrWhiteSpace(artifact.Kind) &&
            !string.Equals(artifact.Kind, kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Persistence artifact '{artifact.Name}' expected kind '{artifact.Kind}' but found '{kind}'.");
        }

        if (artifact.SizeBytes > 0 && artifact.SizeBytes != sizeBytes)
        {
            throw new InvalidOperationException(
                $"Persistence artifact '{artifact.Name}' expected {artifact.SizeBytes} bytes but found {sizeBytes} bytes.");
        }

        if (artifact.FileCount > 0 && artifact.FileCount != fileCount)
        {
            throw new InvalidOperationException(
                $"Persistence artifact '{artifact.Name}' expected {artifact.FileCount} files but found {fileCount} files.");
        }

        return artifact with
        {
            RelativePath = NormalizeArtifactRelativePath(artifact.RelativePath),
            Kind = kind,
            SizeBytes = sizeBytes,
            FileCount = fileCount,
        };
    }

    private static string ResolveArtifactPath(string saveSetRoot, string relativePath)
    {
        string normalized = NormalizeArtifactRelativePath(relativePath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveSetRoot));
        string fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsSameOrChildPath(fullPath, root))
            throw new InvalidOperationException($"Persistence artifact path '{relativePath}' escapes the save-set root.");

        return fullPath;
    }

    private static string NormalizeArtifactRelativePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(static part => part is "." or ".." or ""))
        {
            throw new InvalidOperationException($"Persistence artifact path '{relativePath}' is not a safe relative path.");
        }

        return normalized;
    }

    private static double TicksToMilliseconds(long elapsedTicks)
    {
        return elapsedTicks <= 0 ? 0d : elapsedTicks * 1000d / Stopwatch.Frequency;
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

        string[] existing = EnumerateBackupEntries(backupRoot).ToArray();
        for (int i = 0; i < slots.Length; i++)
        {
            string? existingPath = MatchBackupSlot(existing, slots[i]);
            if (existingPath is null)
                continue;

            if (i == 0)
            {
                try { DeleteFileSystemEntry(existingPath); }
                catch { }
                continue;
            }

            string existingTimeStamp = FindBackupTimestamp(Path.GetFileName(existingPath)) ?? timeStamp;
            string target = FormatBackupPath(backupRoot, slots[i - 1], existingTimeStamp);
            DeleteFileSystemEntry(target);
            try { MoveFileSystemEntry(existingPath, target); }
            catch { }
        }

        return FormatBackupPath(backupRoot, slots[^1], timeStamp);
    }

    private string CreatePreRestoreBackupPath(string backupRoot, DateTimeOffset restoredUtc)
    {
        string path = FormatBackupPath(backupRoot, "Pre-Restore Backup", FormatBackupTimestamp(restoredUtc));
        if (!Directory.Exists(path) && !File.Exists(path))
            return path;

        for (int i = 2; ; i++)
        {
            string suffix = $" #{i.ToString(CultureInfo.InvariantCulture)}";
            string candidate = _options.BackupCompressor is null
                ? path + suffix
                : Path.Combine(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    StripBackupExtension(Path.GetFileName(path), _options.BackupCompressor.FileExtension) +
                    suffix +
                    NormalizeBackupExtension(_options.BackupCompressor.FileExtension));
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }
    }

    private static string[] GetBackupSlotNames(int backupCount) =>
        backupCount <= 1
            ? ["Most Recent"]
            : backupCount == 2
                ? ["Second Backup", "Most Recent"]
                : ["Third Backup", "Second Backup", "Most Recent"];

    private static string? MatchBackupSlot(string[] paths, string slotName)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (Path.GetFileName(paths[i]).StartsWith(slotName, StringComparison.Ordinal))
                return paths[i];
        }

        return null;
    }

    private string FormatBackupPath(string root, string name, string timeStamp)
    {
        string path = Path.Combine(root, $"{name} ({timeStamp})");
        return _options.BackupCompressor is null ? path : path + NormalizeBackupExtension(_options.BackupCompressor.FileExtension);
    }

    private IEnumerable<string> EnumerateBackupEntries(string backupRoot)
    {
        foreach (string directory in Directory.EnumerateDirectories(backupRoot))
            yield return directory;

        if (_options.BackupCompressor is null)
            yield break;

        string extension = NormalizeBackupExtension(_options.BackupCompressor.FileExtension);
        foreach (string file in Directory.EnumerateFiles(backupRoot, "*" + extension))
            yield return file;
    }

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

    private bool IsBackupArchivePath(string path)
    {
        if (_options.BackupCompressor is null || string.IsNullOrWhiteSpace(path))
            return false;

        string extension = NormalizeBackupExtension(_options.BackupCompressor.FileExtension);
        return File.Exists(path) &&
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripBackupExtension(string fileName, string extension)
    {
        string normalizedExtension = NormalizeBackupExtension(extension);
        return fileName.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^normalizedExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static string NormalizeBackupExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("Backup compressor file extension is required.");

        string normalized = extension.Trim();
        return normalized.StartsWith(".", StringComparison.Ordinal)
            ? normalized
            : "." + normalized;
    }

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
        if (_options.BackupCompressor is not null)
        {
            string source = current;
            bool deleteSource = true;
            if (UsesRootAsCurrentSaveSet)
            {
                source = Path.Combine(PendingDirectory, $"backup-source-{Guid.NewGuid():N}");
                Directory.CreateDirectory(source);
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (IsCurrentRootInfrastructureEntry(entry))
                        continue;

                    MoveFileSystemEntry(entry, Path.Combine(source, Path.GetFileName(entry)));
                }
            }

            _options.BackupCompressor.CompressDirectory(source, backupPath);
            if (deleteSource)
                DeleteDirectoryIfExists(source);
            DeleteEmptyParentDirectory(source);
            return;
        }

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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            File.Copy(file, target, overwrite: true);
        }
    }
}
