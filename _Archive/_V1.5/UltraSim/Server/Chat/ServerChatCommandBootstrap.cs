using System;

namespace UltraSim.Server.Chat
{
    public static class ServerChatCommandBootstrap
    {
        public static ServerChatCommandRegistry CreateDefault(Action<string>? serverLog = null)
        {
            var registry = new ServerChatCommandRegistry();
            registry.AddSymbol('[');

            registry.Register(
                name: "help",
                symbol: '/',
                helpText: "List available chat commands.",
                handler: (in ChatCommandContext context, in ChatCommandInvocation _) =>
                {
                    string output = registry.BuildHelp('/');
                    context.Reply?.Invoke(string.IsNullOrWhiteSpace(output) ? "No commands registered." : output);
                });

            registry.Register(
                name: "ping",
                symbol: '/',
                helpText: "Server command path sanity check.",
                handler: (in ChatCommandContext context, in ChatCommandInvocation _) =>
                {
                    context.Reply?.Invoke("pong");
                });

            registry.Register(
                name: "sayas",
                symbol: '[',
                minAccessLevel: 100,
                helpText: "Admin-only: [sayas <name> <text>",
                handler: (in ChatCommandContext context, in ChatCommandInvocation invocation) =>
                {
                    if (invocation.Args.Count < 2)
                    {
                        context.Reply?.Invoke("Usage: [sayas <name> <text>");
                        return;
                    }

                    string alias = invocation.Args[0];
                    string text = string.Join(" ", invocation.Args).Substring(alias.Length + 1);
                    serverLog?.Invoke($"cmd_sayas by={context.SenderName} alias={alias} text={text}");
                });

            return registry;
        }
    }
}
