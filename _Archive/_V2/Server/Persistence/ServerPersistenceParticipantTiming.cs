namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceParticipantTiming(
    string ParticipantName,
    double ElapsedMilliseconds,
    long SizeBytes,
    int FileCount);
