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
    public const int HeightQuantumMillimeters = 10;
    public const int MinimumHeightMillimeters = short.MinValue * HeightQuantumMillimeters;
    public const int MaximumHeightMillimeters = short.MaxValue * HeightQuantumMillimeters;
    public const int MaterialPlaneBytes = MaterialCellCount * sizeof(ushort);
    public const int HeightPlaneBytes = HeightVertexCount * sizeof(short);
    public const int FoundationBytes = MaterialPlaneBytes + HeightPlaneBytes;
    public const int DuplicatedBorderVertexCountPerChunk = HeightVertexCount - MaterialCellCount;
    public const int DuplicatedBorderBytesPerChunk = DuplicatedBorderVertexCountPerChunk * sizeof(short);

    public static int GetMaterialIndex(TerrainCellCoord cell) =>
        checked(cell.Z * MaterialCellEdge + cell.X);

    public static int GetHeightIndex(TerrainVertexCoord vertex) =>
        checked(vertex.Z * HeightVertexEdge + vertex.X);
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

/// <summary>A signed 16-bit terrain height with a permanent ten-millimetre quantum.</summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct TerrainHeight : IEquatable<TerrainHeight>, IComparable<TerrainHeight>
{
    public short Units { get; }
    public int Millimeters => checked(Units * TerrainChunkFormat.HeightQuantumMillimeters);

    public TerrainHeight(short units) => Units = units;

    /// <summary>
    /// Quantizes an admitted millimetre height to nearest ten millimetres, midpoint away from zero.
    /// Values outside the permanent decoded terrain range reject rather than clamp.
    /// </summary>
    public static bool TryFromMillimeters(int millimeters, out TerrainHeight height)
    {
        if (millimeters < TerrainChunkFormat.MinimumHeightMillimeters ||
            millimeters > TerrainChunkFormat.MaximumHeightMillimeters)
        {
            height = default;
            return false;
        }

        long magnitude = millimeters < 0 ? -(long)millimeters : millimeters;
        long units = (magnitude + TerrainChunkFormat.HeightQuantumMillimeters / 2) /
            TerrainChunkFormat.HeightQuantumMillimeters;
        if (millimeters < 0) units = -units;
        height = new TerrainHeight(checked((short)units));
        return true;
    }

    public int CompareTo(TerrainHeight other) => Units.CompareTo(other.Units);
    public bool Equals(TerrainHeight other) => Units == other.Units;
    public override bool Equals(object? obj) => obj is TerrainHeight other && Equals(other);
    public override int GetHashCode() => Units;
    public override string ToString() => $"{Millimeters}mm";
    public static bool operator ==(TerrainHeight left, TerrainHeight right) => left.Equals(right);
    public static bool operator !=(TerrainHeight left, TerrainHeight right) => !left.Equals(right);
}

/// <summary>Cold compiler evidence returned after a complete terrain height plane is preflighted.</summary>
public readonly record struct TerrainHeightEncodingSummary(
    int MaximumAbsoluteErrorMillimeters,
    long TotalAbsoluteErrorMillimeters);

/// <summary>Transactional, allocation-free conversion between authored millimetres and terrain height units.</summary>
public static class TerrainHeightEncoding
{
    public static bool TryEncodePlane(
        ReadOnlySpan<int> sourceMillimeters,
        Span<TerrainHeight> destination,
        out int firstInvalidIndex,
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

        int maximumError = 0;
        long totalError = 0;
        for (int index = 0; index < sourceMillimeters.Length; index++)
        {
            if (!TerrainHeight.TryFromMillimeters(sourceMillimeters[index], out TerrainHeight encoded))
            {
                firstInvalidIndex = index;
                summary = default;
                return false;
            }
            int error = Math.Abs(sourceMillimeters[index] - encoded.Millimeters);
            maximumError = Math.Max(maximumError, error);
            totalError = checked(totalError + error);
        }

        for (int index = 0; index < sourceMillimeters.Length; index++)
        {
            bool encoded = TerrainHeight.TryFromMillimeters(sourceMillimeters[index], out destination[index]);
            if (!encoded) throw new InvalidOperationException("Terrain height changed after successful preflight.");
        }

        firstInvalidIndex = -1;
        summary = new TerrainHeightEncodingSummary(maximumError, totalError);
        return true;
    }

    public static void DecodePlane(ReadOnlySpan<TerrainHeight> source, Span<int> destinationMillimeters)
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
            destinationMillimeters[index] = source[index].Millimeters;
    }
}
