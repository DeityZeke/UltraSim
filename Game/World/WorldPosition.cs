using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Frozen integer geometry and authoring-policy values for the first game world profile.</summary>
public static class GameWorldSpatialProfile
{
    public const int UnitsPerMeter = 1_000;
    public const int AuthoritativePositionQuantumUnits = 1;
    public const int WorldChunkEdgeUnits = 16_000;
    public const int TerrainCellEdgeUnits = 500;
    public const int TerrainCellsPerChunkEdge = 32;
    public const int PlacementSnapUnits = 100;

    public const long MinimumGlobalAxisUnits = (long)int.MinValue * WorldChunkEdgeUnits;
    public const long MaximumGlobalAxisUnits = (long)int.MaxValue * WorldChunkEdgeUnits + WorldChunkEdgeUnits - 1;

    public static SpatialChunkGeometry ChunkGeometry { get; } = new(WorldChunkEdgeUnits);
}

/// <summary>Canonical chunk-local dynamic position with unrestricted signed millimeter height.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct WorldLocalPosition : IEquatable<WorldLocalPosition>
{
    public ushort XMillimeters { get; }
    public ushort ZMillimeters { get; }
    public int YMillimeters { get; }

    public WorldLocalPosition(ushort xMillimeters, ushort zMillimeters, int yMillimeters)
    {
        if (xMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits)
            throw new ArgumentOutOfRangeException(nameof(xMillimeters));
        if (zMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits)
            throw new ArgumentOutOfRangeException(nameof(zMillimeters));
        XMillimeters = xMillimeters;
        ZMillimeters = zMillimeters;
        YMillimeters = yMillimeters;
    }

    public bool Equals(WorldLocalPosition other) =>
        XMillimeters == other.XMillimeters && ZMillimeters == other.ZMillimeters &&
        YMillimeters == other.YMillimeters;
    public override bool Equals(object? obj) => obj is WorldLocalPosition other && Equals(other);
    public override int GetHashCode() => unchecked(
        (int)((uint)XMillimeters * 16_777_619u ^ (uint)ZMillimeters * 2_166_136_261u ^ (uint)YMillimeters));
    public static bool operator ==(WorldLocalPosition left, WorldLocalPosition right) => left.Equals(right);
    public static bool operator !=(WorldLocalPosition left, WorldLocalPosition right) => !left.Equals(right);
}

