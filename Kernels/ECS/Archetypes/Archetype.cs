using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed class Archetype
{
    private const int DefaultEntityCapacity = 1024;
    private const int MinGrowth = 64;

    private Entity[] _entities;
    private int _entityCount;
    private readonly ComponentId[] _signatureIds;
    private readonly IComponentColumn[] _columns;
    private readonly int[] _componentToColumn;

    public ArchetypeSignature Signature { get; }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _entityCount;
    }

    public ReadOnlySpan<ComponentId> ComponentIds
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _signatureIds;
    }

    internal Archetype(ArchetypeSignature signature, ComponentId[] componentIds, IComponentColumn[] columns)
    {
        Signature = signature;
        _signatureIds = componentIds;
        _columns = columns;
        _entities = new Entity[DefaultEntityCapacity];
        _entityCount = 0;

        int maxComponentId = 0;
        for (int i = 0; i < componentIds.Length; i++)
        {
            if (componentIds[i].Value > maxComponentId)
                maxComponentId = componentIds[i].Value;
        }

        _componentToColumn = componentIds.Length == 0
            ? Array.Empty<int>()
            : new int[maxComponentId + 1];

        if (_componentToColumn.Length > 0)
            _componentToColumn.AsSpan().Fill(-1);

        for (int i = 0; i < componentIds.Length; i++)
            _componentToColumn[componentIds[i].Value] = i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int AddEntity(Entity entity)
    {
        int slot = _entityCount;
        EnsureEntityCapacity(slot + 1);
        _entities[slot] = entity;
        _entityCount = slot + 1;

        for (int i = 0; i < _columns.Length; i++)
        {
            _columns[i].EnsureCapacity(_entityCount);
            _columns[i].SetDefaultAt(slot);
        }

        return slot;
    }

    internal int AddEntityUninitializedAssumeCapacity(Entity entity)
    {
        int slot = _entityCount;
        if (slot >= _entities.Length)
            throw new InvalidOperationException("Archetype capacity must be reserved before uninitialized add.");

        _entities[slot] = entity;
        _entityCount = slot + 1;
        return slot;
    }

    internal int AddEntitiesUninitialized(ReadOnlySpan<Entity> entities)
    {
        int count = entities.Length;
        if (count == 0)
            return _entityCount;

        int start = _entityCount;
        int needed = start + count;
        EnsureEntityCapacity(needed);
        entities.CopyTo(_entities.AsSpan(start, count));
        _entityCount = needed;

        for (int i = 0; i < _columns.Length; i++)
            _columns[i].EnsureCapacity(needed);

        return start;
    }

    internal void Reserve(int additionalCount)
    {
        if (additionalCount <= 0)
            return;

        int needed = _entityCount + additionalCount;
        EnsureEntityCapacity(needed);
        for (int i = 0; i < _columns.Length; i++)
            _columns[i].EnsureCapacity(needed);
    }

    internal bool RemoveAtSwap(int slot, out Entity movedEntity, out int movedSlot)
    {
        movedEntity = default;
        movedSlot = -1;

        int last = _entityCount - 1;
        if ((uint)slot > (uint)last)
            return false;

        if (slot != last)
        {
            movedEntity = _entities[last];
            movedSlot = slot;
            _entities[slot] = movedEntity;

            for (int i = 0; i < _columns.Length; i++)
                _columns[i].SwapLastIntoSlot(slot, last);
        }

        _entityCount = last;
        return true;
    }

    public Span<Entity> GetEntitySpan() => _entities.AsSpan(0, _entityCount);

    public ref Entity GetEntityRef(int slot)
    {
        if ((uint)slot >= (uint)_entityCount)
            ThrowOutOfRange(slot);

        return ref _entities[slot];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent(ComponentId id)
    {
        int value = id.Value;
        return (uint)value < (uint)_componentToColumn.Length && _componentToColumn[value] >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetColumnIndex(ComponentId id)
    {
        int value = id.Value;
        return (uint)value < (uint)_componentToColumn.Length ? _componentToColumn[value] : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetComponentSpan<T>(ComponentId id) where T : unmanaged, IComponent<T>
    {
        int idValue = id.Value;
        if ((uint)idValue < (uint)_componentToColumn.Length)
        {
            int colIndex = _componentToColumn[idValue];
            if (colIndex >= 0 && _columns[colIndex] is ComponentColumn<T> typed)
                return typed.AsSpan(_entityCount);
        }

        throw new InvalidOperationException($"Component column not found for id {id}.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Span<T> GetComponentSpanAt<T>(int columnIndex) where T : unmanaged, IComponent<T>
    {
        if ((uint)columnIndex < (uint)_columns.Length && _columns[columnIndex] is ComponentColumn<T> typed)
            return typed.AsSpan(_entityCount);

        throw new InvalidOperationException($"Component column not found for index {columnIndex}.");
    }

    internal IComponentColumn GetColumnAt(int columnIndex) => _columns[columnIndex];

    private void EnsureEntityCapacity(int needed)
    {
        if (needed <= _entities.Length)
            return;

        int newSize = Math.Max(_entities.Length * 2, Math.Max(needed, _entities.Length + MinGrowth));
        var newArray = new Entity[newSize];
        _entities.AsSpan(0, _entityCount).CopyTo(newArray);
        _entities = newArray;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowOutOfRange(int slot)
        => throw new ArgumentOutOfRangeException(nameof(slot), $"Slot {slot} out of range [0, {_entityCount}).");
}
