namespace UltraSim.Server;

public enum ServerCommandDispatchStatus : byte
{
    NotCommand = 0,
    Executed = 1,
    UnknownCommand = 2,
    InvalidSyntax = 3,
    PermissionDenied = 4,
    HandlerError = 5,
}
