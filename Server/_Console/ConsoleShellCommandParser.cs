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
}
