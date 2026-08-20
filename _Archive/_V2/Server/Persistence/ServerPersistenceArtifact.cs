namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceArtifact(
    string Name,
    string RelativePath,
    string Kind = "",
    long SizeBytes = 0,
    int FileCount = 0);
