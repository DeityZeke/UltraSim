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
}
