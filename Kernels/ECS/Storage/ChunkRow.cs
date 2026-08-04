using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Compact location inside one archetype-owned arena. The owning archetype is
/// intentionally external to keep per-entity location metadata at eight bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public readonly struct ChunkRow : IEquatable<ChunkRow>
{
    private readonly uint _rowPlusOne;

    internal ChunkRow(uint chunkIndex, int row)
    {
        if (row < 0)
            throw new ArgumentOutOfRangeException(nameof(row));

        ChunkIndex = chunkIndex;
        _rowPlusOne = checked((uint)row + 1);
    }

    public static ChunkRow Invalid => default;

    public uint ChunkIndex { get; }

    public int Row => IsValid ? checked((int)(_rowPlusOne - 1)) : -1;

    public bool IsValid => _rowPlusOne != 0;

    public bool Equals(ChunkRow other) =>
        ChunkIndex == other.ChunkIndex && _rowPlusOne == other._rowPlusOne;

    public override bool Equals(object? obj) => obj is ChunkRow other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ChunkIndex, _rowPlusOne);

    public override string ToString() => IsValid ? $"ChunkRow({ChunkIndex}:{Row})" : "ChunkRow(Invalid)";

    public static bool operator ==(ChunkRow left, ChunkRow right) => left.Equals(right);

    public static bool operator !=(ChunkRow left, ChunkRow right) => !left.Equals(right);
}
