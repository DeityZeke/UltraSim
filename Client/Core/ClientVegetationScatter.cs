using UltraSim.Game.World;
using UltraSim.Game.Composition;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

/// <summary>Presentation-only scatter input. No entity, authority, harvest or persistence identity.</summary>
public readonly record struct ClientVegetationPatch(int CellIndex, int LocalXMillimeters,
    int AbsoluteYMillimeters, int LocalZMillimeters, ushort Seed);

public static class ClientVegetationScatter
{
    public const uint ProfileVersion = 2;
    public const int MaximumPatchesPerChunk = TerrainChunkFormat.MaterialCellCount;
    private const int DensityRegionCellEdge = 4;

    /// <summary>
    /// At most one patch per eligible base cell. A continuous, space-aware 2 m density field
    /// leaves bare ground and sparse/lush regions; fine hash selects cells within that field.
    /// Additional surfaces are deliberately excluded.
    /// </summary>
    public static int Prepare(ClientWorldSemanticCache cache, ClientSemanticChunkHandle handle,
        uint worldSeed, Span<ClientVegetationPatch> destination)
    {
        if (destination.Length < MaximumPatchesPerChunk)
            throw new ArgumentException("A full bounded patch destination is required.", nameof(destination));
        if (!cache.TryGetResidentView(handle, out var view)) return 0;
        int count = 0;
        for (int cell = 0; cell < TerrainChunkFormat.MaterialCellCount; cell++)
        {
            if (view.TerrainMaterials[cell] != GameTerrainMaterialIds.Grass ||
                !TerrainSurfacePresence.IsPresent(view.TerrainSurfacePresence, cell)) continue;
            uint hash = Hash(worldSeed, view.Info.Key, cell);
            if ((hash >> 24) >= DensityThreshold(worldSeed, view.Info.Key, cell)) continue;
            int x = cell % TerrainChunkFormat.MaterialCellEdge * 500 + 100 + (int)(hash % 301);
            int z = cell / TerrainChunkFormat.MaterialCellEdge * 500 + 100 + (int)((hash >> 12) % 301);
            var position = new WorldPosition(view.Info.Key.Coord.X, view.Info.Key.Coord.Z, (ushort)x, (ushort)z, 0);
            bool present = cache.PreparedComposition is { } composition
                ? composition.TrySampleBaseTerrainHeight(position, out int height)
                : cache.PreparedWorld.TrySampleBaseTerrainHeight(position, out height);
            if (present) destination[count++] = new(cell, x, height, z, (ushort)(hash & ushort.MaxValue));
        }
        return count;
    }

    private static int DensityThreshold(uint worldSeed, SpatialChunkKey key, int cell)
    {
        int localX = cell % TerrainChunkFormat.MaterialCellEdge;
        int localZ = cell / TerrainChunkFormat.MaterialCellEdge;
        long worldCellX = (long)key.Coord.X * TerrainChunkFormat.MaterialCellEdge + localX;
        long worldCellZ = (long)key.Coord.Z * TerrainChunkFormat.MaterialCellEdge + localZ;
        long regionX = FloorDivide(worldCellX, DensityRegionCellEdge);
        long regionZ = FloorDivide(worldCellZ, DensityRegionCellEdge);
        int fractionX = checked((int)(worldCellX - regionX * DensityRegionCellEdge));
        int fractionZ = checked((int)(worldCellZ - regionZ * DensityRegionCellEdge));
        int weightX = fractionX * 2 + 1;
        int weightZ = fractionZ * 2 + 1;
        int a = (Noise(worldSeed, key.Space, regionX, regionZ) * (8 - weightX) +
            Noise(worldSeed, key.Space, regionX + 1, regionZ) * weightX) / 8;
        int b = (Noise(worldSeed, key.Space, regionX, regionZ + 1) * (8 - weightX) +
            Noise(worldSeed, key.Space, regionX + 1, regionZ + 1) * weightX) / 8;
        int density = (a * (8 - weightZ) + b * weightZ) / 8;
        return Math.Clamp(density * 3 / 2 - 95, 0, 255);
    }

    private static long FloorDivide(long value, int divisor)
    {
        long quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    private static int Noise(uint worldSeed, SpatialSpaceId space, long x, long z)
    {
        unchecked
        {
            uint h = worldSeed ^ (uint)space.Value ^ (uint)(space.Value >> 32) ^
                (uint)x * 0x9E3779B9u ^ (uint)(x >> 32) * 0xD8163841u ^
                (uint)z * 0x85EBCA6Bu ^ (uint)(z >> 32) * 0xC2B2AE35u ^ ProfileVersion;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu;
            return (int)((h ^ (h >> 16)) & 255);
        }
    }

    public static uint Hash(uint worldSeed, SpatialChunkKey key, int cell)
    {
        unchecked
        {
            uint h = worldSeed ^ (uint)key.Space.Value ^ (uint)(key.Space.Value >> 32) ^
                (uint)key.Coord.X * 0x9E3779B9u ^ (uint)key.Coord.Z * 0x85EBCA6Bu;
            h ^= (uint)cell * 0xC2B2AE35u ^ ProfileVersion;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu;
            return h ^ (h >> 16);
        }
    }
}
