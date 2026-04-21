namespace UltraSim.Server;

public readonly record struct ServerAccountSummarySnapshot(
    string AccountId,
    string DisplayName,
    bool IsOnline,
    bool IsBanned,
    string? Detail = null);
