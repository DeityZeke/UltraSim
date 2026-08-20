namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceTiming(
    double TotalMilliseconds,
    IReadOnlyList<ServerPersistenceParticipantTiming> Participants)
{
    public static ServerPersistenceTiming Empty { get; } = new(0d, []);
}
