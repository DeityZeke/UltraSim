using System.Collections.ObjectModel;
using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>
/// Bounded memory policy for the generation-owned inventory index.
/// <see cref="MaximumItems"/> bounds both indexed live items and the aggregate
/// child-slot capacity reserved by all container lists.
/// </summary>
public readonly record struct InventoryLimits(
    int MaximumContainers = 256,
    int MaximumItems = 4_096,
    int MaximumItemsPerContainer = 256)
{
    public static InventoryLimits Default => new();

    public void Validate()
    {
        if (MaximumContainers <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumContainers));
        if (MaximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumItems));
        if (MaximumItemsPerContainer <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumItemsPerContainer));
    }
}

/// <summary>One indexed item relationship, reconstructed from ECS truth.</summary>
public readonly record struct InventoryLocation(
    Entity Parent,
    InventoryEquipSlot EquipSlot,
    int ContainerRow = -1);

/// <summary>Bounded logical-capacity accounting for one inventory index.</summary>
public readonly record struct InventoryIndexSnapshot(
    int ContainerCount,
    int ItemCount,
    int ReservedContainerItemSlots,
    int MaximumContainers,
    int MaximumItems,
    int MaximumItemsPerContainer)
{
    public const string Exclusions =
        "CLR dictionary/list object headers, hash-table load-factor slack, and allocator alignment.";
}

/// <summary>
/// Generation-owned derived index. ECS components remain authoritative; this
/// type only accelerates container enumeration and equipment-slot checks.
/// </summary>
public sealed class InventoryIndex : IDisposable
{
    private Dictionary<Entity, ContainerEntry> _containers;
    private Dictionary<Entity, InventoryLocation> _locations;
    private Dictionary<(Entity Character, InventoryEquipSlot Slot), Entity> _equipped;
    private readonly InventoryLimits _limits;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _reservedContainerItemSlots;
    private bool _disposed;

    public InventoryIndex(InventoryLimits limits)
    {
        limits.Validate();
        _limits = limits;
        _containers = new Dictionary<Entity, ContainerEntry>(limits.MaximumContainers);
        _locations = new Dictionary<Entity, InventoryLocation>(limits.MaximumItems);
        _equipped = new Dictionary<(Entity Character, InventoryEquipSlot Slot), Entity>(limits.MaximumItems);
    }

    public int ContainerCount
    {
        get
        {
            EnsureOperational();
            return _containers.Count;
        }
    }

    public int ItemCount
    {
        get
        {
            EnsureOperational();
            return _locations.Count;
        }
    }

    public bool ContainsContainer(Entity container)
    {
        EnsureOperational();
        return _containers.ContainsKey(container);
    }

    /// <summary>Returns indexed children without scanning the ECS population.</summary>
    public IReadOnlyList<Entity> GetItems(Entity container)
    {
        EnsureOperational();
        return _containers.TryGetValue(container, out ContainerEntry? entry)
            ? entry.Items
            : Array.Empty<Entity>();
    }

    public bool TryGetLocation(Entity item, out InventoryLocation location)
    {
        EnsureOperational();
        return _locations.TryGetValue(item, out location);
    }

    public bool TryGetEquipped(Entity character, InventoryEquipSlot slot, out Entity item)
    {
        EnsureOperational();
        return _equipped.TryGetValue((character, slot), out item);
    }

    public InventoryIndexSnapshot CaptureSnapshot()
    {
        EnsureOperational();
        return new InventoryIndexSnapshot(
            _containers.Count,
            _locations.Count,
            _reservedContainerItemSlots,
            _limits.MaximumContainers,
            _limits.MaximumItems,
            _limits.MaximumItemsPerContainer);
    }

    /// <summary>
    /// Rebuilds the index transactionally from current ECS truth. The scan is a
    /// rebuild operation only; normal container enumeration uses the maps above.
    /// </summary>
    public void Rebuild(
        EcsWorld world,
        in ComponentLookup<InventoryCharacter> characterLookup,
        in ComponentLookup<InventoryContainer> containerLookup,
        in ComponentLookup<InventoryItem> itemLookup)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(world);
        EnsureOwner(world);

