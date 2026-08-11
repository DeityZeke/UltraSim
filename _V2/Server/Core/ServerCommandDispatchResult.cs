namespace UltraSim.Server;

public readonly record struct ServerCommandDispatchResult(
    ServerCommandDispatchStatus Status,
    string CommandName = "",
    string Error = "")
{
    public bool IsHandled => Status != ServerCommandDispatchStatus.NotCommand;

    public static ServerCommandDispatchResult NotCommand() => new(ServerCommandDispatchStatus.NotCommand);

    public static ServerCommandDispatchResult Executed(string commandName) =>
        new(ServerCommandDispatchStatus.Executed, commandName);

    public static ServerCommandDispatchResult InvalidSyntax(string error) =>
        new(ServerCommandDispatchStatus.InvalidSyntax, Error: error);

    public static ServerCommandDispatchResult UnknownCommand(string commandName, string error) =>
        new(ServerCommandDispatchStatus.UnknownCommand, commandName, error);

    public static ServerCommandDispatchResult PermissionDenied(string commandName) =>
        new(ServerCommandDispatchStatus.PermissionDenied, commandName, "Permission denied.");

    public static ServerCommandDispatchResult HandlerError(string commandName, string error) =>
        new(ServerCommandDispatchStatus.HandlerError, commandName, error);
}
