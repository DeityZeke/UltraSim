namespace UltraSim.Server.Hosting;

public readonly record struct ServerHostLogLine(
    string Category,
    string Message,
    bool IsError = false);
