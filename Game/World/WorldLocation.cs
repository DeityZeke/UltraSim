using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>One authoritative spatial address. Equal coordinates in different spaces do not meet.</summary>
public readonly record struct WorldLocation(SpatialSpaceId Space, WorldPosition Position)
{
    public bool IsValid => Space.IsValid;
    public SpatialChunkKey ChunkKey => Position.ToChunkKey(Space);
}
