namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveManifest(
    int Version,
    ulong SaveSequence,
    DateTimeOffset SavedUtc,
    IReadOnlyList<ServerPersistenceArtifact> Artifacts)
{
    public const int CurrentVersion = 1;

    public ServerPersistenceMetadata Metadata => new(SaveSequence, SavedUtc);
}
