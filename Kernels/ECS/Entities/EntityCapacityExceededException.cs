namespace UltraSim.ECS;

public sealed class EntityCapacityExceededException : InvalidOperationException
{
    public EntityCapacityExceededException(WorldId worldId, EntityCapacity capacity)
        : base($"Entity capacity {capacity.MaximumEntities} is exhausted for {worldId}.")
    {
        WorldId = worldId;
        Capacity = capacity;
    }

    public WorldId WorldId { get; }

    public EntityCapacity Capacity { get; }
}
