using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Transient generational handle for one chunk slot in one arena.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
public readonly struct ChunkHandle : IEquatable<ChunkHandle>
{
    public static ChunkHandle Null => default;

    internal ChunkHandle(ChunkArenaId arenaId, uint index, uint generation)
    {
        ArenaId = arenaId;
        Index = index;
        Generation = generation;
    }

    public ChunkArenaId ArenaId { get; }

    public uint Index { get; }

    public uint Generation { get; }

    public bool IsValid => ArenaId.IsValid && Generation != 0;

    public bool Equals(ChunkHandle other) =>
        ArenaId == other.ArenaId && Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is ChunkHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ArenaId, Index, Generation);

    public override string ToString() => IsValid
        ? $"Chunk({ArenaId.Value}:{Index}:{Generation})"
        : "Chunk(Null)";

    public static bool operator ==(ChunkHandle left, ChunkHandle right) => left.Equals(right);

    public static bool operator !=(ChunkHandle left, ChunkHandle right) => !left.Equals(right);
}
