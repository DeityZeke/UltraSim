using System.Collections;
using System.Runtime.CompilerServices;
using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Bounded policy for one generation-owned inventory index.</summary>
public readonly record struct InventoryLimits(
    int MaximumContainers = 256,
    int MaximumItems = 4_096,
    int MaximumItemsPerContainer = 256)
{
    public static InventoryLimits Default => new(
        MaximumContainers: 256,
        MaximumItems: 4_096,
        MaximumItemsPerContainer: 256);

    public void Validate()
    {
        if (MaximumContainers <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumContainers));
        if (MaximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumItems));
        if (MaximumItemsPerContainer <= 0 || MaximumItemsPerContainer > MaximumItems)
            throw new ArgumentOutOfRangeException(nameof(MaximumItemsPerContainer));
    }
}

/// <summary>One indexed item relationship reconstructed from ECS truth.</summary>
public readonly record struct InventoryLocation(
    Entity Parent,
    InventoryEquipSlot EquipSlot,
    int ChildNode = -1);

/// <summary>Bounded index accounting with separately stated managed exclusions.</summary>
public readonly record struct InventoryIndexSnapshot(
    int ContainerCount,
    int ItemCount,
    int ContainedItemCount,
    int ChildNodeCapacity,
    long KnownChildStorageBytes,
    int MaximumContainers,
    int MaximumItems,
    int MaximumItemsPerContainer)
{
    public const string Exclusions =
        "CLR dictionary bucket/entry arrays, object headers, load-factor slack, and allocator alignment.";
}

/// <summary>
/// Allocation-free view over one container's linked rows in the global dense
/// child-node pool. Like a span, it is valid only until the index is mutated.
/// </summary>
public readonly struct InventoryItemCollection : IReadOnlyCollection<Entity>
{
    private readonly Entity[]? _entities;
    private readonly int[]? _next;
    private readonly int _head;

    internal InventoryItemCollection(Entity[] entities, int[] next, int head, int count)
    {
        _entities = entities;
        _next = next;
        _head = head;
        Count = count;
    }

    public int Count { get; }

    public Enumerator GetEnumerator() => new(_entities, _next, _head, Count);

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly Entity[] _entities;
        private readonly int[] _next;
        private int _node;
        private int _remaining;

        internal Enumerator(Entity[]? entities, int[]? next, int head, int count)
        {
            _entities = entities ?? Array.Empty<Entity>();
            _next = next ?? Array.Empty<int>();
            _node = head;
            _remaining = count;
            Current = Entity.Null;
        }

        public Entity Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                Current = Entity.Null;
                return false;
            }
            if (unchecked((uint)_node) >= unchecked((uint)_entities.Length))
                throw new InvalidOperationException("The inventory child-node chain is corrupt.");
            Current = _entities[_node];
            _node = _next[_node];
            _remaining--;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }
}

/// <summary>
/// Generation-owned derived index. Empty containers consume metadata only;
/// contained items occupy one row in a global bounded dense node pool.
/// </summary>
public sealed class InventoryIndex : IDisposable
{
    private Dictionary<Entity, ContainerEntry> _containers;
    private Dictionary<Entity, InventoryLocation> _locations;
    private Dictionary<(Entity Owner, InventoryEquipSlot Slot), Entity> _equipped;
    private Entity[] _childEntities;
    private int[] _childNext;
    private int[] _childPrevious;
    private readonly InventoryLimits _limits;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _childCount;
    private bool _disposed;

    public InventoryIndex(InventoryLimits limits)
    {
        limits.Validate();
        _limits = limits;
        _containers = new Dictionary<Entity, ContainerEntry>(limits.MaximumContainers);
        _locations = new Dictionary<Entity, InventoryLocation>(limits.MaximumItems);
        _equipped = new Dictionary<(Entity Owner, InventoryEquipSlot Slot), Entity>(limits.MaximumItems);
        _childEntities = new Entity[limits.MaximumItems];
        _childNext = new int[limits.MaximumItems];
        _childPrevious = new int[limits.MaximumItems];
        Array.Fill(_childNext, -1);
        Array.Fill(_childPrevious, -1);
    }

    public int ContainerCount { get { EnsureOperational(); return _containers.Count; } }
    public int ItemCount { get { EnsureOperational(); return _locations.Count; } }

