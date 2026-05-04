namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveResult(
    bool Succeeded,
    ServerPersistenceMetadata Metadata,
    ServerPersistenceSaveManifest? Manifest,
    IReadOnlyList<ServerPersistenceSaveFailure> Failures,
    ServerPersistenceTiming Timing)
{
    public static ServerPersistenceSaveResult Success(
        ServerPersistenceMetadata metadata,
        ServerPersistenceSaveManifest manifest,
        ServerPersistenceTiming timing)
    {
        return new ServerPersistenceSaveResult(true, metadata, manifest, [], timing);
    }

    public static ServerPersistenceSaveResult Failure(
        ServerPersistenceMetadata metadata,
        IReadOnlyList<ServerPersistenceSaveFailure> failures,
        ServerPersistenceTiming? timing = null)
    {
        return new ServerPersistenceSaveResult(false, metadata, null, failures, timing ?? ServerPersistenceTiming.Empty);
    }
}
