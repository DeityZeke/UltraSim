namespace UltraSim.Server;

public static class ServerCommandPermissionKeys
{
    public const string Prefix = "command.";

    public static string ForCommandName(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));

        string normalized = commandName.Trim().ToLowerInvariant();
        for (int i = 0; i < normalized.Length; i++)
        {
            if (char.IsWhiteSpace(normalized[i]))
                throw new ArgumentException("Command name cannot contain whitespace.", nameof(commandName));
        }

        return Prefix + normalized;
    }
}
