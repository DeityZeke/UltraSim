namespace UltraSim.Server.Hosting;

public static class ServerHostLogFormatter
{
    public static IReadOnlyList<ServerHostLogLine> Format(ServerApplicationEvent evt)
    {
        if (evt.Kind == ServerApplicationEventKind.StateChanged)
            return FormatStateChanged(evt);

        if (evt.Kind == ServerApplicationEventKind.Faulted)
            return new[]
            {
                new ServerHostLogLine("FAULT", "Core: Runtime... failed", IsError: true),
                new ServerHostLogLine("FAULT", $"  {evt.Message}", IsError: true),
            };

        if (evt.Kind == ServerApplicationEventKind.SaveRequested)
            return new[] { new ServerHostLogLine("SAVE", "World: Saving...") };

        if (evt.Kind == ServerApplicationEventKind.SaveCompleted)
            return new[] { new ServerHostLogLine("SAVE", "World: Saving... - done") };

        if (evt.Kind == ServerApplicationEventKind.Log)
            return new[] { new ServerHostLogLine("LOG", evt.Message) };

        if (evt.Kind == ServerApplicationEventKind.CommandHandled)
            return Array.Empty<ServerHostLogLine>();

        return new[] { new ServerHostLogLine(evt.Kind.ToString().ToUpperInvariant(), evt.Message) };
    }

    private static IReadOnlyList<ServerHostLogLine> FormatStateChanged(ServerApplicationEvent evt)
    {
        return evt.State switch
        {
            ServerApplicationState.Initializing => new[] { new ServerHostLogLine("CORE", "Core: Initializing...") },
            ServerApplicationState.Initialized => new[] { new ServerHostLogLine("CORE", "Core: Initializing... - done") },
            ServerApplicationState.Starting => new[] { new ServerHostLogLine("CORE", "Core: Starting...") },
            ServerApplicationState.Loading => new[]
            {
                new ServerHostLogLine("CORE", "Core: Starting... - done"),
                new ServerHostLogLine("WORLD", "World: Loading..."),
            },
            ServerApplicationState.Running => new[] { new ServerHostLogLine("WORLD", "World: Loading... - done") },
            ServerApplicationState.Saving => new[] { new ServerHostLogLine("WORLD", "World: Saving...") },
            ServerApplicationState.Stopping => new[] { new ServerHostLogLine("CORE", "Core: Stopping...") },
            ServerApplicationState.Stopped => new[] { new ServerHostLogLine("CORE", "Core: Stopping... - done") },
            ServerApplicationState.Restarting => new[] { new ServerHostLogLine("CORE", "Core: Restarting...") },
            ServerApplicationState.Faulted => new[] { new ServerHostLogLine("FAULT", "Core: Runtime... failed", IsError: true) },
            _ => new[] { new ServerHostLogLine("STATE", evt.Message) },
        };
    }
}
