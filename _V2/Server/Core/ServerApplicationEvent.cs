namespace UltraSim.Server;

public readonly record struct ServerApplicationEvent(
    ServerApplicationEventKind Kind,
    DateTimeOffset TimestampUtc,
    string Message,
    ServerApplicationState? State = null,
    TimeSpan? Duration = null);
