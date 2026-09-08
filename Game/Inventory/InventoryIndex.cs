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
public readonly record struct InventoryLocation
{
    public InventoryLocation(Entity parent, InventoryEquipSlot equipSlot)
        : this(parent, equipSlot, -1) { }

    internal InventoryLocation(Entity parent, InventoryEquipSlot equipSlot, int childNode)
    {
        Parent = parent;
        EquipSlot = equipSlot;
        ChildNode = childNode;
    }

    public Entity Parent { get; init; }
    public InventoryEquipSlot EquipSlot { get; init; }
    internal int ChildNode { get; init; }
}

/// <summary>Bounded index accounting with separately stated managed exclusions.</summary>
public readonly record struct InventoryIndexSnapshot(
    int ContainerCount,
    int ItemCount,
    int ContainedItemCount,
    int GridPlacedItemCount,
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
    private Dictionary<Entity, InventoryPlacement>? _gridPlacements;
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

    public bool TryGetPlacement(Entity item, out InventoryPlacement placement)
    {
        EnsureOperational();
        if (_gridPlacements?.TryGetValue(item, out placement) == true)
            return true;
        if (_locations.TryGetValue(item, out _))
        {
            placement = InventoryPlacement.Unplaced;
            return true;
        }
        placement = default;
        return false;
    }

    public InventoryIndexSnapshot CaptureSnapshot()
    {
        EnsureOperational();
        return new InventoryIndexSnapshot(
            _containers.Count,
            _locations.Count,
            _childCount,
            _gridPlacements?.Count ?? 0,
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
        in ComponentLookup<InventoryGridPlacement> gridPlacementLookup,
        in ComponentLookup<InventoryGridFootprint> gridFootprintLookup,
        IInventoryContentCatalog content)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        EnsureOwner(world);

        var replacement = new InventoryIndex(_limits);
        int maximumEntities = world.Options.EntityCapacity.MaximumEntities;
        var items = new List<(Entity Entity, InventoryItem Value, InventoryPlacement Placement,
            InventoryItemDefinition Definition)>(_limits.MaximumItems);
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
                bool hasGridPlacement = world.TryRead(
                    entity, gridPlacementLookup, out InventoryGridPlacement gridPlacement);
                bool hasGridFootprint = world.TryRead(entity, gridFootprintLookup, out _);
                if (hasOwner && !hasContainer)
                    throw new InvalidOperationException("An inventory owner relationship exists without a container.");
                if ((hasStackPolicy || hasEquipCapability || hasGridPlacement || hasGridFootprint) && !hasItem)
                    throw new InvalidOperationException("An inventory item policy exists without an item.");
                if (hasContainer)
                {
                    if (!world.TryGetPrefabProvenance(entity, out PrefabKey containerKey) ||
                        !content.TryGetContainer(containerKey, out InventoryContainerDefinition definition) ||
                        definition.HasOwner != hasOwner)
                    {
                        throw new InvalidOperationException(
                            "An inventory container does not match registered content ownership.");
                    }
                    InventoryEffectiveRules rules = ValidateContainer(container, definition, content.InventoryRules);
                    if (!replacement.TryAddContainer(entity, rules))
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
                    InventoryItemDefinition definition = ValidateItem(
                        world, entity, item, stackLookup, equipLookup, gridFootprintLookup, content);
                    if (item.EquipSlot != InventoryEquipSlot.None && hasGridPlacement)
                        throw new InvalidOperationException("An equipped item retains Grid placement state.");
                    InventoryPlacement placement = hasGridPlacement
                        ? InventoryPlacement.Grid(
                            gridPlacement.X,
                            gridPlacement.Y,
                            definition.Footprint.Width,
                            definition.Footprint.Height)
                        : InventoryPlacement.Unplaced;
                    items.Add((entity, item, placement, definition));
                }
            }

            foreach ((Entity entity, InventoryItem item, InventoryPlacement placement,
                InventoryItemDefinition definition) in items)
            {
                if (!item.Parent.IsValid)
                {
                    if (item.EquipSlot != InventoryEquipSlot.None ||
                        placement.Mode != InventoryPlacementMode.Unplaced)
                        throw new InvalidOperationException(
                            "A world item retains equipment or Grid placement state.");
                    continue;
                }
                if (!world.IsAlive(item.Parent))
                    throw new InvalidOperationException("An inventory item references a stale parent.");
                if (item.EquipSlot == InventoryEquipSlot.None)
                {
                    if (!replacement.TryAddItem(entity, item, placement, definition.Footprint))
                        throw new InvalidOperationException("A contained item exceeds an inventory index limit.");
                }
                else
                {
                    if (!replacement.TryAddItem(
                            entity, item, InventoryPlacement.Unplaced, definition.Footprint))
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

    internal bool CanAddContainer(in InventoryEffectiveRules rules) =>
        _containers.Count < _limits.MaximumContainers &&
        rules.IsOperational && rules.ItemCapacity > 0 &&
        rules.ItemCapacity <= _limits.MaximumItemsPerContainer;

    internal bool TryAddContainer(Entity container, in InventoryEffectiveRules rules)
    {
        EnsureOperational();
        if (_containers.ContainsKey(container) || !CanAddContainer(rules))
            return false;
        _containers.Add(container, new ContainerEntry(rules));
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
        entry.Rules.PlacementMode == InventoryPlacementMode.Unplaced &&
        entry.Count < entry.Rules.ItemCapacity;

    internal bool TryGetContainerRules(Entity container, out InventoryEffectiveRules rules)
    {
        EnsureOperational();
        if (_containers.TryGetValue(container, out ContainerEntry entry))
        {
            rules = entry.Rules;
            return true;
        }
        rules = default;
        return false;
    }

    internal bool CanSetContainerRules(
        Entity container,
        in InventoryEffectiveRules rules)
    {
        EnsureOperational();
        if (!_containers.TryGetValue(container, out ContainerEntry entry) ||
            !CanAddContainerRules(rules) ||
            entry.Rules.Profile != rules.Profile ||
            entry.Rules.PlacementMode != rules.PlacementMode ||
            entry.Rules.MaximumWeightGrams != rules.MaximumWeightGrams ||
            entry.Rules.MaximumNestingDepth != rules.MaximumNestingDepth ||
            entry.Count > rules.ItemCapacity)
        {
            return false;
        }
        if (rules.PlacementMode != InventoryPlacementMode.Grid)
            return true;
        foreach (Entity item in GetItems(container))
        {
            if (_gridPlacements?.TryGetValue(item, out InventoryPlacement placement) != true ||
                (long)placement.X + placement.Width > rules.GridWidth ||
                (long)placement.Y + placement.Height > rules.GridHeight)
            {
                return false;
            }
        }
        return true;
    }

    internal bool TrySetContainerRules(
        Entity container,
        in InventoryEffectiveRules rules)
    {
        EnsureOperational();
        if (!CanSetContainerRules(container, rules))
            return false;
        ContainerEntry entry = _containers[container];
        entry.Rules = rules;
        _containers[container] = entry;
        return true;
    }

    internal bool HasContainedItemCapacity(Entity container) =>
        _containers.TryGetValue(container, out ContainerEntry entry) &&
        entry.Count < entry.Rules.ItemCapacity &&
        _childCount < _childEntities.Length;

    internal bool CanPlaceContainedItem(
        Entity item,
        Entity container,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint,
        bool itemAlreadyIndexed)
    {
        EnsureOperational();
        if (!placement.IsValid || !footprint.IsValid ||
            !_containers.TryGetValue(container, out ContainerEntry entry) ||
            (!itemAlreadyIndexed && (_locations.Count >= _limits.MaximumItems ||
                _childCount >= _childEntities.Length)) ||
            (!itemAlreadyIndexed && entry.Count >= entry.Rules.ItemCapacity))
        {
            return false;
        }
        if (entry.Rules.PlacementMode == InventoryPlacementMode.Unplaced)
            return placement.Mode == InventoryPlacementMode.Unplaced;
        if (entry.Rules.PlacementMode != InventoryPlacementMode.Grid ||
            placement.Mode != InventoryPlacementMode.Grid || placement.Rotated ||
            placement.Width != footprint.Width || placement.Height != footprint.Height ||
            (long)placement.X + footprint.Width > entry.Rules.GridWidth ||
            (long)placement.Y + footprint.Height > entry.Rules.GridHeight)
        {
            return false;
        }
        foreach (Entity other in GetItems(container))
        {
            if (other == item)
                continue;
            if (_gridPlacements?.TryGetValue(other, out InventoryPlacement occupied) != true ||
                RectanglesOverlap(placement, occupied))
            {
                return false;
            }
        }
        return true;
    }

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

    internal bool TryAddItem(
        Entity item,
        in InventoryItem value,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint)
    {
        EnsureOperational();
        if (_locations.ContainsKey(item) || _locations.Count >= _limits.MaximumItems)
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
        {
            if (!CanPlaceContainedItem(item, value.Parent, placement, footprint, itemAlreadyIndexed: false))
                return false;
            AddChild(item, value.Parent);
            if (placement.Mode == InventoryPlacementMode.Grid)
                AddGridPlacement(item, placement);
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
            ? IsExactChild(location.ChildNode, item, value.Parent)
            : _equipped.TryGetValue((value.Parent, value.EquipSlot), out Entity equipped) && equipped == item;
    }

    internal bool TryRemoveItem(Entity item, in InventoryItem value)
    {
        EnsureOperational();
        if (!CanRemoveItem(item, value))
            return false;
        if (value.EquipSlot == InventoryEquipSlot.None)
        {
            _gridPlacements?.Remove(item);
            RemoveChild(_locations[item]);
        }
        else
            _equipped.Remove((value.Parent, value.EquipSlot));
        _locations.Remove(item);
        return true;
    }

    internal bool CanMoveItem(
        Entity item,
        Entity source,
        Entity destination,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint)
    {
        EnsureOperational();
        if (!_locations.TryGetValue(item, out InventoryLocation location) ||
            location.Parent != source || location.EquipSlot != InventoryEquipSlot.None ||
            !IsExactChild(location.ChildNode, item, source) ||
            !_containers.TryGetValue(destination, out ContainerEntry destinationEntry))
            return false;
        return CanPlaceContainedItem(
            item, destination, placement, footprint, itemAlreadyIndexed: true) &&
            (source == destination || destinationEntry.Count < destinationEntry.Rules.ItemCapacity);
    }

    internal bool CanMoveItem(Entity item, Entity source, Entity destination)
    {
        EnsureOperational();
        if (!_locations.TryGetValue(item, out InventoryLocation location) ||
            location.Parent != source || location.EquipSlot != InventoryEquipSlot.None ||
            !IsExactChild(location.ChildNode, item, source) ||
            !_containers.TryGetValue(source, out ContainerEntry sourceEntry) ||
            sourceEntry.Rules.PlacementMode != InventoryPlacementMode.Unplaced ||
            !_containers.TryGetValue(destination, out ContainerEntry destinationEntry) ||
            destinationEntry.Rules.PlacementMode != InventoryPlacementMode.Unplaced)
        {
            return false;
        }
        return source == destination || destinationEntry.Count < destinationEntry.Rules.ItemCapacity;
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

    internal bool TryMoveItem(
        Entity item,
        Entity source,
        Entity destination,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint)
    {
        EnsureOperational();
        if (!CanMoveItem(item, source, destination, placement, footprint))
            return false;
        if (source == destination)
        {
            if (placement.Mode == InventoryPlacementMode.Grid)
                SetGridPlacement(item, placement);
            return true;
        }
        _gridPlacements?.Remove(item);
        RemoveChild(_locations[item]);
        _locations.Remove(item);
        AddChild(item, destination);
        if (placement.Mode == InventoryPlacementMode.Grid)
            AddGridPlacement(item, placement);
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
        _gridPlacements?.Remove(item);
        _equipped.Add((owner, slot), item);
        _locations[item] = new InventoryLocation(owner, slot);
        return true;
    }

    internal bool CanUnequipItem(
        Entity item,
        Entity owner,
        InventoryEquipSlot slot,
        Entity destination,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint)
    {
        EnsureOperational();
        return slot == InventoryEquipSlot.MainHand &&
            _locations.TryGetValue(item, out InventoryLocation location) &&
            location.Parent == owner && location.EquipSlot == slot &&
            _equipped.TryGetValue((owner, slot), out Entity equipped) && equipped == item &&
            _childCount < _childEntities.Length &&
            _containers.TryGetValue(destination, out ContainerEntry destinationEntry) &&
            destinationEntry.Count < destinationEntry.Rules.ItemCapacity &&
            CanPlaceContainedItem(
                item, destination, placement, footprint, itemAlreadyIndexed: true);
    }

    internal bool TryUnequipItem(
        Entity item,
        Entity owner,
        InventoryEquipSlot slot,
        Entity destination,
        in InventoryPlacement placement,
        in InventoryItemFootprint footprint)
    {
        EnsureOperational();
        if (!CanUnequipItem(item, owner, slot, destination, placement, footprint))
            return false;
        _equipped.Remove((owner, slot));
        _locations.Remove(item);
        AddChild(item, destination);
        if (placement.Mode == InventoryPlacementMode.Grid)
            AddGridPlacement(item, placement);
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
        _gridPlacements = replacement._gridPlacements;
        _childEntities = replacement._childEntities;
        _childNext = replacement._childNext;
        _childPrevious = replacement._childPrevious;
        _childCount = replacement._childCount;
        replacement._containers = null!;
        replacement._locations = null!;
        replacement._equipped = null!;
        replacement._gridPlacements = null;
        replacement._childEntities = null!;
        replacement._childNext = null!;
        replacement._childPrevious = null!;
        replacement._childCount = 0;
    }

    private static InventoryEffectiveRules ValidateContainer(
        in InventoryContainer container,
        in InventoryContainerDefinition definition,
        InventoryRuleCatalog catalog)
    {
        var capacity = new InventoryInstanceCapacity(
            container.Capacity,
            container.GridWidth,
            container.GridHeight);
        if (!catalog.TryResolve(definition.RuleProfile, capacity, out InventoryEffectiveRules rules))
            throw new InvalidOperationException("An inventory container has invalid effective capacity or rule profile.");
        if (!rules.IsOperational)
        {
            throw new InvalidOperationException(
                "An inventory container selects unsupported placement, weight, or nesting semantics.");
        }
        return rules;
    }

    private bool CanAddContainerRules(in InventoryEffectiveRules rules) =>
        rules.IsOperational && rules.ItemCapacity > 0 &&
        rules.ItemCapacity <= _limits.MaximumItemsPerContainer;

    private static InventoryItemDefinition ValidateItem(
        EcsWorld world,
        Entity entity,
        in InventoryItem item,
        in ComponentLookup<InventoryStackPolicy> stackLookup,
        in ComponentLookup<InventoryEquipCapability> equipLookup,
        in ComponentLookup<InventoryGridFootprint> gridFootprintLookup,
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
        InventoryItemFootprint footprint = world.TryRead(
            entity, gridFootprintLookup, out InventoryGridFootprint gridFootprint)
            ? new InventoryItemFootprint(gridFootprint.Width, gridFootprint.Height)
            : InventoryItemFootprint.OneCell;
        if (maximumQuantity != definition.MaximumQuantity || allowed != definition.AllowedEquipSlots ||
            footprint != definition.Footprint ||
            item.Quantity <= 0 || item.Quantity > maximumQuantity ||
            item.EquipSlot is not (InventoryEquipSlot.None or InventoryEquipSlot.MainHand) ||
            (item.EquipSlot != InventoryEquipSlot.None &&
             (allowed & item.EquipSlot.ToCapability()) == 0))
        {
            throw new InvalidOperationException("An inventory item disagrees with its compiled content policy.");
        }
        return definition;
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
        _gridPlacements?.Clear();
        _childCount = 0;
    }

    private struct ContainerEntry
    {
        internal ContainerEntry(in InventoryEffectiveRules rules)
        {
            Rules = rules;
            Head = -1;
            Tail = -1;
            Count = 0;
        }

        internal InventoryEffectiveRules Rules;
        internal int Head;
        internal int Tail;
        internal int Count;
    }

    private void AddGridPlacement(Entity item, in InventoryPlacement placement)
    {
        _gridPlacements ??= [];
        _gridPlacements.Add(item, placement);
    }

    private void SetGridPlacement(Entity item, in InventoryPlacement placement)
    {
        if (_gridPlacements is null || !_gridPlacements.ContainsKey(item))
            throw new InvalidOperationException("The Grid placement index is missing an existing item.");
        _gridPlacements[item] = placement;
    }

    private static bool RectanglesOverlap(
        in InventoryPlacement left,
        in InventoryPlacement right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width &&
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
}
