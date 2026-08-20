using System.Runtime.InteropServices;
using UltraSim.ECS;

namespace UltraSim.Game.World;

/// <summary>
/// Persisted ECS authority for a dynamic entity that physically occupies the world.
/// </summary>
/// <remarks>
/// Fields are deliberately flattened so the generated persistence codec has a
/// canonical primitive representation. Spatial-index slots are derived and are
/// never persisted.
/// </remarks>
[EcsComponent("game.world.position", PersistenceId = 700603)]
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct WorldPositionComponent : IComponent<WorldPositionComponent>
{
    public int ChunkX;
    public int ChunkZ;
    public ushort LocalXMillimeters;
    public ushort LocalZMillimeters;
    public int YMillimeters;

    public WorldPositionComponent(in WorldPosition value)
    {
        ChunkX = value.Chunk.X;
        ChunkZ = value.Chunk.Z;
        LocalXMillimeters = value.Local.XMillimeters;
        LocalZMillimeters = value.Local.ZMillimeters;
        YMillimeters = value.Local.YMillimeters;
    }

    public readonly bool TryGetValue(out WorldPosition value)
    {
        if (LocalXMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
            LocalZMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits)
        {
            value = default;
            return false;
        }

        value = new WorldPosition(
            ChunkX,
            ChunkZ,
            LocalXMillimeters,
            LocalZMillimeters,
            YMillimeters);
        return true;
    }
}
