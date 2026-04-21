namespace UltraSim.Server;

public readonly record struct ServerPlayerSessionSnapshot(
    string SessionId,
    string PlayerName,
    string AccountId,
    string SessionState,
    string? Detail = null);
