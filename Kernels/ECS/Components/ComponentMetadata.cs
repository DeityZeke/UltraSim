using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Compact metadata used by storage and query construction. Names and runtime type
/// handles live in parallel cold arrays owned by <see cref="ComponentSchema"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly struct ComponentMetadata : IEquatable<ComponentMetadata>
{
    private readonly uint _persistenceId;
    private readonly int _size;
    private readonly ushort _id;
    private readonly ushort _version;
    private readonly byte _alignment;
    private readonly ComponentFlags _flags;
    private readonly ComponentPersistenceMode _persistenceMode;
    private readonly byte _reserved;

    internal ComponentMetadata(
        ComponentId id,
        int size,
        byte alignment,
        ushort version,
        ComponentFlags flags,
        uint persistenceId,
        ComponentPersistenceMode persistenceMode)
    {
        _persistenceId = persistenceId;
        _size = size;
        _id = id.Value;
        _version = version;
        _alignment = alignment;
        _flags = flags;
        _persistenceMode = persistenceMode;
        _reserved = 0;
    }

    public ComponentId Id => new(_id);

    /// <summary>Stored bytes per entity. Tags deliberately report zero.</summary>
    public int Size => _size;

    public byte Alignment => _alignment;

    public ushort Version => _version;

    public ComponentFlags Flags => _flags;

    public uint PersistenceId => _persistenceId;

    public ComponentPersistenceMode PersistenceMode => _persistenceMode;

    public bool IsTag => (_flags & ComponentFlags.Tag) != 0;

    public bool IsEnableable => (_flags & ComponentFlags.Enableable) != 0;

    public bool IsPersistent => (_flags & ComponentFlags.Persistent) != 0;

    public bool Equals(ComponentMetadata other) =>
        _persistenceId == other._persistenceId &&
        _size == other._size &&
        _id == other._id &&
        _version == other._version &&
        _alignment == other._alignment &&
        _flags == other._flags &&
        _persistenceMode == other._persistenceMode;

    public override bool Equals(object? obj) => obj is ComponentMetadata other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _persistenceId,
        _size,
        _id,
        _version,
        _alignment,
        _flags,
        _persistenceMode);
}
