using System.Runtime.InteropServices;

namespace UltraSim.Server.Runtime;

/// <summary>
/// Deterministic initial clock configuration for one server generation.
/// </summary>
/// <remarks>
/// <see cref="WorldTimeRate"/> is a dimensionless simulation rate. It does not
/// imply a calendar, day/night system, or any other game-domain meaning.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct WorldClockOptions : IEquatable<WorldClockOptions>
{
    public const int DefaultSimulationFrequencyHz = 60;
    public const int MaximumSimulationFrequencyHz = 1_000;
    /// <summary>
    /// Fixed fractional world-time units in one microsecond.
    /// </summary>
    /// <remarks>
    /// This denominator is an authoritative format constant, not a simulation
    /// frequency. It makes common server cadences, including 20/30/40/50/60 Hz,
    /// exactly representable without making saved time depend on the host that
    /// produced it.
    /// </remarks>
    public const int WorldTimeFractionQuantaPerMicrosecond = 3_600_000;
    public const long WorldTimeQuantaPerSecond =
        1_000_000L * WorldTimeFractionQuantaPerMicrosecond;
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
        ValidateSimulationFrequency(simulationFrequencyHz, nameof(simulationFrequencyHz));
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
        IsSupportedSimulationFrequency(SimulationFrequencyHz) &&
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

    /// <summary>
    /// Reports whether a cadence belongs to the bounded exact-time domain.
    /// </summary>
    public static bool IsSupportedSimulationFrequency(int simulationFrequencyHz) =>
        simulationFrequencyHz > 0 &&
        simulationFrequencyHz <= MaximumSimulationFrequencyHz &&
        WorldTimeQuantaPerSecond % simulationFrequencyHz == 0;

    internal static void ValidateSimulationFrequency(int simulationFrequencyHz, string parameterName)
    {
        if (!IsSupportedSimulationFrequency(simulationFrequencyHz))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                simulationFrequencyHz,
                $"Simulation frequency must be at most {MaximumSimulationFrequencyHz} Hz and exactly divide " +
                $"the authoritative {WorldTimeQuantaPerSecond}-quanta-per-second time base.");
        }
    }
}

/// <summary>Diagnostic snapshot of a generation's authoritative clock plus host playback.</summary>
/// <remarks>
/// This diagnostic snapshot also includes host playback. Durable game saves
/// use <see cref="PersistentWorldClockState"/>, which deliberately excludes
/// host execution policy.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct WorldClockSnapshot : IEquatable<WorldClockSnapshot>
{
    internal WorldClockSnapshot(
        int simulationFrequencyHz,
        int worldTimeRate,
        ulong simulationTick,
        long worldTimeMicroseconds,
        int worldTimeFractionQuanta,
        int playbackRate)
    {
        SimulationFrequencyHz = simulationFrequencyHz;
        WorldTimeRate = worldTimeRate;
        SimulationTick = simulationTick;
        WorldTimeMicroseconds = worldTimeMicroseconds;
        WorldTimeFractionQuanta = worldTimeFractionQuanta;
        PlaybackRate = playbackRate;
    }

    public int SimulationFrequencyHz { get; }

    public int WorldTimeRate { get; }

    public ulong SimulationTick { get; }

    public long WorldTimeMicroseconds { get; }

    /// <summary>
    /// Fractional microsecond in the fixed
    /// <see cref="WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond"/> time base.
    /// </summary>
    public int WorldTimeFractionQuanta { get; }

    public int PlaybackRate { get; }

    public bool Equals(WorldClockSnapshot other) =>
        SimulationFrequencyHz == other.SimulationFrequencyHz &&
        WorldTimeRate == other.WorldTimeRate &&
        SimulationTick == other.SimulationTick &&
        WorldTimeMicroseconds == other.WorldTimeMicroseconds &&
        WorldTimeFractionQuanta == other.WorldTimeFractionQuanta &&
        PlaybackRate == other.PlaybackRate;

    public override bool Equals(object? obj) => obj is WorldClockSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        SimulationFrequencyHz,
        WorldTimeRate,
        SimulationTick,
        WorldTimeMicroseconds,
        WorldTimeFractionQuanta,
        PlaybackRate);

    public static bool operator ==(WorldClockSnapshot left, WorldClockSnapshot right) => left.Equals(right);

    public static bool operator !=(WorldClockSnapshot left, WorldClockSnapshot right) => !left.Equals(right);
}

