namespace UltraSim.Server.Hosting;

/// <summary>Severity used by the host diagnostics capability.</summary>
public enum HostLogLevel : byte
{
    Trace = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Explicit, typed host output capability.
/// </summary>
/// <remarks>
/// This is intentionally a sink rather than a general service lookup mechanism.
/// A missing sink is valid for an internal host and means that diagnostics are
/// simply not emitted.
/// </remarks>
public interface IHostLogSink
{
    void Write(HostLogLevel level, string message);
}

/// <summary>
/// Cold host capabilities; a missing diagnostics sink is valid.
/// </summary>
public readonly struct HostCapabilities
{
    public HostCapabilities(IHostLogSink? logSink = null)
    {
        LogSink = logSink;
    }

    public IHostLogSink? LogSink { get; }
}

/// <summary>Basic console diagnostics for headless process hosting.</summary>
public sealed class ConsoleHostLogSink : IHostLogSink
{
    public static ConsoleHostLogSink Instance { get; } = new();

    private ConsoleHostLogSink() { }

    public void Write(HostLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Console.WriteLine($"[{level}] {message}");
    }
}
