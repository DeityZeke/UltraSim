namespace UltraSim.Server.Hosting;

public sealed record ServerHostLoopOptions(
    TimeSpan TickInterval,
    int MaxCatchUpTicksPerPump = 8)
{
    public static ServerHostLoopOptions Default { get; } = new(TimeSpan.FromMilliseconds(16));
}
