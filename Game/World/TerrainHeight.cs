using System.Runtime.InteropServices;

namespace UltraSim.Game.World;

/// <summary>Frozen packed terrain-foundation geometry and storage for WorldPack terrain v1.</summary>
public static class TerrainChunkFormat
{
    public const int MaterialCellEdge = GameWorldSpatialProfile.TerrainCellsPerChunkEdge;
    public const int MaterialCellCount = MaterialCellEdge * MaterialCellEdge;
    public const int HeightVertexEdge = MaterialCellEdge + 1;
    public const int HeightVertexCount = HeightVertexEdge * HeightVertexEdge;
    public const int TriangleCount = MaterialCellCount * 2;
    public const int TriangleIndexCount = TriangleCount * 3;
    public const int HeightQuantumMillimeters = 5;
    public const int MaximumHeightReliefMillimeters = ushort.MaxValue * HeightQuantumMillimeters;
    public const int MinimumQuantizedHeightMillimeters = -2_147_483_645;
    public const int MaximumQuantizedHeightMillimeters = 2_147_483_645;
    public const int MaterialPlaneBytes = MaterialCellCount * sizeof(ushort);
    public const int HeightPlaneBaseBytes = sizeof(int);
    public const int HeightOffsetPlaneBytes = HeightVertexCount * sizeof(ushort);
    public const int HeightPlaneBytes = HeightPlaneBaseBytes + HeightOffsetPlaneBytes;
    public const int SurfacePresenceWordCount = MaterialCellCount / 64;
    public const int SurfacePresencePlaneBytes = SurfacePresenceWordCount * sizeof(ulong);
    // Keep the frozen T0 dense-foundation accounting stable. T1 presence is an optional section:
    // all-present chunks retain the exact historical representation and byte identity.
    public const int FoundationBytes = MaterialPlaneBytes + HeightPlaneBytes;
    public const int FoundationBytesWithSurfacePresence = FoundationBytes + SurfacePresencePlaneBytes;
    public const int DuplicatedBorderVertexCountPerChunk = HeightVertexCount - MaterialCellCount;
    public const int DuplicatedBorderBytesPerChunk = DuplicatedBorderVertexCountPerChunk * sizeof(ushort);

    public static int GetMaterialIndex(TerrainCellCoord cell) =>
        checked(cell.Z * MaterialCellEdge + cell.X);

    public static int GetHeightIndex(TerrainVertexCoord vertex) =>
        checked(vertex.Z * HeightVertexEdge + vertex.X);
}

/// <summary>
/// Canonical one-bit-per-cell topology plane for the dense base terrain. A clear bit means
/// intentional resident void; it is not a material identity or missing chunk data.
/// </summary>
public static class TerrainSurfacePresence
{
    public static void FillPresent(Span<ulong> destination)
    {
        ValidateLength(destination.Length);
        destination.Fill(ulong.MaxValue);
    }

    public static bool IsPresent(ReadOnlySpan<ulong> source, int materialCellIndex)
    {
        ValidateLength(source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            (uint)materialCellIndex, (uint)TerrainChunkFormat.MaterialCellCount);
        return (source[materialCellIndex >> 6] & (1UL << (materialCellIndex & 63))) != 0;
    }

    public static bool IsPresent(ReadOnlySpan<ulong> source, TerrainCellCoord cell) =>
        IsPresent(source, TerrainChunkFormat.GetMaterialIndex(cell));

    public static void SetPresent(Span<ulong> destination, TerrainCellCoord cell, bool present)
    {
        ValidateLength(destination.Length);
        int index = TerrainChunkFormat.GetMaterialIndex(cell);
        ulong mask = 1UL << (index & 63);
        ref ulong word = ref destination[index >> 6];
        if (present) word |= mask;
        else word &= ~mask;
    }

    public static bool HasAnyAbsent(ReadOnlySpan<ulong> source)
    {
        ValidateLength(source.Length);
        for (int index = 0; index < source.Length; index++)
            if (source[index] != ulong.MaxValue) return true;
        return false;
    }

    private static void ValidateLength(int length)
    {
        if (length != TerrainChunkFormat.SurfacePresenceWordCount)
            throw new ArgumentException("A terrain presence plane must contain exactly one bit per base cell.");
    }
}