/// <summary>
/// Game-owned authoritative world value. It is not an ECS component and carries no subsystem-local slot identity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct WorldPosition : IEquatable<WorldPosition>, IComparable<WorldPosition>
{
    public SpatialChunkCoord Chunk { get; }
    public WorldLocalPosition Local { get; }

    public long GlobalXMillimeters => checked((long)Chunk.X * GameWorldSpatialProfile.WorldChunkEdgeUnits + Local.XMillimeters);
    public long GlobalZMillimeters => checked((long)Chunk.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits + Local.ZMillimeters);
    public int YMillimeters => Local.YMillimeters;

    public WorldPosition(SpatialChunkCoord chunk, WorldLocalPosition local)
    {
        Chunk = chunk;
        Local = local;
    }

    public WorldPosition(int chunkX, int chunkZ, ushort localXMillimeters, ushort localZMillimeters, int yMillimeters)
        : this(new SpatialChunkCoord(chunkX, chunkZ),
            new WorldLocalPosition(localXMillimeters, localZMillimeters, yMillimeters)) { }

    /// <summary>Normalizes exact authoritative millimeters without clamping.</summary>
    public static bool TryFromGlobalMillimeters(
        long globalXMillimeters,
        int yMillimeters,
        long globalZMillimeters,
        out WorldPosition position)
    {
        SpatialChunkGeometry geometry = GameWorldSpatialProfile.ChunkGeometry;
        if (!geometry.TryNormalize(globalXMillimeters, globalZMillimeters,
                out SpatialChunkCoord chunk, out int localX, out int localZ))
        {
            position = default;
            return false;
        }

        position = new WorldPosition(chunk,
            new WorldLocalPosition(checked((ushort)localX), checked((ushort)localZ), yMillimeters));
        return true;
    }

    /// <summary>
    /// Imports finite meter values using nearest millimeter, midpoint away from zero. Invalid or
    /// unrepresentable inputs fail rather than clamp or saturate.
    /// </summary>
    public static bool TryFromMeters(double xMeters, double yMeters, double zMeters, out WorldPosition position)
    {
        if (!TryQuantizeMeters(xMeters,
                GameWorldSpatialProfile.MinimumGlobalAxisUnits,
                GameWorldSpatialProfile.MaximumGlobalAxisUnits,
                out long xMillimeters) ||
            !TryQuantizeMeters(zMeters,
                GameWorldSpatialProfile.MinimumGlobalAxisUnits,
                GameWorldSpatialProfile.MaximumGlobalAxisUnits,
                out long zMillimeters) ||
            !TryQuantizeMeters(yMeters, int.MinValue, int.MaxValue, out long yMillimeters))
        {
            position = default;
            return false;
        }

        return TryFromGlobalMillimeters(xMillimeters, checked((int)yMillimeters), zMillimeters, out position);
    }

    public SpatialChunkKey ToChunkKey(SpatialSpaceId space) => new(space, Chunk);

    public int CompareTo(WorldPosition other)
    {
        int chunk = Chunk.CompareTo(other.Chunk);
        if (chunk != 0) return chunk;
        int x = Local.XMillimeters.CompareTo(other.Local.XMillimeters);
        if (x != 0) return x;
        int z = Local.ZMillimeters.CompareTo(other.Local.ZMillimeters);
        return z != 0 ? z : Local.YMillimeters.CompareTo(other.Local.YMillimeters);
    }

    public bool Equals(WorldPosition other) => Chunk == other.Chunk && Local == other.Local;
    public override bool Equals(object? obj) => obj is WorldPosition other && Equals(other);
    public override int GetHashCode() => unchecked(Chunk.GetHashCode() * 397 ^ Local.GetHashCode());
    public override string ToString() =>
        $"Chunk{Chunk}+({Local.XMillimeters},{Local.YMillimeters},{Local.ZMillimeters})mm";
    public static bool operator ==(WorldPosition left, WorldPosition right) => left.Equals(right);
    public static bool operator !=(WorldPosition left, WorldPosition right) => !left.Equals(right);

    private static bool TryQuantizeMeters(double meters, long minimum, long maximum, out long millimeters)
    {
        if (!double.IsFinite(meters))
        {
            millimeters = 0;
            return false;
        }

        double scaled = meters * GameWorldSpatialProfile.UnitsPerMeter;
        if (!double.IsFinite(scaled) || scaled < minimum || scaled > maximum)
        {
            millimeters = 0;
            return false;
        }

        double rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
        if (rounded < minimum || rounded > maximum)
        {
            millimeters = 0;
            return false;
        }

        millimeters = checked((long)rounded);
        return true;
    }
}

/// <summary>Canonical terrain-cell coordinate within one 16 m game-world chunk.</summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct TerrainCellCoord : IEquatable<TerrainCellCoord>, IComparable<TerrainCellCoord>
{
    public byte X { get; }
    public byte Z { get; }

    public TerrainCellCoord(byte x, byte z)
    {
        if (x >= GameWorldSpatialProfile.TerrainCellsPerChunkEdge)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (z >= GameWorldSpatialProfile.TerrainCellsPerChunkEdge)
            throw new ArgumentOutOfRangeException(nameof(z));
        X = x;
        Z = z;
    }

    public static TerrainCellCoord FromLocal(in WorldLocalPosition local) => new(
        checked((byte)(local.XMillimeters / GameWorldSpatialProfile.TerrainCellEdgeUnits)),
        checked((byte)(local.ZMillimeters / GameWorldSpatialProfile.TerrainCellEdgeUnits)));

    public int CompareTo(TerrainCellCoord other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }

    public bool Equals(TerrainCellCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is TerrainCellCoord other && Equals(other);
    public override int GetHashCode() => X | Z << 8;
    public override string ToString() => $"({X},{Z})";
    public static bool operator ==(TerrainCellCoord left, TerrainCellCoord right) => left.Equals(right);
    public static bool operator !=(TerrainCellCoord left, TerrainCellCoord right) => !left.Equals(right);
}
