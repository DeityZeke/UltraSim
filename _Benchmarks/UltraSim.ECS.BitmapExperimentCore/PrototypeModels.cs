using System.Runtime.InteropServices;

namespace UltraSim.ECS.BitmapExperiment;

public enum PrototypeStorageKind : byte
{
    DensePacked = 0,
    BitmapPersistent = 1,
    BitmapTopologyAware = 2,
    BitmapBarrierLocal = 3,
    BitmapAdaptive = 4,
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public readonly struct PrototypeEntity : IEquatable<PrototypeEntity>
{
    internal PrototypeEntity(int index, uint generation)
    {
        Index = index;
        Generation = generation;
    }

    public int Index { get; }

    public uint Generation { get; }

    public bool IsValid => Index >= 0 && Generation != 0;

    public bool Equals(PrototypeEntity other) =>
        Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is PrototypeEntity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation);

    public static PrototypeEntity Invalid => new(-1, 0);
}

public readonly record struct PrototypeRelocation(int EntityIndex, int SourceRow, int DestinationRow)
{
    public bool HasValue => SourceRow != DestinationRow;
}

public readonly record struct PrototypeTopology(
    int Capacity,
    int LiveCount,
    int HighWaterRows,
    int HoleCount,
    int FullWords,
    int PartialWords,
    int EmptyWords,
    long RowRelocations,
    long RelocatedPayloadBytes,
    long HolesReused,
    long CompactionPasses);