/// <summary>Canonical terrain vertex coordinate within one chunk, including its positive duplicated edge.</summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct TerrainVertexCoord : IEquatable<TerrainVertexCoord>, IComparable<TerrainVertexCoord>
{
    public byte X { get; }
    public byte Z { get; }

    public int LocalXMillimeters => X * GameWorldSpatialProfile.TerrainCellEdgeUnits;
    public int LocalZMillimeters => Z * GameWorldSpatialProfile.TerrainCellEdgeUnits;

    public TerrainVertexCoord(byte x, byte z)
    {
        if (x >= TerrainChunkFormat.HeightVertexEdge)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (z >= TerrainChunkFormat.HeightVertexEdge)
            throw new ArgumentOutOfRangeException(nameof(z));
        X = x;
        Z = z;
    }

    public int CompareTo(TerrainVertexCoord other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }

    public bool Equals(TerrainVertexCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is TerrainVertexCoord other && Equals(other);
    public override int GetHashCode() => X | Z << 8;
    public override string ToString() => $"({X},{Z})";
    public static bool operator ==(TerrainVertexCoord left, TerrainVertexCoord right) => left.Equals(right);
    public static bool operator !=(TerrainVertexCoord left, TerrainVertexCoord right) => !left.Equals(right);
}

/// <summary>Validated absolute millimetre base for one terrain height plane.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct TerrainHeightPlaneBase : IEquatable<TerrainHeightPlaneBase>, IComparable<TerrainHeightPlaneBase>
{
    public int Millimeters { get; }

    private TerrainHeightPlaneBase(int millimeters) => Millimeters = millimeters;

    public static bool TryCreate(int millimeters, out TerrainHeightPlaneBase value)
    {
        if (millimeters < TerrainChunkFormat.MinimumQuantizedHeightMillimeters ||
            millimeters > TerrainChunkFormat.MaximumQuantizedHeightMillimeters ||
            millimeters % TerrainChunkFormat.HeightQuantumMillimeters != 0)
        {
            value = default;
            return false;
        }
        value = new TerrainHeightPlaneBase(millimeters);
        return true;
    }

    public int CompareTo(TerrainHeightPlaneBase other) => Millimeters.CompareTo(other.Millimeters);
    public bool Equals(TerrainHeightPlaneBase other) => Millimeters == other.Millimeters;
    public override bool Equals(object? obj) => obj is TerrainHeightPlaneBase other && Equals(other);
    public override int GetHashCode() => Millimeters;
    public override string ToString() => $"{Millimeters}mm";
    public static bool operator ==(TerrainHeightPlaneBase left, TerrainHeightPlaneBase right) => left.Equals(right);
    public static bool operator !=(TerrainHeightPlaneBase left, TerrainHeightPlaneBase right) => !left.Equals(right);
}

/// <summary>Unsigned five-millimetre offset from one terrain plane's validated absolute base.</summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct TerrainHeightOffset : IEquatable<TerrainHeightOffset>, IComparable<TerrainHeightOffset>
{
    public ushort Units { get; }
    public int OffsetMillimeters => Units * TerrainChunkFormat.HeightQuantumMillimeters;

    public TerrainHeightOffset(ushort units) => Units = units;

    public int CompareTo(TerrainHeightOffset other) => Units.CompareTo(other.Units);
    public bool Equals(TerrainHeightOffset other) => Units == other.Units;
    public override bool Equals(object? obj) => obj is TerrainHeightOffset other && Equals(other);
    public override int GetHashCode() => Units;
    public override string ToString() => $"+{OffsetMillimeters}mm";
    public static bool operator ==(TerrainHeightOffset left, TerrainHeightOffset right) => left.Equals(right);
    public static bool operator !=(TerrainHeightOffset left, TerrainHeightOffset right) => !left.Equals(right);
}

/// <summary>Cold compiler evidence returned after a complete terrain height plane is preflighted.</summary>
public readonly record struct TerrainHeightEncodingSummary(
    int MaximumAbsoluteErrorMillimeters,
    long TotalAbsoluteErrorMillimeters,
    int ReliefMillimeters);

/// <summary>Transactional, allocation-free conversion between absolute authored heights and based offsets.</summary>
public static class TerrainHeightEncoding
{
    public static bool TryEncodePlane(
        ReadOnlySpan<int> sourceMillimeters,
        Span<TerrainHeightOffset> destination,
        out TerrainHeightPlaneBase planeBase,
        out int failureIndex,
        out TerrainHeightEncodingSummary summary)
    {
        if (sourceMillimeters.Length != TerrainChunkFormat.HeightVertexCount)
            throw new ArgumentException("A terrain height source must contain exactly one complete vertex plane.",
                nameof(sourceMillimeters));
        if (destination.Length < TerrainChunkFormat.HeightVertexCount)
            throw new ArgumentException("The terrain height destination is too small.", nameof(destination));
        if (MemoryMarshal.AsBytes(sourceMillimeters).Overlaps(MemoryMarshal.AsBytes(destination)))
            throw new ArgumentException("Terrain height source and destination storage must not overlap.",
                nameof(destination));

        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        int maximumIndex = -1;
        int maximumError = 0;
        long totalError = 0;
        for (int index = 0; index < sourceMillimeters.Length; index++)
        {
            if (!TryQuantizeMillimeters(sourceMillimeters[index], out int quantized))
            {
                planeBase = default;
                failureIndex = index;
                summary = default;
                return false;
            }
            if (quantized < minimum) minimum = quantized;
            if (quantized > maximum)
            {
                maximum = quantized;
                maximumIndex = index;
            }
            int error = Math.Abs(sourceMillimeters[index] - quantized);
            maximumError = Math.Max(maximumError, error);
            totalError = checked(totalError + error);
        }

        long relief = (long)maximum - minimum;
        if (relief > TerrainChunkFormat.MaximumHeightReliefMillimeters ||
            !TerrainHeightPlaneBase.TryCreate(minimum, out planeBase))
        {
            planeBase = default;
            failureIndex = maximumIndex;
            summary = default;
            return false;
        }

        for (int index = 0; index < sourceMillimeters.Length; index++)
        {
            bool quantized = TryQuantizeMillimeters(sourceMillimeters[index], out int absolute);
            if (!quantized) throw new InvalidOperationException("Terrain height changed after successful preflight.");
            int units = checked((absolute - minimum) / TerrainChunkFormat.HeightQuantumMillimeters);
            destination[index] = new TerrainHeightOffset(checked((ushort)units));
        }

        failureIndex = -1;
        summary = new TerrainHeightEncodingSummary(maximumError, totalError, checked((int)relief));
        return true;
    }

    public static bool TryDecodePlane(
        TerrainHeightPlaneBase planeBase,
        ReadOnlySpan<TerrainHeightOffset> source,
        Span<int> destinationMillimeters,
        out int failureIndex)
    {
        if (source.Length != TerrainChunkFormat.HeightVertexCount)
            throw new ArgumentException("A terrain height source must contain exactly one complete vertex plane.",
                nameof(source));
        if (destinationMillimeters.Length < TerrainChunkFormat.HeightVertexCount)
            throw new ArgumentException("The terrain height destination is too small.", nameof(destinationMillimeters));
        if (MemoryMarshal.AsBytes(source).Overlaps(MemoryMarshal.AsBytes(destinationMillimeters)))
            throw new ArgumentException("Terrain height source and destination storage must not overlap.",
                nameof(destinationMillimeters));

        for (int index = 0; index < source.Length; index++)
        {
            long absolute = (long)planeBase.Millimeters + source[index].OffsetMillimeters;
            if (absolute is < int.MinValue or > int.MaxValue)
            {
                failureIndex = index;
                return false;
            }
        }
        for (int index = 0; index < source.Length; index++)
            destinationMillimeters[index] = checked(planeBase.Millimeters + source[index].OffsetMillimeters);
        failureIndex = -1;
        return true;
    }

    private static bool TryQuantizeMillimeters(int millimeters, out int quantized)
    {
        long magnitude = millimeters < 0 ? -(long)millimeters : millimeters;
        long units = (magnitude + TerrainChunkFormat.HeightQuantumMillimeters / 2) /
            TerrainChunkFormat.HeightQuantumMillimeters;
        if (millimeters < 0) units = -units;
        long decoded = units * TerrainChunkFormat.HeightQuantumMillimeters;
        if (decoded is < int.MinValue or > int.MaxValue)
        {
            quantized = 0;
            return false;
        }
        quantized = (int)decoded;
        return true;
    }
}
