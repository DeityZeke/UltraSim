using UltraSim.ECS;

namespace UltraSim.Server.Runtime;

/// <summary>Cold configuration for one concrete authoritative server runtime.</summary>
public sealed class ServerRuntimeOptions
{
    public ServerRuntimeOptions(
        EcsWorldOptions worldOptions,
        WorldClockOptions clockOptions,
        bool allowVariablePlayback = false,
        bool allowVariableSimulationFrequency = false)
    {
        if (!worldOptions.IsValid)
            throw new ArgumentException("ECS world options are invalid or default initialized.", nameof(worldOptions));
        if (!worldOptions.Scheduler.IsEnabled)
            throw new ArgumentException("A server runtime requires an enabled ECS scheduler.", nameof(worldOptions));
        if (!clockOptions.IsValid)
            throw new ArgumentException("World clock options are invalid or default initialized.", nameof(clockOptions));
        if (!allowVariablePlayback && clockOptions.InitialPlaybackRate != WorldClockOptions.DefaultPlaybackRate)
        {
            throw new ArgumentException(
                "Fixed-playback runtimes must start at the normal 1x playback rate.",
                nameof(clockOptions));
        }

        WorldOptions = worldOptions;
        ClockOptions = clockOptions;
        AllowVariablePlayback = allowVariablePlayback;
        AllowVariableSimulationFrequency = allowVariableSimulationFrequency;
    }

    public EcsWorldOptions WorldOptions { get; }

    public WorldClockOptions ClockOptions { get; }

    public bool AllowVariablePlayback { get; }

    /// <summary>
    /// Allows explicit owner-thread cadence changes at an idle Runtime boundary.
    /// False by default for fixed authoritative servers and benchmark baselines.
    /// </summary>
    public bool AllowVariableSimulationFrequency { get; }

    internal Action<ServerGeneration, ServerGeneration?>? PublicationBoundaryHook { get; set; }
}
