using System.Runtime.InteropServices;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Structural WorldPack grid address, independent of World and spatial-space identity.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct WorldPackCoord : IEquatable<WorldPackCoord>, IComparable<WorldPackCoord>
{
    public int X { get; }
    public int Z { get; }

    public WorldPackCoord(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int CompareTo(WorldPackCoord other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }

    public bool Equals(WorldPackCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is WorldPackCoord other && Equals(other);
    public override int GetHashCode() => unchecked(X * 397 ^ Z);
    public override string ToString() => $"Pack({X},{Z})";
    public static bool operator ==(WorldPackCoord left, WorldPackCoord right) => left.Equals(right);
    public static bool operator !=(WorldPackCoord left, WorldPackCoord right) => !left.Equals(right);
    public static bool operator <(WorldPackCoord left, WorldPackCoord right) => left.CompareTo(right) < 0;
    public static bool operator >(WorldPackCoord left, WorldPackCoord right) => left.CompareTo(right) > 0;
}

/// <summary>Bounded 0..255 X/Z chunk address within one structural WorldPack.</summary>
[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct WorldPackLocalChunkCoord : IEquatable<WorldPackLocalChunkCoord>, IComparable<WorldPackLocalChunkCoord>
{
    public byte X { get; }
    public byte Z { get; }

    public WorldPackLocalChunkCoord(int x, int z)
    {
        if (x < 0 || x >= WorldPackAddressing.ChunksPerWorldPackEdge)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (z < 0 || z >= WorldPackAddressing.ChunksPerWorldPackEdge)
            throw new ArgumentOutOfRangeException(nameof(z));
        X = checked((byte)x);
        Z = checked((byte)z);
    }

    public int CompareTo(WorldPackLocalChunkCoord other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }

    public bool Equals(WorldPackLocalChunkCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is WorldPackLocalChunkCoord other && Equals(other);
    public override int GetHashCode() => X | Z << 8;
    public override string ToString() => $"Local({X},{Z})";
    public static bool operator ==(WorldPackLocalChunkCoord left, WorldPackLocalChunkCoord right) => left.Equals(right);
    public static bool operator !=(WorldPackLocalChunkCoord left, WorldPackLocalChunkCoord right) => !left.Equals(right);
    public static bool operator <(WorldPackLocalChunkCoord left, WorldPackLocalChunkCoord right) => left.CompareTo(right) < 0;
    public static bool operator >(WorldPackLocalChunkCoord left, WorldPackLocalChunkCoord right) => left.CompareTo(right) > 0;
}

/// <summary>Canonical centered partition of global SpatialChunk coordinates into WorldPack addresses.</summary>
public static class WorldPackAddressing
{
    public const int ChunksPerWorldPackEdge = 256;
    public const int CenteringBiasChunks = ChunksPerWorldPackEdge / 2;
    public const int WorldPackEdgeUnits = ChunksPerWorldPackEdge * GameWorldSpatialProfile.WorldChunkEdgeUnits;
    public const int WorldPackEdgeMeters = WorldPackEdgeUnits / GameWorldSpatialProfile.UnitsPerMeter;

    /// <summary>Maps every signed-32-bit global chunk coordinate to exactly one Pack and local address.</summary>
    public static void ToPackLocal(
        SpatialChunkCoord global,
        out WorldPackCoord pack,
        out WorldPackLocalChunkCoord local)
    {
        SpatialMath.FloorDivRem(checked((long)global.X + CenteringBiasChunks), ChunksPerWorldPackEdge,
            out long packX, out int localX);
        SpatialMath.FloorDivRem(checked((long)global.Z + CenteringBiasChunks), ChunksPerWorldPackEdge,
            out long packZ, out int localZ);
        pack = new WorldPackCoord(checked((int)packX), checked((int)packZ));
        local = new WorldPackLocalChunkCoord(localX, localZ);
    }

    /// <summary>Reconstructs a global chunk if both axes fit canonical signed-32-bit Spatial identity.</summary>
    public static bool TryToGlobal(
        WorldPackCoord pack,
        WorldPackLocalChunkCoord local,
        out SpatialChunkCoord global)
    {
        long x = checked((long)pack.X * ChunksPerWorldPackEdge + local.X - CenteringBiasChunks);
        long z = checked((long)pack.Z * ChunksPerWorldPackEdge + local.Z - CenteringBiasChunks);
        if (x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue)
        {
            global = default;
            return false;
        }

        global = new SpatialChunkCoord((int)x, (int)z);
        return true;
    }

    /// <summary>Tests whether this Pack address owns the global chunk coordinate (without a SpaceId).</summary>
    public static bool Contains(WorldPackCoord pack, SpatialChunkCoord global)
    {
        ToPackLocal(global, out WorldPackCoord owner, out _);
        return owner == pack;
    }
}
