using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Compact world-local route from an entity slot to its archetype-owned row.
/// </summary>
/// <remarks>
/// Generation remains authoritative in <see cref="EntityPool"/> and is not
/// duplicated here. This value is transient and must never be serialized.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
public readonly struct EntityLocation : IEquatable<EntityLocation>
{
    internal EntityLocation(ArchetypeId archetypeId, ChunkRow chunkRow)
    {
        if (!archetypeId.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(archetypeId));
        if (!chunkRow.IsValid)
            throw new ArgumentException("A valid chunk-row location is required.", nameof(chunkRow));

        ArchetypeId = archetypeId;
        ChunkRow = chunkRow;
    }

    public static EntityLocation Invalid => default;

    public ArchetypeId ArchetypeId { get; }

    public ChunkRow ChunkRow { get; }

    public bool IsValid => ArchetypeId.IsValid && ChunkRow.IsValid;

    public bool Equals(EntityLocation other) =>
        ArchetypeId == other.ArchetypeId && ChunkRow == other.ChunkRow;

    public override bool Equals(object? obj) => obj is EntityLocation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ArchetypeId, ChunkRow);

    public override string ToString() => IsValid
        ? $"EntityLocation({ArchetypeId.Value}:{ChunkRow.ChunkIndex}:{ChunkRow.Row})"
        : "EntityLocation(Invalid)";

    public static bool operator ==(EntityLocation left, EntityLocation right) => left.Equals(right);

    public static bool operator !=(EntityLocation left, EntityLocation right) => !left.Equals(right);
}
