namespace UltraSim.ECS;

/// <summary>
/// Fixed entity-slot reservation for one world.
/// </summary>
public readonly struct EntityCapacity : IEquatable<EntityCapacity>
{
    // generation uint + state byte + free-index int, excluding array headers.
    private const int CurrentMetadataBytesPerSlot = sizeof(uint) + sizeof(byte) + sizeof(int);

    public EntityCapacity(int maximumEntities)
    {
        if (maximumEntities <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntities), maximumEntities, "Entity capacity must be positive.");
        if (maximumEntities > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEntities),
                maximumEntities,
                $"Entity capacity cannot exceed the runtime array limit of {Array.MaxLength} slots.");
        }

        MaximumEntities = maximumEntities;
    }

    public static int MaximumSupportedEntities => Array.MaxLength;

    public int MaximumEntities { get; }

    public long EstimatedMetadataBytes
        => checked((long)MaximumEntities * CurrentMetadataBytesPerSlot);

    public bool Equals(EntityCapacity other) => MaximumEntities == other.MaximumEntities;

    public override bool Equals(object? obj) => obj is EntityCapacity other && Equals(other);

    public override int GetHashCode() => MaximumEntities;

    public static bool operator ==(EntityCapacity left, EntityCapacity right) => left.Equals(right);

    public static bool operator !=(EntityCapacity left, EntityCapacity right) => !left.Equals(right);

    public override string ToString()
        => $"EntityCapacity(Maximum={MaximumEntities}, EstimatedMetadataBytes={EstimatedMetadataBytes})";
}
