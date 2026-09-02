using System.Runtime.CompilerServices;

namespace UltraSim.Game.Replication;

/// <summary>Identifies a normalized Game semantic fact used by packet-free parity evidence.</summary>
public enum GameSemanticTraceKind : byte
{
    ProjectionStatePublished = 1,
    ProjectionLifecyclePublished = 2,
    ApplicationEventPublished = 3,
    BaselineCommitted = 4,
}

/// <summary>Contains one fixed-width normalized Game semantic trace fact.</summary>
public readonly record struct GameSemanticTraceEntry(
    GameSemanticTraceKind Kind,
    StateEpoch StateEpoch,
    ulong SimulationTick,
    ulong Subject,
    ulong Version,
    ulong ValueHash);

/// <summary>Contains a stable allocation-free 128-bit digest of a Game semantic trace.</summary>
public readonly record struct GameSemanticTraceDigest(ulong Low, ulong High)
{
    public override string ToString() => $"{High:X16}{Low:X16}";
}

/// <summary>Stores a bounded packet-free semantic trace for deterministic parity evidence.</summary>
public sealed class GameSemanticTrace
{
    private const int MaximumCapacity = 16 * 1024 * 1024;
    private readonly GameSemanticTraceEntry[] _entries;
    private int _count;

    public GameSemanticTrace(int capacity)
    {
        if (capacity is <= 0 or > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new GameSemanticTraceEntry[capacity];
    }

    public int Capacity => _entries.Length;
    public int Count => _count;
    public long ReservedBytes => checked((long)_entries.Length * Unsafe.SizeOf<GameSemanticTraceEntry>());
    public ReadOnlySpan<GameSemanticTraceEntry> Entries => _entries.AsSpan(0, _count);

    public bool TryAppend(in GameSemanticTraceEntry entry)
    {
        if ((uint)_count >= (uint)_entries.Length) return false;
        _entries[_count++] = entry;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
    }

    public bool CanonicalEquals(GameSemanticTrace? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    public GameSemanticTraceDigest ComputeDigest()
    {
        const ulong firstOffset = 14695981039346656037UL;
        const ulong secondOffset = 7809847782465536322UL;
        const ulong firstPrime = 1099511628211UL;
        const ulong secondPrime = 14029467366897019727UL;
        ulong low = firstOffset;
        ulong high = secondOffset;
        Mix(ref low, ref high, unchecked((ulong)_count), firstPrime, secondPrime);
        foreach (ref readonly GameSemanticTraceEntry entry in Entries)
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
