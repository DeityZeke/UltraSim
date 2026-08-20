using System.Runtime.InteropServices;

namespace UltraSim.Spatial;

/// <summary>Identifies one independent spatial coordinate space.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct SpatialSpaceId : IEquatable<SpatialSpaceId>, IComparable<SpatialSpaceId>
{
    /// <summary>Gets the nonzero spatial-space value.</summary>
    public ulong Value { get; }

    /// <summary>Gets whether this identity names a spatial space.</summary>
    public bool IsValid => Value != 0;

    /// <summary>Creates an explicit spatial-space identity.</summary>
    public SpatialSpaceId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public int CompareTo(SpatialSpaceId other) => Value.CompareTo(other.Value);
    public bool Equals(SpatialSpaceId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SpatialSpaceId other && Equals(other);
    public override int GetHashCode() => SpatialHash.Fold(Value);
    public override string ToString() => IsValid ? $"Space({Value})" : "Space(Invalid)";
    public static bool operator ==(SpatialSpaceId left, SpatialSpaceId right) => left.Equals(right);
    public static bool operator !=(SpatialSpaceId left, SpatialSpaceId right) => !left.Equals(right);
    public static bool operator <(SpatialSpaceId left, SpatialSpaceId right) => left.CompareTo(right) < 0;
    public static bool operator >(SpatialSpaceId left, SpatialSpaceId right) => left.CompareTo(right) > 0;
}

/// <summary>Canonical signed coordinate of one coarse X/Z spatial chunk.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct SpatialChunkCoord : IEquatable<SpatialChunkCoord>, IComparable<SpatialChunkCoord>
{
    public int X { get; }
    public int Z { get; }

    public SpatialChunkCoord(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int CompareTo(SpatialChunkCoord other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }

    public bool Equals(SpatialChunkCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is SpatialChunkCoord other && Equals(other);
    public override int GetHashCode() => SpatialHash.Combine(unchecked((uint)X), unchecked((uint)Z));
    public override string ToString() => $"({X},{Z})";
    public static bool operator ==(SpatialChunkCoord left, SpatialChunkCoord right) => left.Equals(right);
    public static bool operator !=(SpatialChunkCoord left, SpatialChunkCoord right) => !left.Equals(right);
    public static bool operator <(SpatialChunkCoord left, SpatialChunkCoord right) => left.CompareTo(right) < 0;
    public static bool operator >(SpatialChunkCoord left, SpatialChunkCoord right) => left.CompareTo(right) > 0;
}

/// <summary>Canonical spatial-space and chunk address.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct SpatialChunkKey : IEquatable<SpatialChunkKey>, IComparable<SpatialChunkKey>
{
    public SpatialSpaceId Space { get; }
    public SpatialChunkCoord Coord { get; }

    public SpatialChunkKey(SpatialSpaceId space, SpatialChunkCoord coord)
    {
        if (!space.IsValid) throw new ArgumentOutOfRangeException(nameof(space));
        Space = space;
        Coord = coord;
    }

    public SpatialChunkKey(SpatialSpaceId space, int x, int z) : this(space, new SpatialChunkCoord(x, z)) { }

    public int CompareTo(SpatialChunkKey other)
    {
        int space = Space.CompareTo(other.Space);
        return space != 0 ? space : Coord.CompareTo(other.Coord);
    }

    public bool Equals(SpatialChunkKey other) => Space == other.Space && Coord == other.Coord;
    public override bool Equals(object? obj) => obj is SpatialChunkKey other && Equals(other);
    public override int GetHashCode() => SpatialHash.Combine(SpatialHash.Bits(Space.Value), unchecked((uint)Coord.X), unchecked((uint)Coord.Z));
    public override string ToString() => $"{Space}:{Coord}";
    public static bool operator ==(SpatialChunkKey left, SpatialChunkKey right) => left.Equals(right);
    public static bool operator !=(SpatialChunkKey left, SpatialChunkKey right) => !left.Equals(right);
    public static bool operator <(SpatialChunkKey left, SpatialChunkKey right) => left.CompareTo(right) < 0;
    public static bool operator >(SpatialChunkKey left, SpatialChunkKey right) => left.CompareTo(right) > 0;
}

/// <summary>Explicit positive edge geometry for an integer spatial chunk grid.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct SpatialChunkGeometry : IEquatable<SpatialChunkGeometry>
{
    public int EdgeUnits { get; }
    public bool IsValid => EdgeUnits > 0;

    public SpatialChunkGeometry(int edgeUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(edgeUnits);
        EdgeUnits = edgeUnits;
    }

    /// <summary>Normalizes two global axes into a bounded chunk coordinate and nonnegative local axes.</summary>
    public bool TryNormalize(
        long globalX,
        long globalZ,
        out SpatialChunkCoord chunk,
        out int localX,
        out int localZ)
    {
        EnsureValid();
        if (!SpatialMath.TryNormalizeAxis(globalX, EdgeUnits, out int chunkX, out localX) ||
            !SpatialMath.TryNormalizeAxis(globalZ, EdgeUnits, out int chunkZ, out localZ))
        {
            chunk = default;
            localX = 0;
            localZ = 0;
            return false;
        }

        chunk = new SpatialChunkCoord(chunkX, chunkZ);
        return true;
    }

    /// <summary>Reconstructs one global axis from a chunk coordinate and a canonical local value.</summary>
    public bool TryReconstructAxis(int chunk, int local, out long global)
    {
        EnsureValid();
        if ((uint)local >= (uint)EdgeUnits)
        {
            global = 0;
            return false;
        }

        global = checked((long)chunk * EdgeUnits + local);
        return true;
    }

    /// <summary>Constructs the half-open global X/Z bounds of one chunk.</summary>
    public SpatialBounds GetBounds(SpatialChunkCoord chunk)
    {
        EnsureValid();
        long minX = checked((long)chunk.X * EdgeUnits);
        long minZ = checked((long)chunk.Z * EdgeUnits);
        return new SpatialBounds(minX, minZ, checked(minX + EdgeUnits), checked(minZ + EdgeUnits));
    }

    public bool Equals(SpatialChunkGeometry other) => EdgeUnits == other.EdgeUnits;
    public override bool Equals(object? obj) => obj is SpatialChunkGeometry other && Equals(other);
    public override int GetHashCode() => EdgeUnits;
    public override string ToString() => IsValid ? $"Edge({EdgeUnits})" : "Edge(Invalid)";
    public static bool operator ==(SpatialChunkGeometry left, SpatialChunkGeometry right) => left.Equals(right);
    public static bool operator !=(SpatialChunkGeometry left, SpatialChunkGeometry right) => !left.Equals(right);

    private void EnsureValid()
    {
        if (!IsValid) throw new InvalidOperationException("Default or invalid spatial geometry has no canonical math.");
    }
}

/// <summary>Half-open integer X/Z bounds: minimum inclusive, maximum exclusive.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct SpatialBounds : IEquatable<SpatialBounds>
{
    public long MinX { get; }
    public long MinZ { get; }
    public long MaxXExclusive { get; }
    public long MaxZExclusive { get; }
    public bool IsValid => MaxXExclusive > MinX && MaxZExclusive > MinZ;

    public SpatialBounds(long minX, long minZ, long maxXExclusive, long maxZExclusive)
    {
        if (maxXExclusive <= minX) throw new ArgumentOutOfRangeException(nameof(maxXExclusive));
        if (maxZExclusive <= minZ) throw new ArgumentOutOfRangeException(nameof(maxZExclusive));
        MinX = minX;
        MinZ = minZ;
        MaxXExclusive = maxXExclusive;
        MaxZExclusive = maxZExclusive;
    }

    /// <summary>Tests half-open point ownership.</summary>
    public bool Contains(long x, long z) =>
        x >= MinX && x < MaxXExclusive && z >= MinZ && z < MaxZExclusive;

    /// <summary>Tests half-open area intersection; edge-only contact is not an overlap.</summary>
    public bool Intersects(in SpatialBounds other) =>
        IsValid && other.IsValid &&
        MinX < other.MaxXExclusive && MaxXExclusive > other.MinX &&
        MinZ < other.MaxZExclusive && MaxZExclusive > other.MinZ;

    /// <summary>
    /// Returns exact squared X/Z distance to the geometric AABB. The exclusive maximum remains
    /// an ownership rule; touching that boundary has zero geometric distance for interest tests.
    /// </summary>
    public Int128 SquaredDistanceTo(long x, long z)
    {
        if (!IsValid) throw new InvalidOperationException("Default bounds have no distance semantics.");
        Int128 wideX = x;
        Int128 wideZ = z;
        Int128 dx = x < MinX ? (Int128)MinX - wideX : x > MaxXExclusive ? wideX - MaxXExclusive : 0;
        Int128 dz = z < MinZ ? (Int128)MinZ - wideZ : z > MaxZExclusive ? wideZ - MaxZExclusive : 0;
        return checked(dx * dx + dz * dz);
    }

    /// <summary>Tests exact point-to-AABB circular overlap, including radius contact.</summary>
    public bool IntersectsCircle(long centerX, long centerZ, long radiusUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusUnits);
        Int128 radius = radiusUnits;
        return SquaredDistanceTo(centerX, centerZ) <= checked(radius * radius);
    }

    public bool Equals(SpatialBounds other) =>
        MinX == other.MinX && MinZ == other.MinZ &&
        MaxXExclusive == other.MaxXExclusive && MaxZExclusive == other.MaxZExclusive;
    public override bool Equals(object? obj) => obj is SpatialBounds other && Equals(other);
    public override int GetHashCode() => SpatialHash.Combine(
        SpatialHash.Bits(unchecked((ulong)MinX)),
        SpatialHash.Bits(unchecked((ulong)MinZ)),
        SpatialHash.Bits(unchecked((ulong)MaxXExclusive)),
        SpatialHash.Bits(unchecked((ulong)MaxZExclusive)));
    public override string ToString() => $"[{MinX},{MaxXExclusive})x[{MinZ},{MaxZExclusive})";
    public static bool operator ==(SpatialBounds left, SpatialBounds right) => left.Equals(right);
    public static bool operator !=(SpatialBounds left, SpatialBounds right) => !left.Equals(right);
}

/// <summary>Canonical checked integer math shared by spatial adapters.</summary>
public static class SpatialMath
{
    /// <summary>Computes mathematical floor division and a nonnegative remainder.</summary>
    public static void FloorDivRem(long value, int positiveDivisor, out long quotient, out int remainder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(positiveDivisor);
        quotient = value / positiveDivisor;
        long rawRemainder = value % positiveDivisor;
        if (rawRemainder < 0)
        {
            quotient--;
            rawRemainder += positiveDivisor;
        }

        remainder = checked((int)rawRemainder);
    }

    /// <summary>Normalizes one global axis when its chunk coordinate fits signed 32-bit identity.</summary>
    public static bool TryNormalizeAxis(long value, int positiveEdge, out int chunk, out int local)
    {
        FloorDivRem(value, positiveEdge, out long wideChunk, out local);
        if (wideChunk < int.MinValue || wideChunk > int.MaxValue)
        {
            chunk = 0;
            local = 0;
            return false;
        }

        chunk = (int)wideChunk;
        return true;
    }

    /// <summary>Computes exact squared distance without overflowing signed 64-bit subtraction.</summary>
    public static Int128 SquaredDistance(long leftX, long leftZ, long rightX, long rightZ)
    {
        Int128 dx = (Int128)leftX - rightX;
        Int128 dz = (Int128)leftZ - rightZ;
        return checked(dx * dx + dz * dz);
    }
}

internal static class SpatialHash
{
    private const uint Offset = 2_166_136_261;
    private const uint Prime = 16_777_619;

    internal static uint Bits(ulong value) => unchecked((uint)value ^ (uint)(value >> 32));
    internal static int Fold(ulong value) => unchecked((int)Mix(Offset, Bits(value)));

    internal static int Combine(uint first, uint second)
    {
        uint hash = Mix(Mix(Offset, first), second);
        return unchecked((int)hash);
    }

    internal static int Combine(uint first, uint second, uint third)
    {
        uint hash = Mix(Mix(Mix(Offset, first), second), third);
        return unchecked((int)hash);
    }

    internal static int Combine(uint first, uint second, uint third, uint fourth)
    {
        uint hash = Mix(Mix(Mix(Mix(Offset, first), second), third), fourth);
        return unchecked((int)hash);
    }

    private static uint Mix(uint hash, uint value)
    {
        unchecked
        {
            hash = (hash ^ (byte)value) * Prime;
            hash = (hash ^ (byte)(value >> 8)) * Prime;
            hash = (hash ^ (byte)(value >> 16)) * Prime;
            return (hash ^ (byte)(value >> 24)) * Prime;
        }
    }
}
