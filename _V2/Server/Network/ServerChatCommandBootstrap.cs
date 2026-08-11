namespace UltraSim.Server.Network;

public static class ServerChatCommandBootstrap
{
    public static void RegisterDefaults(
        ServerCommandRegistry registry,
        ServerChatService chat,
        string commandPrefix = "/")
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(chat);
        if (string.IsNullOrWhiteSpace(commandPrefix))
            throw new ArgumentException("Command prefix cannot be empty.", nameof(commandPrefix));

        string prefix = commandPrefix.Trim();
        registry.AddPrefix(prefix);

        registry.Register(
            "help",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                string help = registry.BuildHelp(invocation.Prefix);
                request.Reply?.Invoke(string.IsNullOrWhiteSpace(help) ? "No commands registered." : help);
            },
            prefix: prefix,
            helpText: "List available commands.");

        registry.Register(
            "ping",
            (in ServerCommandRequest request, ServerTextCommandInvocation _) =>
            {
                request.Reply?.Invoke("pong");
            },
            prefix: prefix,
            helpText: "Check the command path.");

        registry.Register(
            "announce",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                if (invocation.Arguments.Count == 0)
                {
                    request.Reply?.Invoke("Usage: announce \"message\"");
                    return;
                }

                chat.PublishSystemAnnouncement(string.Join(' ', invocation.Arguments));
            },
            prefix: "",
            minAccessLevel: 100,
            helpText: "Publish a server announcement.");
    }
}