    public bool ContainsContainer(Entity container)
    {
        EnsureOperational();
        return _containers.ContainsKey(container);
    }

    /// <summary>Returns children without scanning the ECS population.</summary>
    public InventoryItemCollection GetItems(Entity container)
    {
        EnsureOperational();
        return _containers.TryGetValue(container, out ContainerEntry entry)
            ? new InventoryItemCollection(_childEntities, _childNext, entry.Head, entry.Count)
            : default;
    }

    public bool TryGetLocation(Entity item, out InventoryLocation location)
    {
        EnsureOperational();
        return _locations.TryGetValue(item, out location);
    }

    public bool TryGetEquipped(Entity owner, InventoryEquipSlot slot, out Entity item)
    {
        EnsureOperational();
        return _equipped.TryGetValue((owner, slot), out item);
    }

    public InventoryIndexSnapshot CaptureSnapshot()
    {
        EnsureOperational();
        return new InventoryIndexSnapshot(
            _containers.Count,
            _locations.Count,
            _childCount,
            _childEntities.Length,
            checked((long)_childEntities.Length *
                (Unsafe.SizeOf<Entity>() + (sizeof(int) * 2))),
            _limits.MaximumContainers,
            _limits.MaximumItems,
            _limits.MaximumItemsPerContainer);
    }

    /// <summary>
    /// Rebuilds transactionally from ECS truth. The full entity scan is a cold
    /// restore/repair boundary; normal enumeration uses the index.
    /// </summary>
    public void Rebuild(
        EcsWorld world,
        in ComponentLookup<InventoryContainer> containerLookup,
        in ComponentLookup<InventoryContainerOwner> ownerLookup,
        in ComponentLookup<InventoryItem> itemLookup,
        in ComponentLookup<InventoryStackPolicy> stackLookup,
        in ComponentLookup<InventoryEquipCapability> equipLookup,
        IInventoryContentCatalog content)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        EnsureOwner(world);