        var containers = new Dictionary<Entity, ContainerEntry>(_limits.MaximumContainers);
        var locations = new Dictionary<Entity, InventoryLocation>(_limits.MaximumItems);
        var equipped = new Dictionary<(Entity Character, InventoryEquipSlot Slot), Entity>(_limits.MaximumItems);
        var items = new List<(Entity Entity, InventoryItem Value)>(_limits.MaximumItems);
        int reservedContainerItemSlots = 0;
        int maximumEntities = world.Options.EntityCapacity.MaximumEntities;

        for (uint index = 0; index < unchecked((uint)maximumEntities); index++)
        {
            if (!world.TryGetEntity(index, out Entity entity))
                continue;

            if (world.TryRead(entity, containerLookup, out InventoryContainer container))
            {
                ValidateContainer(world, entity, container, characterLookup);
                if (containers.Count >= _limits.MaximumContainers)
                    throw new InvalidOperationException("The inventory container index limit was exceeded.");
                if (container.Capacity > _limits.MaximumItemsPerContainer)
                    throw new InvalidOperationException("An inventory container exceeds the per-container index limit.");
                reservedContainerItemSlots = checked(reservedContainerItemSlots + container.Capacity);
                if (reservedContainerItemSlots > _limits.MaximumItems)
                    throw new InvalidOperationException("Inventory container reservations exceed the item-slot limit.");
                containers.Add(entity, new ContainerEntry(container.Capacity));
            }

            if (world.TryRead(entity, itemLookup, out InventoryItem item))
            {
                ValidateItem(item);
                if (items.Count >= _limits.MaximumItems)
                    throw new InvalidOperationException("The inventory item index limit was exceeded.");
                items.Add((entity, item));
            }
        }

        foreach ((Entity entity, InventoryItem item) in items)
        {
            if (!item.Parent.IsValid || !world.IsAlive(item.Parent))
                throw new InvalidOperationException("An inventory item references a stale parent.");

            if (item.EquipSlot == InventoryEquipSlot.None)
            {
                if (!containers.TryGetValue(item.Parent, out ContainerEntry? container))
                    throw new InvalidOperationException("A contained inventory item references a non-container.");
                int containerRow = container.MutableItems.Count;
                AddToContainer(container, entity);
                locations.Add(entity, new InventoryLocation(item.Parent, item.EquipSlot, containerRow));
            }
            else
            {
                if (!world.Has(item.Parent, characterLookup))
                    throw new InvalidOperationException("An equipped inventory item references a non-character.");
                if (!equipped.TryAdd((item.Parent, item.EquipSlot), entity))
                    throw new InvalidOperationException("Two inventory items occupy the same equipment slot.");
                locations.Add(entity, new InventoryLocation(item.Parent, item.EquipSlot));
            }
        }

