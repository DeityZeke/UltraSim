namespace UltraSim.Server.Console;

public static class ConsoleShellCommandParser
{
    public static ConsoleShellDirective Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.None);

        string command = line.Trim();

        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "?", StringComparison.OrdinalIgnoreCase))
        {
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.Help);
        }

        if (string.Equals(command, "start", StringComparison.OrdinalIgnoreCase))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.Start());

        if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.Status());

        if (string.Equals(command, "save", StringComparison.OrdinalIgnoreCase))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.Save());

        if (string.Equals(command, "restart", StringComparison.OrdinalIgnoreCase))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.Restart());

        if (IsNoSaveCommand(command, "restart"))
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.RestartWithoutSave());

        if (IsNoSaveCommand(command, "shutdown") ||
            IsNoSaveCommand(command, "quit") ||
            IsNoSaveCommand(command, "exit"))
        {
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.ShutdownWithoutSave());
        }

        if (string.Equals(command, "stop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "shutdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "quit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase))
        {
            return new ConsoleShellDirective(ConsoleShellDirectiveKind.ServerCommand, ServerControlCommand.Shutdown());
        }

        return new ConsoleShellDirective(
            ConsoleShellDirectiveKind.Invalid,
            Message: $"Unknown console command '{command}'. Type 'help' for the command list.");
    }

    private static bool IsNoSaveCommand(string command, string verb)
    {
        return string.Equals(command, $"{verb}-nosave", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, $"{verb}-no-save", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, $"{verb} nosave", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, $"{verb} no-save", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, $"{verb} without-save", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, $"{verb} --no-save", StringComparison.OrdinalIgnoreCase);
    }
}
