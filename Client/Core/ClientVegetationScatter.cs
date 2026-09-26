using UltraSim.Game.World;
using UltraSim.Game.Composition;
using UltraSim.Spatial;

namespace UltraSim.Client.Core;

/// <summary>Presentation-only scatter input. No entity, authority, harvest or persistence identity.</summary>
public readonly record struct ClientVegetationPatch(int CellIndex, int LocalXMillimeters,
    int AbsoluteYMillimeters, int LocalZMillimeters, ushort Seed);

public static class ClientVegetationScatter
{
    public const uint ProfileVersion = 1;
    public const int MaximumPatchesPerChunk = TerrainChunkFormat.MaterialCellCount;

    /// <summary>One bounded patch per eligible base cell; additional surfaces are deliberately excluded.</summary>
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
