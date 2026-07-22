using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Compact runtime-only entity handle containing a world, slot, and generation.
/// </summary>
/// <remarks>
/// The 12-byte layout is an initial measured-design candidate. Runtime handles
/// are never durable persistence identifiers. Slot index zero is valid; the
/// invalid/null state is represented by zero world and generation values.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
public readonly struct Entity : IEquatable<Entity>
{
    public static Entity Null => default;

    private readonly uint _worldId;
    private readonly uint _index;
    private readonly uint _generation;

    internal Entity(WorldId worldId, uint index, uint generation)
    {
        _worldId = worldId.Value;
        _index = index;
        _generation = generation;
    }

    public WorldId World => new(_worldId);

    public uint Index => _index;

    public uint Generation => _generation;

    public bool IsValid => _worldId != 0 && _generation != 0;

    public bool Equals(Entity other)
        => _worldId == other._worldId
            && _index == other._index
            && _generation == other._generation;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_worldId, _index, _generation);

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

    public override string ToString()
        => IsValid
            ? $"Entity({_worldId}:{_index}:{_generation})"
            : "Entity(Null)";
}
