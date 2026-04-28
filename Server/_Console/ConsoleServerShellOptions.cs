namespace UltraSim.Server.Console;

public sealed record ConsoleServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 16,
    int MaxCatchUpTicksPerPump = 8,
    bool AutoStart = true,
    bool SaveOnStop = false,
    bool EnableInput = true,
    TimeSpan? RunFor = null,
    TimeSpan IdleDelay = default,
    bool EnableMovementWorld = true,
    int NetworkPort = 42042,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0)
{
    public static ConsoleServerShellOptions Default { get; } =
        new("console", IdleDelay: TimeSpan.FromMilliseconds(1));
}
