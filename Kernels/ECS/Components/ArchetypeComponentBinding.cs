using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Compact cold-built route to one generated component column in one world archetype.
/// </summary>
/// <remarks>
/// The binding contains no storage reference and cannot expose a component ref. It is
/// valid only for entities currently in its exact archetype. Value zero is invalid.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
internal readonly struct ArchetypeComponentBinding<T> : IEquatable<ArchetypeComponentBinding<T>>
    where T : unmanaged, IComponent<T>
{
    private readonly uint _worldId;
    private readonly uint _archetypeId;
    private readonly ushort _componentId;
    private readonly ushort _columnIndex;
    private readonly ComponentFlags _flags;
    private readonly byte _reservedByte;
    private readonly ushort _reservedWord;

    internal ArchetypeComponentBinding(
        WorldId worldId,
        ArchetypeId archetypeId,
        ComponentId componentId,
        int columnIndex,
        ComponentFlags flags)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (!archetypeId.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(archetypeId));
        if (!componentId.IsValid)
            throw new ArgumentException("A valid component ID is required.", nameof(componentId));
        if (unchecked((uint)columnIndex) >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        _worldId = worldId.Value;
        _archetypeId = archetypeId.Value;
        _componentId = componentId.Value;
        _columnIndex = (ushort)columnIndex;
        _flags = flags;
        _reservedByte = 0;
        _reservedWord = 0;
    }

    internal WorldId WorldId => new(_worldId);

    internal ArchetypeId ArchetypeId => new(_archetypeId);

    internal ComponentId ComponentId => new(_componentId);

    internal int ColumnIndex => _columnIndex;

    internal ComponentFlags Flags => _flags;

    internal bool IsValid => _worldId != 0 && _archetypeId != 0;

    internal bool HasData => IsValid && (_flags & ComponentFlags.Tag) == 0;

    internal bool IsEnableable => IsValid && (_flags & ComponentFlags.Enableable) != 0;

    public bool Equals(ArchetypeComponentBinding<T> other) =>
        _worldId == other._worldId &&
        _archetypeId == other._archetypeId &&
        _componentId == other._componentId &&
        _columnIndex == other._columnIndex &&
        _flags == other._flags;

    public override bool Equals(object? obj) =>
        obj is ArchetypeComponentBinding<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _worldId,
        _archetypeId,
        _componentId,
        _columnIndex,
        _flags);

    public static bool operator ==(
        ArchetypeComponentBinding<T> left,
        ArchetypeComponentBinding<T> right) => left.Equals(right);

    public static bool operator !=(
        ArchetypeComponentBinding<T> left,
        ArchetypeComponentBinding<T> right) => !left.Equals(right);
}
