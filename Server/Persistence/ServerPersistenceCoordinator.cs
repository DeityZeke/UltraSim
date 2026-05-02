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
        if (!File.Exists(path))
        {
            manifest = default!;
            return false;
        }

        manifest = ReadManifest(path);
        return true;
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void DeletePendingSaveSet(string pendingRoot)
    {
        DeleteDirectoryIfExists(pendingRoot);
        string? parent = Path.GetDirectoryName(pendingRoot);
        if (!string.IsNullOrWhiteSpace(parent) &&
            Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }
}
