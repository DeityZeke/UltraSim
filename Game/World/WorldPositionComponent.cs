using System.Runtime.InteropServices;
using UltraSim.ECS;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>
/// Persisted ECS authority for a dynamic entity that physically occupies the world.
/// </summary>
/// <remarks>
/// Fields are deliberately flattened so the generated persistence codec has a
/// canonical primitive representation. Spatial-index slots are derived and are
/// never persisted.
/// </remarks>
[EcsComponent("game.world.position", Version = 2, PersistenceId = 700603)]
[StructLayout(LayoutKind.Sequential, Size = 24)]
public struct WorldPositionComponent : IComponent<WorldPositionComponent>
{
    public int ChunkX;
    public int ChunkZ;
    public ushort LocalXMillimeters;
    public ushort LocalZMillimeters;
    public int YMillimeters;
    public ulong SpatialSpaceValue;

    public WorldPositionComponent(in WorldPosition value)
        : this(new WorldLocation(GameWorldState.DefaultSpace, value)) { }

    public WorldPositionComponent(in WorldLocation location)
    {
        if (!location.IsValid)
            throw new ArgumentOutOfRangeException(nameof(location));
        ChunkX = location.Position.Chunk.X;
        ChunkZ = location.Position.Chunk.Z;
        LocalXMillimeters = location.Position.Local.XMillimeters;
        LocalZMillimeters = location.Position.Local.ZMillimeters;
        YMillimeters = location.Position.Local.YMillimeters;
        SpatialSpaceValue = location.Space.Value;
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

    public readonly bool TryGetLocation(out WorldLocation location)
    {
        if (!TryGetValue(out WorldPosition position))
        {
            location = default;
            return false;
        }
        // Zero is the default-initialized/legacy component and still denotes the original space.
        location = new WorldLocation(SpatialSpaceValue == 0
            ? GameWorldState.DefaultSpace : new SpatialSpaceId(SpatialSpaceValue), position);
        return true;
    }
}
