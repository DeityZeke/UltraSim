namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveResult(
    bool Succeeded,
    ServerPersistenceMetadata Metadata,
    ServerPersistenceSaveManifest? Manifest,
    IReadOnlyList<ServerPersistenceSaveFailure> Failures)
{
    public static ServerPersistenceSaveResult Success(
        ServerPersistenceMetadata metadata,
        ServerPersistenceSaveManifest manifest)
    {
        return new ServerPersistenceSaveResult(true, metadata, manifest, []);
    }

    public static ServerPersistenceSaveResult Failure(
        ServerPersistenceMetadata metadata,
        IReadOnlyList<ServerPersistenceSaveFailure> failures)
    {
        return new ServerPersistenceSaveResult(false, metadata, null, failures);
    }
}
