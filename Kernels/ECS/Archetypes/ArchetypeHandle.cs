using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Checked public identity of one archetype in one runtime world.</summary>
/// <remarks>
/// Unlike the dense four-byte <see cref="ArchetypeId"/>, this handle includes the
/// owning world and therefore rejects accidental cross-world ID collisions.
/// It is transient routing data and must not be persisted.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public readonly struct ArchetypeHandle : IEquatable<ArchetypeHandle>
{
    private readonly uint _worldId;
    private readonly uint _archetypeId;

    internal ArchetypeHandle(WorldId worldId, ArchetypeId archetypeId)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (!archetypeId.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(archetypeId));

        _worldId = worldId.Value;
        _archetypeId = archetypeId.Value;
    }

    public static ArchetypeHandle Invalid => default;

    public WorldId World => new(_worldId);

    public bool IsValid => _worldId != 0 && _archetypeId != 0;

    internal ArchetypeId Id => new(_archetypeId);

    public bool Equals(ArchetypeHandle other) =>
        _worldId == other._worldId && _archetypeId == other._archetypeId;

    public override bool Equals(object? obj) => obj is ArchetypeHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_worldId, _archetypeId);

    public override string ToString() => IsValid
        ? $"Archetype({_worldId}:{_archetypeId})"
        : "Archetype(Invalid)";

    public static bool operator ==(ArchetypeHandle left, ArchetypeHandle right) => left.Equals(right);

    public static bool operator !=(ArchetypeHandle left, ArchetypeHandle right) => !left.Equals(right);
}
