namespace UltraSim.Server;

public readonly record struct ServerControlCommand(
    ServerControlCommandKind Kind,
    string? Argument = null)
{
    public static ServerControlCommand Start() => new(ServerControlCommandKind.Start);
    public static ServerControlCommand Stop() => new(ServerControlCommandKind.Stop);
    public static ServerControlCommand Restart() => new(ServerControlCommandKind.Restart);
    public static ServerControlCommand Save() => new(ServerControlCommandKind.Save);
    public static ServerControlCommand Status() => new(ServerControlCommandKind.Status);
    public static ServerControlCommand Shutdown() => new(ServerControlCommandKind.Shutdown);
}
