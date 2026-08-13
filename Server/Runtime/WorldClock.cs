using System.Runtime.InteropServices;

namespace UltraSim.Server.Runtime;

/// <summary>
/// Fixed, deterministic clock configuration for one server generation.
/// </summary>
/// <remarks>
/// <see cref="WorldTimeRate"/> is a dimensionless simulation rate. It does not
/// imply a calendar, day/night system, or any other game-domain meaning.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct WorldClockOptions : IEquatable<WorldClockOptions>
{
    public const int DefaultSimulationFrequencyHz = 60;
    // A 1:1 default is mechanism-neutral. Games may deliberately configure
    // accelerated world time (the S0 fixture uses 24x), but Runtime must not
    // encode that game policy as its default.
    public const int DefaultWorldTimeRate = 1;
    public const int DefaultPlaybackRate = 1;
    public const int DefaultMaximumSimulationStepsPerAdvance = 1_000_000;

    public WorldClockOptions(
        int simulationFrequencyHz = DefaultSimulationFrequencyHz,
        int worldTimeRate = DefaultWorldTimeRate,
        int initialPlaybackRate = DefaultPlaybackRate,
        int maximumSimulationStepsPerAdvance = DefaultMaximumSimulationStepsPerAdvance)
    {
        if (simulationFrequencyHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(simulationFrequencyHz));
        if (worldTimeRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeRate));
        if (initialPlaybackRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialPlaybackRate));
        if (maximumSimulationStepsPerAdvance <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSimulationStepsPerAdvance));

        SimulationFrequencyHz = simulationFrequencyHz;
        WorldTimeRate = worldTimeRate;
        InitialPlaybackRate = initialPlaybackRate;
        MaximumSimulationStepsPerAdvance = maximumSimulationStepsPerAdvance;
    }

    public static WorldClockOptions Default => new(
        DefaultSimulationFrequencyHz,
        DefaultWorldTimeRate,
        DefaultPlaybackRate,
        DefaultMaximumSimulationStepsPerAdvance);

    public int SimulationFrequencyHz { get; }

    public int WorldTimeRate { get; }

    public int InitialPlaybackRate { get; }

    public int MaximumSimulationStepsPerAdvance { get; }

    public bool IsValid =>
        SimulationFrequencyHz > 0 &&
        WorldTimeRate > 0 &&
        InitialPlaybackRate > 0 &&
        MaximumSimulationStepsPerAdvance > 0;

    public bool Equals(WorldClockOptions other) =>
        SimulationFrequencyHz == other.SimulationFrequencyHz &&
        WorldTimeRate == other.WorldTimeRate &&
        InitialPlaybackRate == other.InitialPlaybackRate &&
        MaximumSimulationStepsPerAdvance == other.MaximumSimulationStepsPerAdvance;

    public override bool Equals(object? obj) => obj is WorldClockOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        SimulationFrequencyHz,
        WorldTimeRate,
        InitialPlaybackRate,
        MaximumSimulationStepsPerAdvance);

    public static bool operator ==(WorldClockOptions left, WorldClockOptions right) => left.Equals(right);

    public static bool operator !=(WorldClockOptions left, WorldClockOptions right) => !left.Equals(right);
}

/// <summary>Persistable authoritative portion of a generation's world clock.</summary>
/// <remarks>
/// S0.1 defines and validates this value contract. Durable save-set ownership is
/// installed later with the real multi-participant persistence integration.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct WorldClockSnapshot : IEquatable<WorldClockSnapshot>
{
    internal WorldClockSnapshot(
        int simulationFrequencyHz,
        int worldTimeRate,
        ulong simulationTick,
        long worldTimeMicroseconds,
        long worldTimeRemainder,
        int playbackRate)
    {
        SimulationFrequencyHz = simulationFrequencyHz;
        WorldTimeRate = worldTimeRate;
        SimulationTick = simulationTick;
        WorldTimeMicroseconds = worldTimeMicroseconds;
        WorldTimeRemainder = worldTimeRemainder;
        PlaybackRate = playbackRate;
    }

    public int SimulationFrequencyHz { get; }

    public int WorldTimeRate { get; }

    public ulong SimulationTick { get; }

    public long WorldTimeMicroseconds { get; }

    public long WorldTimeRemainder { get; }

    public int PlaybackRate { get; }

    public bool Equals(WorldClockSnapshot other) =>
        SimulationFrequencyHz == other.SimulationFrequencyHz &&
        WorldTimeRate == other.WorldTimeRate &&
        SimulationTick == other.SimulationTick &&
        WorldTimeMicroseconds == other.WorldTimeMicroseconds &&
        WorldTimeRemainder == other.WorldTimeRemainder &&
        PlaybackRate == other.PlaybackRate;

    public override bool Equals(object? obj) => obj is WorldClockSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        SimulationFrequencyHz,
        WorldTimeRate,
        SimulationTick,
        WorldTimeMicroseconds,
        WorldTimeRemainder,
        PlaybackRate);

    public static bool operator ==(WorldClockSnapshot left, WorldClockSnapshot right) => left.Equals(right);

    public static bool operator !=(WorldClockSnapshot left, WorldClockSnapshot right) => !left.Equals(right);
}