        var replacement = new InventoryIndex(_limits);
        int maximumEntities = world.Options.EntityCapacity.MaximumEntities;
        var items = new List<(Entity Entity, InventoryItem Value)>(_limits.MaximumItems);
        try
        {
            for (uint index = 0; index < unchecked((uint)maximumEntities); index++)
            {
                if (!world.TryGetEntity(index, out Entity entity))
                    continue;
                bool hasContainer = world.TryRead(entity, containerLookup, out InventoryContainer container);
                bool hasOwner = world.TryRead(entity, ownerLookup, out InventoryContainerOwner owner);
                bool hasItem = world.TryRead(entity, itemLookup, out InventoryItem item);
                bool hasStackPolicy = world.TryRead(entity, stackLookup, out _);
                bool hasEquipCapability = world.TryRead(entity, equipLookup, out _);
                if (hasOwner && !hasContainer)
                    throw new InvalidOperationException("An inventory owner relationship exists without a container.");
                if ((hasStackPolicy || hasEquipCapability) && !hasItem)
                    throw new InvalidOperationException("An inventory item policy exists without an item.");
                if (hasContainer)
                {
                    ValidateContainer(container);
                    if (!world.TryGetPrefabProvenance(entity, out PrefabKey containerKey) ||
                        !content.TryGetContainer(containerKey, out InventoryContainerDefinition definition) ||
                        definition.HasOwner != hasOwner)
                    {
                        throw new InvalidOperationException(
                            "An inventory container does not match registered content ownership.");
                    }
                    if (!replacement.TryAddContainer(entity, container.Capacity))
                        throw new InvalidOperationException("The inventory container index limit was exceeded.");
                    if (hasOwner && (!owner.Owner.IsValid || !world.IsAlive(owner.Owner)))
                    {
                        throw new InvalidOperationException("An inventory container has a stale owner relationship.");
                    }
                }
                if (hasItem)
                {
                    if (items.Count >= _limits.MaximumItems)
                        throw new InvalidOperationException("The inventory item index limit was exceeded.");
                    ValidateItem(world, entity, item, stackLookup, equipLookup, content);
                    items.Add((entity, item));
                }
            }

            foreach ((Entity entity, InventoryItem item) in items)
            {
                if (!item.Parent.IsValid || !world.IsAlive(item.Parent))
                    throw new InvalidOperationException("An inventory item references a stale parent.");
                if (item.EquipSlot == InventoryEquipSlot.None)
                {
                    if (!replacement.TryAddItem(entity, item))
                        throw new InvalidOperationException("A contained item exceeds an inventory index limit.");
                }
                else
                {
                    if (!replacement.TryAddItem(entity, item))
                        throw new InvalidOperationException("Two inventory items occupy the same equipment slot.");
                }
            }

            TakeStorageFrom(replacement);
        }
        finally
        {
            replacement.Dispose();
        }
    }

    internal bool CanAddContainer(int capacity) =>
        _containers.Count < _limits.MaximumContainers &&
        capacity > 0 && capacity <= _limits.MaximumItemsPerContainer;

    internal bool TryAddContainer(Entity container, int capacity)
    {
        EnsureOperational();
        if (_containers.ContainsKey(container) || !CanAddContainer(capacity))
            return false;
        _containers.Add(container, new ContainerEntry(capacity));
        return true;
    }

    internal bool CanRemoveContainer(Entity container) =>
        _containers.TryGetValue(container, out ContainerEntry entry) && entry.Count == 0;

    internal bool TryRemoveContainer(Entity container)
    {
        EnsureOperational();
        return CanRemoveContainer(container) && _containers.Remove(container);
    }

    internal bool CanAddContainedItem(Entity container) =>
        _locations.Count < _limits.MaximumItems &&
        _childCount < _childEntities.Length &&
        _containers.TryGetValue(container, out ContainerEntry entry) &&
        entry.Count < entry.Capacity;

    internal bool CanAddItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (_locations.ContainsKey(item) || _locations.Count >= _limits.MaximumItems)
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
            return CanAddContainedItem(value.Parent);
        return value.EquipSlot == InventoryEquipSlot.MainHand &&
            !_equipped.ContainsKey((value.Parent, value.EquipSlot));
    }

    internal bool TryAddItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!CanAddItem(item, value))
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
            AddChild(item, value.Parent);
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
            ? IsExactChild(location.ChildNode, item, value.Parent)
            : _equipped.TryGetValue((value.Parent, value.EquipSlot), out Entity equipped) && equipped == item;
    }

    internal bool TryRemoveItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!CanRemoveItem(item, value))
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
            RemoveChild(_locations[item]);
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
            !IsExactChild(location.ChildNode, item, source) ||
            !_containers.TryGetValue(destination, out ContainerEntry destinationEntry))
            return false;
        return source == destination || destinationEntry.Count < destinationEntry.Capacity;
    }

    internal bool TryMoveItem(Entity item, Entity source, Entity destination)
    {
        EnsureOperational();
        if (!CanMoveItem(item, source, destination))
            return false;
        if (source == destination)
            return true;
        RemoveChild(_locations[item]);
        _locations.Remove(item);
        AddChild(item, destination);
        return true;
    }

    internal bool CanEquipItem(Entity item, Entity source, Entity owner, InventoryEquipSlot slot)
    {
        EnsureOperational();
        return slot == InventoryEquipSlot.MainHand &&
            _locations.TryGetValue(item, out InventoryLocation location) &&
            location.Parent == source && location.EquipSlot == InventoryEquipSlot.None &&
            IsExactChild(location.ChildNode, item, source) &&
            !_equipped.ContainsKey((owner, slot));
    }

    internal bool TryEquipItem(Entity item, Entity source, Entity owner, InventoryEquipSlot slot)
    {
        EnsureOperational();
        if (!CanEquipItem(item, source, owner, slot))
            return false;
        RemoveChild(_locations[item]);
        _equipped.Add((owner, slot), item);
        _locations[item] = new InventoryLocation(owner, slot);
        return true;
    }

    private void AddChild(Entity item, Entity container)
    {
        ContainerEntry entry = _containers[container];
        int node = _childCount++;
        _childEntities[node] = item;
        _childPrevious[node] = entry.Tail;
        _childNext[node] = -1;
        if (entry.Tail >= 0)
            _childNext[entry.Tail] = node;
        else
            entry.Head = node;
        entry.Tail = node;
        entry.Count++;
        _containers[container] = entry;
        _locations.Add(item, new InventoryLocation(container, InventoryEquipSlot.None, node));
    }

    private void RemoveChild(InventoryLocation location)
    {
        int node = location.ChildNode;
        ContainerEntry removedFrom = _containers[location.Parent];
        int previous = _childPrevious[node];
        int next = _childNext[node];
        if (previous >= 0)
            _childNext[previous] = next;
        else
            removedFrom.Head = next;
        if (next >= 0)
            _childPrevious[next] = previous;
        else
            removedFrom.Tail = previous;
        removedFrom.Count--;
        _containers[location.Parent] = removedFrom;

        int last = --_childCount;
        if (node != last)
        {
            Entity donor = _childEntities[last];
            int donorPrevious = _childPrevious[last];
            int donorNext = _childNext[last];
            _childEntities[node] = donor;
            _childPrevious[node] = donorPrevious;
            _childNext[node] = donorNext;
            if (donorPrevious >= 0)
                _childNext[donorPrevious] = node;
            if (donorNext >= 0)
                _childPrevious[donorNext] = node;

            InventoryLocation donorLocation = _locations[donor];
            ContainerEntry donorContainer = _containers[donorLocation.Parent];
            if (donorContainer.Head == last)
                donorContainer.Head = node;
            if (donorContainer.Tail == last)
                donorContainer.Tail = node;
            _containers[donorLocation.Parent] = donorContainer;
            _locations[donor] = donorLocation with { ChildNode = node };
        }
        _childEntities[last] = Entity.Null;
        _childPrevious[last] = -1;
        _childNext[last] = -1;
    }

    private bool IsExactChild(int node, Entity item, Entity parent) =>
        unchecked((uint)node) < unchecked((uint)_childCount) &&
        _childEntities[node] == item &&
        _locations.TryGetValue(item, out InventoryLocation location) &&
        location.Parent == parent && location.ChildNode == node;

    private void TakeStorageFrom(InventoryIndex replacement)
    {
        _containers = replacement._containers;
        _locations = replacement._locations;
        _equipped = replacement._equipped;
        _childEntities = replacement._childEntities;
        _childNext = replacement._childNext;
        _childPrevious = replacement._childPrevious;
        _childCount = replacement._childCount;
        replacement._containers = null!;
        replacement._locations = null!;
        replacement._equipped = null!;
        replacement._childEntities = null!;
        replacement._childNext = null!;
        replacement._childPrevious = null!;
        replacement._childCount = 0;
    }

    private static void ValidateContainer(in InventoryContainer container)
    {
        if (container.Capacity <= 0)
            throw new InvalidOperationException("An inventory container has invalid capacity.");
    }

    private static void ValidateItem(
        EcsWorld world,
        Entity entity,
        in InventoryItem item,
        in ComponentLookup<InventoryStackPolicy> stackLookup,
        in ComponentLookup<InventoryEquipCapability> equipLookup,
        IInventoryContentCatalog content)
    {
        if (!world.TryGetPrefabProvenance(entity, out PrefabKey key) ||
            !content.TryGetItem(key, out InventoryItemDefinition definition))
        {
            throw new InvalidOperationException("An inventory item has unknown content provenance.");
        }
        int maximumQuantity = 1;
        if (world.TryRead(entity, stackLookup, out InventoryStackPolicy stack))
            maximumQuantity = stack.MaximumQuantity;
        InventoryEquipSlots allowed = InventoryEquipSlots.None;
        if (world.TryRead(entity, equipLookup, out InventoryEquipCapability equip))
            allowed = equip.AllowedSlots;
        if (maximumQuantity != definition.MaximumQuantity || allowed != definition.AllowedEquipSlots ||
            item.Quantity <= 0 || item.Quantity > maximumQuantity ||
            item.EquipSlot is not (InventoryEquipSlot.None or InventoryEquipSlot.MainHand) ||
            (item.EquipSlot != InventoryEquipSlot.None &&
             (allowed & item.EquipSlot.ToCapability()) == 0))
        {
            throw new InvalidOperationException("An inventory item disagrees with its compiled content policy.");
        }
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
        _containers?.Clear();
        _locations?.Clear();
        _equipped?.Clear();
        _childCount = 0;
    }

    private struct ContainerEntry
    {
        internal ContainerEntry(int capacity)
        {
            Capacity = capacity;
            Head = -1;
            Tail = -1;
            Count = 0;
        }

        internal int Capacity;
        internal int Head;
        internal int Tail;
        internal int Count;
    }
}
