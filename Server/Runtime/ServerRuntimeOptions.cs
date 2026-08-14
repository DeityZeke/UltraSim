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

    /// <summary>Gets the fixed ECS world/scheduler capacity policy used for every generation.</summary>
    public EcsWorldOptions WorldOptions { get; }

    /// <summary>Gets cold clock/rate/step-limit policy; live cadence is read from <see cref="ServerRuntime"/>.</summary>
    public WorldClockOptions ClockOptions { get; }

    /// <summary>Gets whether explicit owner-thread playback changes are admitted.</summary>
    public bool AllowVariablePlayback { get; }

    /// <summary>
    /// Allows explicit owner-thread cadence changes at an idle Runtime boundary.
    /// False by default for fixed authoritative servers and benchmark baselines.
    /// </summary>
    public bool AllowVariableSimulationFrequency { get; }

    internal Action<ServerGeneration, ServerGeneration?>? PublicationBoundaryHook { get; set; }
}
