namespace UltraSim.Server.Console;

public readonly record struct ConsoleShellDirective(
    ConsoleShellDirectiveKind Kind,
    ServerControlCommand Command = default,
    string? Message = null);
