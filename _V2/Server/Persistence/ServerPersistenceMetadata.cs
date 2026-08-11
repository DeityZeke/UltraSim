namespace UltraSim.Server.Persistence;

public readonly record struct ServerPersistenceMetadata(
    ulong SaveSequence,
    DateTimeOffset SavedUtc);
