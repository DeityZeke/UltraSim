namespace UltraSim.Server.Persistence;

public sealed record ServerPersistenceSaveFailure(
    string ParticipantName,
    string Message);
