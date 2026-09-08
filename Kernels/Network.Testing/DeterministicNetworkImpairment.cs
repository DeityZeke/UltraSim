namespace UltraSim.Network;

/// <summary>Defines bounded deterministic datagram impairment probabilities.</summary>
/// <remarks>
/// Values are per-thousand probabilities. The oracle is an evidence control, not
/// a random-number source and not a transport implementation.
/// </remarks>
public readonly struct NetworkImpairmentOptions : IEquatable<NetworkImpairmentOptions>
{
    public ulong Seed { get; }
    public ushort LossPermille { get; }
    public ushort DuplicatePermille { get; }
    public ushort CorruptionPermille { get; }
    public ushort MaximumDelaySteps { get; }

    /// <summary>Creates a deterministic impairment profile.</summary>
    /// <param name="seed">The complete deterministic profile seed.</param>
    /// <param name="lossPermille">Drop probability within 0..1000.</param>
    /// <param name="duplicatePermille">Duplicate probability within 0..1000.</param>
    /// <param name="corruptionPermille">Corruption probability within 0..1000.</param>
    /// <param name="maximumDelaySteps">Maximum bounded delivery delay.</param>
    /// <exception cref="ArgumentOutOfRangeException">A probability exceeds 1000.</exception>
    public NetworkImpairmentOptions(
        ulong seed,
        ushort lossPermille,
        ushort duplicatePermille,
        ushort corruptionPermille,
        ushort maximumDelaySteps)
    {
        if (lossPermille > 1000) throw new ArgumentOutOfRangeException(nameof(lossPermille));
        if (duplicatePermille > 1000) throw new ArgumentOutOfRangeException(nameof(duplicatePermille));
        if (corruptionPermille > 1000) throw new ArgumentOutOfRangeException(nameof(corruptionPermille));
        Seed = seed;
        LossPermille = lossPermille;
        DuplicatePermille = duplicatePermille;
        CorruptionPermille = corruptionPermille;
        MaximumDelaySteps = maximumDelaySteps;
    }

    /// <inheritdoc />
    public bool Equals(NetworkImpairmentOptions other) =>
        Seed == other.Seed && LossPermille == other.LossPermille &&
        DuplicatePermille == other.DuplicatePermille &&
        CorruptionPermille == other.CorruptionPermille &&
        MaximumDelaySteps == other.MaximumDelaySteps;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkImpairmentOptions other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Seed, LossPermille, DuplicatePermille, CorruptionPermille, MaximumDelaySteps);

    public static bool operator ==(NetworkImpairmentOptions left, NetworkImpairmentOptions right) => left.Equals(right);
    public static bool operator !=(NetworkImpairmentOptions left, NetworkImpairmentOptions right) => !left.Equals(right);
}

/// <summary>Contains the reproducible impairment decision for one submission ordinal.</summary>
public readonly struct NetworkImpairmentDecision : IEquatable<NetworkImpairmentDecision>
{
    public bool Dropped { get; }
    public bool Duplicated { get; }
    public bool Corrupted { get; }
    public ushort DelaySteps { get; }

    internal NetworkImpairmentDecision(bool dropped, bool duplicated, bool corrupted, ushort delaySteps)
    {
        Dropped = dropped;
        Duplicated = duplicated;
        Corrupted = corrupted;
        DelaySteps = delaySteps;
    }

    /// <inheritdoc />
    public bool Equals(NetworkImpairmentDecision other) =>
        Dropped == other.Dropped && Duplicated == other.Duplicated &&
        Corrupted == other.Corrupted && DelaySteps == other.DelaySteps;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkImpairmentDecision other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Dropped, Duplicated, Corrupted, DelaySteps);

    public static bool operator ==(NetworkImpairmentDecision left, NetworkImpairmentDecision right) => left.Equals(right);
    public static bool operator !=(NetworkImpairmentDecision left, NetworkImpairmentDecision right) => !left.Equals(right);
}

/// <summary>
/// Produces stateless deterministic fault-injection decisions from submission ordinals.
/// </summary>
/// <remarks>
/// Decisions do not depend on call order or mutable random state, so a failed run
/// can be reproduced exactly and two protocol topologies can receive identical
/// impairment. A dropped submission is never also duplicated or corrupted.
/// </remarks>
public sealed class DeterministicNetworkImpairment
{
    private readonly NetworkImpairmentOptions _options;

    public DeterministicNetworkImpairment(NetworkImpairmentOptions options) => _options = options;

    public NetworkImpairmentOptions Options => _options;

    /// <summary>Gets the decision for one stable submission ordinal.</summary>
    /// <param name="submissionOrdinal">The zero-based canonical submission ordinal.</param>
    /// <returns>The deterministic impairment decision.</returns>
    public NetworkImpairmentDecision Decide(ulong submissionOrdinal)
    {
        ulong value = Mix(unchecked(_options.Seed + submissionOrdinal * 0x9E3779B97F4A7C15UL));
        bool dropped = PerThousand(value, _options.LossPermille);
        value = Mix(value);
        bool duplicated = !dropped && PerThousand(value, _options.DuplicatePermille);
        value = Mix(value);
        bool corrupted = !dropped && PerThousand(value, _options.CorruptionPermille);
        value = Mix(value);
        ushort delay = _options.MaximumDelaySteps == 0
            ? (ushort)0
            : (ushort)(value % ((uint)_options.MaximumDelaySteps + 1U));
        return new(dropped, duplicated, corrupted, delay);
    }

    private static bool PerThousand(ulong value, ushort threshold) => value % 1000UL < threshold;

    private static ulong Mix(ulong value)
    {
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
