using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Cold compatibility hook that reconstructs one explicitly registered historical runtime shape.</summary>
public interface IPersistenceShapeMigrator
{
    void Migrate(PersistenceShapeMigrationContext context);
}

/// <summary>Rebuilds a declared derived component after persisted values and references are available.</summary>
public interface IPersistenceDerivedReconstructor
{
    void Reconstruct(PersistenceReconstructionContext context);
}

/// <summary>Read-only historical encoded column exposed only to an admitted shape migrator.</summary>
public readonly struct PersistenceSavedColumn
{
    internal PersistenceSavedColumn(uint persistenceId, ushort version, int encodedSize, ReadOnlyMemory<byte> bytes)
    { PersistenceId = persistenceId; Version = version; EncodedSize = encodedSize; Bytes = bytes; }
    public uint PersistenceId { get; }
    public ushort Version { get; }
    public int EncodedSize { get; }
    public int Count => EncodedSize == 0 ? 0 : Bytes.Length / EncodedSize;
    public ReadOnlyMemory<byte> Bytes { get; }
    public ReadOnlySpan<byte> GetEncodedValue(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)Count)) throw new ArgumentOutOfRangeException(nameof(row));
        return Bytes.Span.Slice(row * EncodedSize, EncodedSize);
    }
}

/// <summary>Read-only historical enable bitplane exposed only to an admitted shape migrator.</summary>
public readonly struct PersistenceSavedEnablePlane
{
    internal PersistenceSavedEnablePlane(uint persistenceId, int rowCount, ReadOnlyMemory<byte> bits)
    { PersistenceId = persistenceId; RowCount = rowCount; Bits = bits; }
    public uint PersistenceId { get; }
    public int RowCount { get; }
    public ReadOnlyMemory<byte> Bits { get; }
    public bool IsEnabled(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)RowCount)) throw new ArgumentOutOfRangeException(nameof(row));
        return (Bits.Span[row >> 3] & (1 << (row & 7))) != 0;
    }
}

/// <summary>Validated staging-world entities and typed read/write operations for derived reconstruction.</summary>
public class PersistenceReconstructionContext
{
    private readonly EcsWorld _world;
    private readonly Entity[] _entities;
    internal PersistenceReconstructionContext(EcsWorld world, Entity[] entities) { _world = world; _entities = entities; }
    public EcsWorld World => _world;
    public ReadOnlySpan<Entity> Entities => _entities;
    public T Read<T>(int row) where T : unmanaged, IComponent<T>
    {
        if (unchecked((uint)row) >= unchecked((uint)_entities.Length)) throw new ArgumentOutOfRangeException(nameof(row));
        if (!_world.Schema.TryGetComponentId<T>(out ComponentId id)) throw new ArgumentException("Component is not in the current schema.");
        T value = default;
        Span<T> typed = MemoryMarshal.CreateSpan(ref value, 1);
        _world.PersistenceCore.CopyPersistenceComponent(_entities[row], id, MemoryMarshal.AsBytes(typed));
        return value;
    }
    public void Write<T>(int row, in T value) where T : unmanaged, IComponent<T>
    {
        if (unchecked((uint)row) >= unchecked((uint)_entities.Length)) throw new ArgumentOutOfRangeException(nameof(row));
        if (!_world.Schema.TryGetComponentId<T>(out ComponentId id)) throw new ArgumentException("Component is not in the current schema.");
        ReadOnlySpan<T> typed = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1);
        _world.PersistenceCore.RestorePersistenceComponent(_entities[row], id, MemoryMarshal.AsBytes(typed));
    }
    public void SetEnabled<T>(int row, bool enabled) where T : unmanaged, IComponent<T>
    {
        if (unchecked((uint)row) >= unchecked((uint)_entities.Length)) throw new ArgumentOutOfRangeException(nameof(row));
        if (!_world.Schema.TryGetComponentId<T>(out ComponentId id) || !_world.Schema.GetMetadata(id).IsEnableable)
            throw new ArgumentException("Component is absent or not enableable in the current schema.");
        _world.PersistenceCore.RestorePersistenceEnabled(_entities[row], id, enabled);
    }
}

/// <summary>Derived reconstruction context extended with the explicit historical columns/enable planes.</summary>
public sealed class PersistenceShapeMigrationContext : PersistenceReconstructionContext
{
    private readonly PersistenceSavedColumn[] _columns;
    private readonly PersistenceSavedEnablePlane[] _enables;
    internal PersistenceShapeMigrationContext(EcsWorld world, Entity[] entities, PersistenceSavedColumn[] columns,
        PersistenceSavedEnablePlane[] enables)
        : base(world, entities) { _columns = columns; _enables = enables; }
    public bool TryGetSavedColumn(uint persistenceId, out PersistenceSavedColumn column)
    {
        foreach (PersistenceSavedColumn candidate in _columns)
        {
            if (candidate.PersistenceId == persistenceId) { column = candidate; return true; }
        }
        column = default;
        return false;
    }
    public bool TryGetSavedEnablePlane(uint persistenceId, out PersistenceSavedEnablePlane enable)
    {
        foreach (PersistenceSavedEnablePlane candidate in _enables)
        {
            if (candidate.PersistenceId == persistenceId) { enable = candidate; return true; }
        }
        enable = default;
        return false;
    }
}

internal sealed class ShapeMigrationBinding
{
    internal required uint[] SavedIds;
    internal required ComponentId[] CurrentComponents;
    internal required IPersistenceShapeMigrator Migrator;
}
