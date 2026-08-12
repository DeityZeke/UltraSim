using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Compact world-local direct handle for one compiled prefab.</summary>
/// <remarks>
/// This transient handle is assigned by deterministic catalog key order. Persist
/// <see cref="PrefabKey"/>, not this value.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
public readonly struct PrefabHandle : IEquatable<PrefabHandle>
{
    private readonly uint _worldId;
    private readonly uint _prefabId;
    private readonly uint _archetypeId;

    internal PrefabHandle(WorldId worldId, uint prefabId, ArchetypeId archetypeId)
    {
        if (!worldId.IsValid || prefabId == 0 || !archetypeId.IsValid)
            throw new ArgumentException("A prefab handle requires valid world, prefab, and archetype identities.");
        _worldId = worldId.Value;
        _prefabId = prefabId;
        _archetypeId = archetypeId.Value;
    }

    public static PrefabHandle Invalid => default;

    public bool IsValid => _worldId != 0 && _prefabId != 0 && _archetypeId != 0;

    public WorldId World => new(_worldId);

    public ArchetypeHandle TargetArchetype => IsValid
        ? new ArchetypeHandle(new WorldId(_worldId), new ArchetypeId(_archetypeId))
        : ArchetypeHandle.Invalid;

    internal uint PrefabId => _prefabId;

    internal ArchetypeId ArchetypeId => new(_archetypeId);

    public bool Equals(PrefabHandle other) =>
        _worldId == other._worldId && _prefabId == other._prefabId &&
        _archetypeId == other._archetypeId;

    public override bool Equals(object? obj) => obj is PrefabHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_worldId, _prefabId, _archetypeId);

    public override string ToString() => IsValid
        ? $"Prefab({_worldId}:{_prefabId}->{_archetypeId})"
        : "Prefab(Invalid)";

    public static bool operator ==(PrefabHandle left, PrefabHandle right) => left.Equals(right);

    public static bool operator !=(PrefabHandle left, PrefabHandle right) => !left.Equals(right);
}
