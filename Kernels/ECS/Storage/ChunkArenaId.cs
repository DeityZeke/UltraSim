using System.Threading;

namespace UltraSim.ECS;

/// <summary>Transient process-lifetime identity for one chunk arena instance.</summary>
public readonly struct ChunkArenaId : IEquatable<ChunkArenaId>
{
    private readonly uint _value;

    internal ChunkArenaId(uint value)
    {
        _value = value;
    }

    public static ChunkArenaId Invalid => default;

    public uint Value => _value;

    public bool IsValid => _value != 0;

    internal static ChunkArenaId Create() => Source.Allocate();

    public bool Equals(ChunkArenaId other) => _value == other._value;

    public override bool Equals(object? obj) => obj is ChunkArenaId other && Equals(other);

    public override int GetHashCode() => (int)_value;

    public override string ToString() => IsValid ? $"ChunkArena({_value})" : "ChunkArena(Invalid)";

    public static bool operator ==(ChunkArenaId left, ChunkArenaId right) => left.Equals(right);

    public static bool operator !=(ChunkArenaId left, ChunkArenaId right) => !left.Equals(right);

    private static class Source
    {
        private static long _lastIssued;

        internal static ChunkArenaId Allocate()
        {
            long value = Interlocked.Increment(ref _lastIssued);
            if ((ulong)value > uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The process-lifetime chunk arena identity space is exhausted; arena IDs never wrap or reuse.");
            }

            return new ChunkArenaId((uint)value);
        }
    }
}