/// <summary>The durable, host-independent portion of one authoritative world clock.</summary>
/// <remarks>
/// Playback rate, simulation frequency, and accumulated host elapsed time are
/// deliberately excluded: they are execution policy of the loading host, not
/// saved world state. The fractional unit has one fixed format-wide denominator.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct PersistentWorldClockState : IEquatable<PersistentWorldClockState>
{
    public PersistentWorldClockState(
        int worldTimeRate,
        ulong simulationTick,
        long worldTimeMicroseconds,
        int worldTimeFractionQuanta)
    {
        if (worldTimeRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeRate));
        if (worldTimeMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeMicroseconds));
        if (worldTimeFractionQuanta < 0 ||
            worldTimeFractionQuanta >= WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond)
        {
            throw new ArgumentOutOfRangeException(nameof(worldTimeFractionQuanta));
        }

        WorldTimeRate = worldTimeRate;
        SimulationTick = simulationTick;
        WorldTimeMicroseconds = worldTimeMicroseconds;
        WorldTimeFractionQuanta = worldTimeFractionQuanta;
    }

    public int WorldTimeRate { get; }
    public ulong SimulationTick { get; }
    public long WorldTimeMicroseconds { get; }
    public int WorldTimeFractionQuanta { get; }

    public bool Equals(PersistentWorldClockState other) =>
        WorldTimeRate == other.WorldTimeRate &&
        SimulationTick == other.SimulationTick &&
        WorldTimeMicroseconds == other.WorldTimeMicroseconds &&
        WorldTimeFractionQuanta == other.WorldTimeFractionQuanta;

    public override bool Equals(object? obj) => obj is PersistentWorldClockState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        WorldTimeRate, SimulationTick, WorldTimeMicroseconds, WorldTimeFractionQuanta);
    public static bool operator ==(PersistentWorldClockState left, PersistentWorldClockState right) => left.Equals(right);
    public static bool operator !=(PersistentWorldClockState left, PersistentWorldClockState right) => !left.Equals(right);
}

/// <summary>
/// Mutable, generation-owned simulation and world-time clock.
/// </summary>
/// <remarks>
/// World time uses integer microseconds plus a fixed fractional unit. Host
/// elapsed time is accumulated as integer <see cref="TimeSpan.Ticks"/> and is
/// only used to decide how many fixed simulation steps to execute. Playback rate
/// therefore changes how quickly tick N is reached, never the meaning of tick N.
/// </remarks>
internal sealed class WorldClock
{
    public const long WorldTimeUnitsPerSecond = 1_000_000;

    private readonly WorldClockOptions _options;
    private int _simulationFrequencyHz;
    private double _fixedStepSeconds;
    private long _stepWholeMicrosecondsPerWorldRate;
    private long _stepFractionQuantaPerWorldRate;
    private ulong _hostAccumulatorNumerator;
    private ulong _simulationTick;
    private long _worldTimeMicroseconds;
    private int _worldTimeFractionQuanta;
    private int _playbackRate;

    internal WorldClock(WorldClockOptions options)
    {
        if (!options.IsValid)
            throw new ArgumentException("World clock options are invalid or default initialized.", nameof(options));

        _options = options;
        ApplySimulationFrequency(options.SimulationFrequencyHz, resetHostResidue: false);
        _playbackRate = options.InitialPlaybackRate;
    }

    public WorldClockOptions Options => _options;

    public ulong SimulationTick => _simulationTick;

    public long WorldTimeMicroseconds => _worldTimeMicroseconds;

    public int SimulationFrequencyHz => _simulationFrequencyHz;

    public int PlaybackRate => _playbackRate;

    public double FixedStepSeconds => _fixedStepSeconds;

    /// <summary>Captures the authoritative clock fields without host-time residue.</summary>
    public WorldClockSnapshot CaptureSnapshot() => new(
        _simulationFrequencyHz,
        _options.WorldTimeRate,
        _simulationTick,
        _worldTimeMicroseconds,
        _worldTimeFractionQuanta,
        _playbackRate);

    internal PersistentWorldClockState CapturePersistentState() => new(
        _options.WorldTimeRate,
        _simulationTick,
        _worldTimeMicroseconds,
        _worldTimeFractionQuanta);

