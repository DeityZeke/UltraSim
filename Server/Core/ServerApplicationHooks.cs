namespace UltraSim.Server;

public sealed class ServerApplicationHooks
{
    public Action<ServerApplication>? Initialize { get; init; }
    public Action<ServerApplication>? Start { get; init; }
    public Action<ServerApplication>? Load { get; init; }
    public Action<ServerApplication, float>? Tick { get; init; }
    public Action<ServerApplication>? Save { get; init; }
    public Action<ServerApplication>? Stop { get; init; }
    public Func<ServerApplication, bool>? IsAcceptingConnections { get; init; }
    public Func<ServerApplication, int>? GetActiveSessions { get; init; }
    public Func<ServerApplication, IReadOnlyList<ServerSystemRuntimeSnapshot>>? GetSystemRuntimeSnapshots { get; init; }
    public Func<ServerApplication, IReadOnlyList<ServerPlayerSessionSnapshot>>? GetPlayerSessions { get; init; }
    public Func<ServerApplication, IReadOnlyList<ServerAccountSummarySnapshot>>? GetAccountSummaries { get; init; }
    public Func<ServerApplication, string, ServerPlayerSessionActionResult>? KickPlayerSession { get; init; }
    public Func<ServerApplication, string, ServerAccountActionResult>? BanAccount { get; init; }
    public Func<ServerApplication, string, int, IReadOnlyList<string>, IReadOnlyList<string>, ServerAccountAccessUpdateResult>? ReplaceAccountAccess { get; init; }
    public Func<ServerApplication, ServerControlCommand, ServerCommandResult?>? ExecuteCommand { get; init; }
}
