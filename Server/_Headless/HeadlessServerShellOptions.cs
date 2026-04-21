namespace UltraSim.Server.Headless;

public sealed record HeadlessServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 50,
    int MaxCatchUpTicksPerPump = 8,
    bool AutoStart = true,
    bool SaveOnStop = false,
    bool Quiet = false,
    TimeSpan? RunFor = null,
    TimeSpan IdleDelay = default)
{
    public static HeadlessServerShellOptions Default { get; } =
        new("headless", IdleDelay: TimeSpan.FromMilliseconds(1));
}