/// <summary>
/// Mutable, generation-owned simulation and world-time clock.
/// </summary>
/// <remarks>
/// World time uses integer microseconds plus a bounded rational remainder. Host
/// elapsed time is accumulated as integer <see cref="TimeSpan.Ticks"/> and is
/// only used to decide how many fixed simulation steps to execute. Playback rate
/// therefore changes how quickly tick N is reached, never the meaning of tick N.
/// </remarks>
internal sealed class WorldClock
{
    public const long WorldTimeUnitsPerSecond = 1_000_000;

    private readonly WorldClockOptions _options;
    private readonly double _fixedStepSeconds;
    private ulong _hostAccumulatorNumerator;
    private ulong _simulationTick;
    private long _worldTimeMicroseconds;
    private long _worldTimeRemainder;
    private int _playbackRate;

    internal WorldClock(WorldClockOptions options)
    {
        if (!options.IsValid)
            throw new ArgumentException("World clock options are invalid or default initialized.", nameof(options));

        _options = options;
        _fixedStepSeconds = 1d / options.SimulationFrequencyHz;
        _playbackRate = options.InitialPlaybackRate;
    }

    public WorldClockOptions Options => _options;

    public ulong SimulationTick => _simulationTick;

    public long WorldTimeMicroseconds => _worldTimeMicroseconds;

    public int PlaybackRate => _playbackRate;

    public double FixedStepSeconds => _fixedStepSeconds;

    /// <summary>Captures the authoritative clock fields without host-time residue.</summary>
    public WorldClockSnapshot CaptureSnapshot() => new(
        _options.SimulationFrequencyHz,
        _options.WorldTimeRate,
        _simulationTick,
        _worldTimeMicroseconds,
        _worldTimeRemainder,
        _playbackRate);

    /// <summary>Restores a snapshot produced for the same clock configuration.</summary>
    /// <remarks>In-flight host elapsed time is intentionally discarded.</remarks>
    internal void RestoreSnapshot(WorldClockSnapshot snapshot)
    {
        if (snapshot.SimulationFrequencyHz != _options.SimulationFrequencyHz ||
            snapshot.WorldTimeRate != _options.WorldTimeRate)
        {
            throw new ArgumentException("The clock snapshot belongs to a different fixed clock configuration.", nameof(snapshot));
        }
        if (snapshot.WorldTimeMicroseconds < 0 ||
            snapshot.WorldTimeRemainder < 0 ||
            snapshot.WorldTimeRemainder >= _options.SimulationFrequencyHz ||
            snapshot.PlaybackRate <= 0)
        {
            throw new ArgumentException("The clock snapshot contains invalid state.", nameof(snapshot));
        }

        _simulationTick = snapshot.SimulationTick;
        _worldTimeMicroseconds = snapshot.WorldTimeMicroseconds;
        _worldTimeRemainder = snapshot.WorldTimeRemainder;
        _playbackRate = snapshot.PlaybackRate;
        _hostAccumulatorNumerator = 0;
    }

    /// <summary>
    /// Converts host monotonic elapsed time into a bounded number of fixed steps.
    /// </summary>
    internal int ConsumeHostElapsed(TimeSpan hostElapsed)
    {
        if (hostElapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(hostElapsed));

        ulong scaledHostTicks = checked((ulong)hostElapsed.Ticks *
            (ulong)_playbackRate *
            (ulong)_options.SimulationFrequencyHz);
        ulong nextAccumulator = checked(_hostAccumulatorNumerator + scaledHostTicks);

        ulong denominator = (ulong)TimeSpan.TicksPerSecond;
        ulong steps = nextAccumulator / denominator;
        if (steps > (ulong)_options.MaximumSimulationStepsPerAdvance)
        {
            throw new InvalidOperationException(
                $"Host elapsed time requested {steps} simulation steps, exceeding the configured limit " +
                $"of {_options.MaximumSimulationStepsPerAdvance}.");
        }

        _hostAccumulatorNumerator = nextAccumulator % denominator;
        return checked((int)steps);
    }

    internal void SetPlaybackRate(int playbackRate)
    {
        if (playbackRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(playbackRate));
        _playbackRate = playbackRate;
    }

    internal void AdvanceSimulationTick()
    {
        if (_simulationTick == ulong.MaxValue)
            throw new InvalidOperationException("The simulation tick counter reached its terminal value.");

        long numerator = checked(_worldTimeRemainder +
            checked((long)_options.WorldTimeRate * WorldTimeUnitsPerSecond));
        long wholeUnits = numerator / _options.SimulationFrequencyHz;
        long remainder = numerator % _options.SimulationFrequencyHz;
        _worldTimeMicroseconds = checked(_worldTimeMicroseconds + wholeUnits);
        _worldTimeRemainder = remainder;
        _simulationTick++;
    }
}
