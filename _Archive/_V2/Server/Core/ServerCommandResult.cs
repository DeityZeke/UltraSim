namespace UltraSim.Server;

public readonly record struct ServerCommandResult(
    ServerCommandResultKind Kind,
    string Message)
{
    public static ServerCommandResult Accepted(string message) => new(ServerCommandResultKind.Accepted, message);
    public static ServerCommandResult Rejected(string message) => new(ServerCommandResultKind.Rejected, message);
    public static ServerCommandResult NoOp(string message) => new(ServerCommandResultKind.NoOp, message);
    public static ServerCommandResult Unknown(string message) => new(ServerCommandResultKind.Unknown, message);
}