        _containers = containers;
        _locations = locations;
        _equipped = equipped;
        _reservedContainerItemSlots = reservedContainerItemSlots;
    }

    internal bool CanAddContainer(int capacity) =>
        _containers.Count < _limits.MaximumContainers &&
        capacity > 0 && capacity <= _limits.MaximumItemsPerContainer &&
        capacity <= _limits.MaximumItems - _reservedContainerItemSlots;

    internal bool CanAddContainer(Entity container, int capacity) =>
        !_containers.ContainsKey(container) && CanAddContainer(capacity);

    internal bool TryAddContainer(Entity container, int capacity)
    {
        EnsureOperational();
        if (!CanAddContainer(container, capacity))
            return false;
        _containers.Add(container, new ContainerEntry(capacity));
        _reservedContainerItemSlots = checked(_reservedContainerItemSlots + capacity);
        return true;
    }

    internal bool CanRemoveContainer(Entity container) =>
        _containers.TryGetValue(container, out ContainerEntry? entry) &&
        entry.MutableItems.Count == 0;

    internal bool TryRemoveContainer(Entity container)
    {
        EnsureOperational();
        if (!CanRemoveContainer(container))
            return false;
        int capacity = _containers[container].Capacity;
        if (!_containers.Remove(container))
            return false;
        _reservedContainerItemSlots -= capacity;
        return true;
    }

    internal bool CanAddItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (_locations.ContainsKey(item) || _locations.Count >= _limits.MaximumItems)
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
        {
            return _containers.TryGetValue(value.Parent, out ContainerEntry? container) &&
                container.MutableItems.Count < container.Capacity &&
                container.MutableItems.Count < _limits.MaximumItemsPerContainer;
        }
        return value.EquipSlot == InventoryEquipSlot.MainHand &&
            !_equipped.ContainsKey((value.Parent, value.EquipSlot));
    }

    internal bool CanAddContainedItem(Entity container) =>
        _locations.Count < _limits.MaximumItems &&
        _containers.TryGetValue(container, out ContainerEntry? entry) &&
        entry.MutableItems.Count < entry.Capacity &&
        entry.MutableItems.Count < _limits.MaximumItemsPerContainer;

    internal bool TryAddItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!CanAddItem(item, value))
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
        {
            ContainerEntry container = _containers[value.Parent];
            int containerRow = container.MutableItems.Count;
            container.MutableItems.Add(item);
            _locations.Add(item, new InventoryLocation(value.Parent, value.EquipSlot, containerRow));
        }
        else
        {
            _equipped.Add((value.Parent, value.EquipSlot), item);
            _locations.Add(item, new InventoryLocation(value.Parent, value.EquipSlot));
        }
        return true;
    }

    internal bool CanRemoveItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!_locations.TryGetValue(item, out InventoryLocation location) ||
            location.Parent != value.Parent || location.EquipSlot != value.EquipSlot)
            return false;
        return value.EquipSlot == InventoryEquipSlot.None
            ? _containers.TryGetValue(value.Parent, out ContainerEntry? container) &&
                IsExactContainerRow(container, location.ContainerRow, item)
            : _equipped.TryGetValue((value.Parent, value.EquipSlot), out Entity equipped) &&
                equipped == item;
    }

    internal bool TryRemoveItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!CanRemoveItem(item, value))
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
            RemoveContainerRow(_containers[value.Parent], location: _locations[item]);
        else
            _equipped.Remove((value.Parent, value.EquipSlot));
        _locations.Remove(item);
        return true;
    }

    internal bool CanMoveItem(Entity item, Entity source, Entity destination)
    {
        EnsureOperational();
        if (!_locations.TryGetValue(item, out InventoryLocation location) ||
            location.Parent != source || location.EquipSlot != InventoryEquipSlot.None ||
            !_containers.TryGetValue(source, out ContainerEntry? sourceEntry) ||
            !_containers.TryGetValue(destination, out ContainerEntry? destinationEntry) ||
            !IsExactContainerRow(sourceEntry, location.ContainerRow, item))
            return false;
        return source == destination ||
            (destinationEntry.MutableItems.Count < destinationEntry.Capacity &&
             destinationEntry.MutableItems.Count < _limits.MaximumItemsPerContainer);
    }

    internal bool TryMoveItem(Entity item, Entity source, Entity destination)
    {
        EnsureOperational();
        if (!CanMoveItem(item, source, destination))
            return false;
        if (source == destination)
            return true;
        ContainerEntry sourceEntry = _containers[source];
        ContainerEntry destinationEntry = _containers[destination];
        InventoryLocation sourceLocation = _locations[item];
        RemoveContainerRow(sourceEntry, sourceLocation);
        int destinationRow = destinationEntry.MutableItems.Count;
        destinationEntry.MutableItems.Add(item);
        _locations[item] = new InventoryLocation(destination, InventoryEquipSlot.None, destinationRow);
        return true;
    }

    internal bool CanEquipItem(Entity item, Entity source, Entity character, InventoryEquipSlot slot)
    {
        EnsureOperational();
        return slot == InventoryEquipSlot.MainHand &&
            _locations.TryGetValue(item, out InventoryLocation location) &&
            location.Parent == source && location.EquipSlot == InventoryEquipSlot.None &&
            _containers.TryGetValue(source, out ContainerEntry? sourceEntry) &&
            IsExactContainerRow(sourceEntry, location.ContainerRow, item) &&
            !_equipped.ContainsKey((character, slot));
    }

    internal bool TryEquipItem(Entity item, Entity source, Entity character, InventoryEquipSlot slot)
    {
        EnsureOperational();
        if (!CanEquipItem(item, source, character, slot))
            return false;
        ContainerEntry sourceEntry = _containers[source];
        RemoveContainerRow(sourceEntry, _locations[item]);
        _equipped.Add((character, slot), item);
        _locations[item] = new InventoryLocation(character, slot);
        return true;
    }

    private static bool IsExactContainerRow(ContainerEntry container, int row, Entity item) =>
        unchecked((uint)row) < unchecked((uint)container.MutableItems.Count) &&
        container.MutableItems[row] == item;

    private void RemoveContainerRow(ContainerEntry container, InventoryLocation location)
    {
        int row = location.ContainerRow;
        int last = container.MutableItems.Count - 1;
        Entity donor = container.MutableItems[last];
        container.MutableItems[row] = donor;
        container.MutableItems.RemoveAt(last);
        if (row != last)
        {
            InventoryLocation donorLocation = _locations[donor];
            _locations[donor] = donorLocation with { ContainerRow = row };
        }
    }

    private void AddToContainer(ContainerEntry container, Entity item)
    {
        if (container.MutableItems.Count >= container.Capacity ||
            container.MutableItems.Count >= _limits.MaximumItemsPerContainer)
            throw new InvalidOperationException("An inventory container is at capacity.");
        container.MutableItems.Add(item);
    }

    private static void ValidateContainer(
        EcsWorld world,
        Entity entity,
        InventoryContainer container,
        in ComponentLookup<InventoryCharacter> characterLookup)
    {
        if (container.Capacity <= 0 || !container.Owner.IsValid ||
            !world.IsAlive(container.Owner) || !world.Has(container.Owner, characterLookup))
            throw new InvalidOperationException("An inventory container has invalid ownership or capacity.");
    }

    private static void ValidateItem(InventoryItem item)
    {
        if (item.Quantity <= 0 ||
            item.Kind is not (InventoryItemKind.Bread or InventoryItemKind.IronSword) ||
            item.EquipSlot is not (InventoryEquipSlot.None or InventoryEquipSlot.MainHand))
            throw new InvalidOperationException("An inventory item has invalid kind, quantity, or equipment state.");
        if (item.Kind == InventoryItemKind.IronSword && item.Quantity != 1)
            throw new InvalidOperationException("Iron swords are non-stackable.");
        if (item.EquipSlot != InventoryEquipSlot.None &&
            (item.Kind != InventoryItemKind.IronSword || item.Quantity != 1))
            throw new InvalidOperationException("Only one compatible non-stackable item may be equipped.");
    }

    private void EnsureOwner(EcsWorld world)
    {
        if (world.OwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Inventory index access must use the ECS owner thread.");
    }

    private void EnsureOperational()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InventoryIndex));
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Inventory index access must use its creating owner thread.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _containers.Clear();
        _locations.Clear();
        _equipped.Clear();
        _reservedContainerItemSlots = 0;
    }

    private sealed class ContainerEntry
    {
        internal ContainerEntry(int capacity)
        {
            Capacity = capacity;
            _mutableItems = new List<Entity>(capacity);
            Items = new ReadOnlyCollection<Entity>(_mutableItems);
        }

        private readonly List<Entity> _mutableItems;

        internal int Capacity { get; }
        internal List<Entity> MutableItems => _mutableItems;
        internal ReadOnlyCollection<Entity> Items { get; }
    }
}
