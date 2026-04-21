namespace UltraSim.Server.Console;

public sealed record ConsoleServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 50,
    int MaxCatchUpTicksPerPump = 8,
    bool AutoStart = true,
    bool SaveOnStop = false,
    bool EnableInput = true,
    TimeSpan? RunFor = null,
    TimeSpan IdleDelay = default)
{
    public static ConsoleServerShellOptions Default { get; } =
        new("console", IdleDelay: TimeSpan.FromMilliseconds(1));
}
