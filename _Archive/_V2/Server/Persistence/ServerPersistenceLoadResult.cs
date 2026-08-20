namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceLoadResult(
    bool Succeeded,
    bool StartedFresh,
    bool LoadedBackup,
    string? SaveSetRoot,
    ServerPersistenceSaveManifest? Manifest,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ServerPersistenceSaveFailure> Failures,
    ServerPersistenceTiming Timing)
{
    public static ServerPersistenceLoadResult Fresh(
        IReadOnlyList<string>? warnings = null,
        ServerPersistenceTiming? timing = null)
    {
        return new ServerPersistenceLoadResult(
            Succeeded: true,
            StartedFresh: true,
            LoadedBackup: false,
            SaveSetRoot: null,
            Manifest: null,
            Warnings: warnings ?? [],
            Failures: [],
            Timing: timing ?? ServerPersistenceTiming.Empty);
    }

    public static ServerPersistenceLoadResult Loaded(
        string saveSetRoot,
        ServerPersistenceSaveManifest manifest,
        bool loadedBackup,
        IReadOnlyList<string>? warnings = null,
        ServerPersistenceTiming? timing = null)
    {
        return new ServerPersistenceLoadResult(
            Succeeded: true,
            StartedFresh: false,
            LoadedBackup: loadedBackup,
            SaveSetRoot: saveSetRoot,
            Manifest: manifest,
            Warnings: warnings ?? [],
            Failures: [],
            Timing: timing ?? ServerPersistenceTiming.Empty);
    }

    public static ServerPersistenceLoadResult Failure(
        IReadOnlyList<ServerPersistenceSaveFailure> failures,
        IReadOnlyList<string>? warnings = null,
        ServerPersistenceTiming? timing = null)
    {
        return new ServerPersistenceLoadResult(
            Succeeded: false,
            StartedFresh: false,
            LoadedBackup: false,
            SaveSetRoot: null,
            Manifest: null,
            Warnings: warnings ?? [],
            Failures: failures,
            Timing: timing ?? ServerPersistenceTiming.Empty);
    }
}
