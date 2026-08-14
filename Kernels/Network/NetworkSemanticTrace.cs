using System.Runtime.CompilerServices;

namespace UltraSim.Network;

/// <summary>
/// Identifies a normalized semantic fact used for cross-topology correctness evidence.
/// </summary>
/// <remarks>
/// Trace facts describe committed observable meaning, not packets, retries, fragments,
/// encryption records, connection-local aliases, or client storage slots.
/// </remarks>
public enum NetworkSemanticTraceKind : byte
{
    ConnectionStateChanged = 0,
    AuthorityInputAccepted = 1,
    ProjectionStatePublished = 2,
    ProjectionLifecyclePublished = 3,
    ApplicationEventPublished = 4,
    BaselineCommitted = 5,
    ClientStateApplied = 6,
    UiOperationApplied = 7,
    ClockAnchorApplied = 8,
}

/// <summary>Contains one fixed-width normalized semantic trace fact.</summary>
public readonly struct NetworkSemanticTraceEntry : IEquatable<NetworkSemanticTraceEntry>
{
    public NetworkSemanticTraceKind Kind { get; }
    public StateEpoch StateEpoch { get; }
    public ulong SimulationTick { get; }
    public ulong Subject { get; }
    public ulong Version { get; }
    public ulong ValueHash { get; }

    /// <summary>Creates a normalized semantic fact.</summary>
    /// <param name="kind">The admitted fact kind.</param>
    /// <param name="stateEpoch">The projection epoch, or default for pre-projection connection facts.</param>
    /// <param name="simulationTick">The authoritative completed simulation tick.</param>
    /// <param name="subject">A workload-defined canonical subject identifier.</param>
    /// <param name="version">A workload-defined state, event, or operation version.</param>
    /// <param name="valueHash">A stable hash of the canonical semantic payload.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is unknown.</exception>
    public NetworkSemanticTraceEntry(
        NetworkSemanticTraceKind kind,
        StateEpoch stateEpoch,
        ulong simulationTick,
        ulong subject,
        ulong version,
        ulong valueHash)
    {
        if ((uint)kind > (uint)NetworkSemanticTraceKind.ClockAnchorApplied)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown semantic trace kind.");
        Kind = kind;
        StateEpoch = stateEpoch;
        SimulationTick = simulationTick;
        Subject = subject;
        Version = version;
        ValueHash = valueHash;
    }

    /// <inheritdoc />
    public bool Equals(NetworkSemanticTraceEntry other) =>
        Kind == other.Kind && StateEpoch == other.StateEpoch &&
        SimulationTick == other.SimulationTick && Subject == other.Subject &&
        Version == other.Version && ValueHash == other.ValueHash;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkSemanticTraceEntry other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Kind, StateEpoch, SimulationTick, Subject, Version, ValueHash);

    public static bool operator ==(NetworkSemanticTraceEntry left, NetworkSemanticTraceEntry right) => left.Equals(right);
    public static bool operator !=(NetworkSemanticTraceEntry left, NetworkSemanticTraceEntry right) => !left.Equals(right);
}

/// <summary>Contains a stable allocation-free 128-bit digest of a semantic trace.</summary>
public readonly struct NetworkTraceDigest : IEquatable<NetworkTraceDigest>
{
    public ulong Low { get; }
    public ulong High { get; }

    internal NetworkTraceDigest(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    /// <inheritdoc />
    public bool Equals(NetworkTraceDigest other) => Low == other.Low && High == other.High;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkTraceDigest other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Low, High);

    /// <inheritdoc />
    public override string ToString() => $"{High:X16}{Low:X16}";

    public static bool operator ==(NetworkTraceDigest left, NetworkTraceDigest right) => left.Equals(right);
    public static bool operator !=(NetworkTraceDigest left, NetworkTraceDigest right) => !left.Equals(right);
}

/// <summary>
/// Stores a bounded normalized semantic trace for deterministic parity evidence.
/// </summary>
/// <remarks>
/// Capacity is fixed at construction. <see cref="TryAppend"/> performs no managed
/// allocation and returns false without mutation when full. Exact entry comparison,
/// rather than digest equality alone, is the correctness authority.
/// </remarks>
public sealed class NetworkSemanticTrace
{
    private const int MaximumCapacity = 16 * 1024 * 1024;
    private readonly NetworkSemanticTraceEntry[] _entries;
    private int _count;

    /// <summary>Creates a fixed-capacity semantic trace.</summary>
    /// <param name="capacity">The maximum number of retained facts.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is outside 1..16,777,216.</exception>
    public NetworkSemanticTrace(int capacity)
    {
        if (capacity is <= 0 or > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                $"Trace capacity must be within 1..{MaximumCapacity}.");
        _entries = new NetworkSemanticTraceEntry[capacity];
    }

    /// <summary>Gets the fixed fact capacity.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Gets the retained fact count.</summary>
    public int Count => _count;

    /// <summary>Gets the known fixed-array payload reserved by this trace.</summary>
    public long ReservedBytes => checked((long)_entries.Length * Unsafe.SizeOf<NetworkSemanticTraceEntry>());

    /// <summary>Gets the retained facts in canonical append order.</summary>
    public ReadOnlySpan<NetworkSemanticTraceEntry> Entries => _entries.AsSpan(0, _count);

    /// <summary>Attempts to append one semantic fact.</summary>
    /// <param name="entry">The normalized fact.</param>
    /// <returns>True when appended; false when capacity was already exhausted.</returns>
    public bool TryAppend(in NetworkSemanticTraceEntry entry)
    {
        if ((uint)_count >= (uint)_entries.Length)
            return false;
        _entries[_count++] = entry;
        return true;
    }

    /// <summary>Clears retained facts without releasing or reallocating capacity.</summary>
    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
    }

    /// <summary>Compares exact canonical fact sequences.</summary>
    /// <param name="other">The trace to compare.</param>
    /// <returns>True only when every retained fact matches in order.</returns>
    public bool CanonicalEquals(NetworkSemanticTrace? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    /// <summary>Computes a stable allocation-free evidence digest.</summary>
    /// <returns>A deterministic order-sensitive 128-bit digest.</returns>
    public NetworkTraceDigest ComputeDigest()
    {
        const ulong firstOffset = 14695981039346656037UL;
        const ulong secondOffset = 7809847782465536322UL;
        const ulong firstPrime = 1099511628211UL;
        const ulong secondPrime = 14029467366897019727UL;
        ulong low = firstOffset;
        ulong high = secondOffset;

        Mix(ref low, ref high, unchecked((ulong)_count), firstPrime, secondPrime);
        foreach (ref readonly NetworkSemanticTraceEntry entry in Entries)
        {
            Mix(ref low, ref high, (byte)entry.Kind, firstPrime, secondPrime);
            Mix(ref low, ref high, entry.StateEpoch.Value, firstPrime, secondPrime);
            Mix(ref low, ref high, entry.SimulationTick, firstPrime, secondPrime);
            Mix(ref low, ref high, entry.Subject, firstPrime, secondPrime);
            Mix(ref low, ref high, entry.Version, firstPrime, secondPrime);
            Mix(ref low, ref high, entry.ValueHash, firstPrime, secondPrime);
        }
        return new(low, high);
    }

    private static void Mix(ref ulong low, ref ulong high, ulong value, ulong firstPrime, ulong secondPrime)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            byte current = unchecked((byte)(value >> shift));
            low = unchecked((low ^ current) * firstPrime);
            high = unchecked((high ^ unchecked((byte)(current + shift))) * secondPrime);
        }
    }
}