    internal void RestorePersistentState(PersistentWorldClockState state)
    {
        if (state.WorldTimeRate != _options.WorldTimeRate)
        {
            throw new ArgumentException(
                "The persistent clock state belongs to a different world-time-rate configuration.",
                nameof(state));
        }

        _simulationTick = state.SimulationTick;
        _worldTimeMicroseconds = state.WorldTimeMicroseconds;
        _worldTimeFractionQuanta = state.WorldTimeFractionQuanta;
        _hostAccumulatorNumerator = 0;
    }

    /// <summary>Restores a diagnostic snapshot with a supported cadence and matching world-time rate.</summary>
    /// <remarks>In-flight host elapsed time is intentionally discarded.</remarks>
    internal void RestoreSnapshot(WorldClockSnapshot snapshot)
    {
        if (snapshot.WorldTimeRate != _options.WorldTimeRate ||
            !WorldClockOptions.IsSupportedSimulationFrequency(snapshot.SimulationFrequencyHz))
        {
            throw new ArgumentException("The clock snapshot contains an unsupported clock configuration.", nameof(snapshot));
        }
        if (snapshot.WorldTimeMicroseconds < 0 ||
            snapshot.WorldTimeFractionQuanta < 0 ||
            snapshot.WorldTimeFractionQuanta >= WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond ||
            snapshot.PlaybackRate <= 0)
        {
            throw new ArgumentException("The clock snapshot contains invalid state.", nameof(snapshot));
        }

        SetSimulationFrequency(snapshot.SimulationFrequencyHz);
        _simulationTick = snapshot.SimulationTick;
        _worldTimeMicroseconds = snapshot.WorldTimeMicroseconds;
        _worldTimeFractionQuanta = snapshot.WorldTimeFractionQuanta;
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
            (ulong)_simulationFrequencyHz);
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

    /// <summary>
    /// Changes host simulation cadence while preserving authoritative time.
    /// In-flight host elapsed residue is deliberately discarded at the policy
    /// boundary so elapsed time cannot be interpreted under two frequencies.
    /// </summary>
    internal void SetSimulationFrequency(int simulationFrequencyHz)
    {
        WorldClockOptions.ValidateSimulationFrequency(
            simulationFrequencyHz,
            nameof(simulationFrequencyHz));
        if (simulationFrequencyHz == _simulationFrequencyHz)
            return;

        ApplySimulationFrequency(simulationFrequencyHz, resetHostResidue: true);
    }

    internal void AdvanceSimulationTick()
    {
        if (_simulationTick == ulong.MaxValue)
            throw new InvalidOperationException("The simulation tick counter reached its terminal value.");

        // Do not multiply WorldTimeRate by the 3.6e12-quanta-per-second base.
        // The per-step value is decomposed first: whole microseconds are at most
        // 1,000,000 and fractional quanta are below 3,600,000. Multiplying either
        // term by the supported int WorldTimeRate therefore remains inside Int64,
        // including int.MaxValue; checked addition still protects accumulated time.
        long totalFractionQuanta = checked(
            _stepFractionQuantaPerWorldRate * _options.WorldTimeRate +
            _worldTimeFractionQuanta);
        long wholeMicroseconds = checked(
            _stepWholeMicrosecondsPerWorldRate * _options.WorldTimeRate +
            totalFractionQuanta / WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond);
        _worldTimeFractionQuanta = checked((int)(totalFractionQuanta %
            WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond));
        _worldTimeMicroseconds = checked(_worldTimeMicroseconds + wholeMicroseconds);
        _simulationTick++;
    }

    private void ApplySimulationFrequency(int simulationFrequencyHz, bool resetHostResidue)
    {
        _simulationFrequencyHz = simulationFrequencyHz;
        _fixedStepSeconds = 1d / simulationFrequencyHz;
        long stepQuantaPerWorldRate =
            WorldClockOptions.WorldTimeQuantaPerSecond / simulationFrequencyHz;
        _stepWholeMicrosecondsPerWorldRate = stepQuantaPerWorldRate /
            WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond;
        _stepFractionQuantaPerWorldRate = stepQuantaPerWorldRate %
            WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond;
        if (resetHostResidue)
            _hostAccumulatorNumerator = 0;
    }
}
