namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptLogEntry(
    UltraSimScriptLogLevel Level,
    string Message,
    DateTimeOffset TimestampUtc);
