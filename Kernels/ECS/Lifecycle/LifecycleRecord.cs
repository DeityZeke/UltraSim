using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>One fixed-size fact from a world's ordered lifecycle history.</summary>
/// <remarks>
/// <see cref="Entity"/> remains the exact runtime generation/tombstone involved
/// in the commit. <see cref="ComponentId"/> is valid only for component-added
/// and component-removed records. Records contain no component payload,
/// persistent identity, prefab identity, or physical storage location.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 24)]
public readonly struct LifecycleRecord : IEquatable<LifecycleRecord>
{
    private readonly ulong _sequence;
    private readonly Entity _entity;
    private readonly ComponentId _componentId;
    private readonly LifecycleKind _kind;
    private readonly byte _reserved;

    internal LifecycleRecord(
        ulong sequence,
        LifecycleKind kind,
        Entity entity,
        ComponentId componentId)
    {
        _sequence = sequence;
        _entity = entity;
        _componentId = componentId;
        _kind = kind;
        _reserved = 0;
    }

    /// <summary>Gets the wrap-safe world-local sequence assigned at commit.</summary>
    public ulong Sequence => _sequence;

    /// <summary>Gets the exact runtime identity or destroyed-generation tombstone.</summary>
    public Entity Entity => _entity;

    /// <summary>Gets the lifecycle fact kind.</summary>
    public LifecycleKind Kind => _kind;

    /// <summary>
    /// Gets the schema-local component identity for membership records, or
    /// <see cref="ComponentId.Invalid"/> for create/destroy records.
    /// </summary>
    public ComponentId ComponentId => _componentId;

    /// <inheritdoc/>
    public bool Equals(LifecycleRecord other) =>
        _sequence == other._sequence &&
        _entity == other._entity &&
        _componentId == other._componentId &&
        _kind == other._kind;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LifecycleRecord other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_sequence, _entity, _componentId, _kind);

    public static bool operator ==(LifecycleRecord left, LifecycleRecord right) => left.Equals(right);

    public static bool operator !=(LifecycleRecord left, LifecycleRecord right) => !left.Equals(right);
}
