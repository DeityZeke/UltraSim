namespace UltraSim.Server.Network;

public static class ServerChatCommandBootstrap
{
    public static void RegisterDefaults(ServerCommandRegistry registry, ServerChatService chat)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(chat);

        registry.AddPrefix("[");

        registry.Register(
            "help",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                string help = registry.BuildHelp(invocation.Prefix);
                request.Reply?.Invoke(string.IsNullOrWhiteSpace(help) ? "No commands registered." : help);
            },
            prefix: "/",
            helpText: "List available commands.");

        registry.Register(
            "ping",
            (in ServerCommandRequest request, ServerTextCommandInvocation _) =>
            {
                request.Reply?.Invoke("pong");
            },
            prefix: "/",
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
