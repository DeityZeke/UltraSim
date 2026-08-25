using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Expected authoritative outcome of one inventory mutation attempt.</summary>
public enum InventoryMutationStatus : byte
{
    Accepted = 0,
    RuntimeWorldMismatch = 1,
    MissingItem = 2,
    MissingContainer = 3,
    MissingEquipOwner = 4,
    WrongSource = 5,
    InvalidQuantity = 6,
    CapacityExceeded = 7,
    InvalidEquipmentSlot = 8,
    SlotOccupied = 9,
    ItemAlreadyEquipped = 10,
    InvariantViolation = 11,
    ContainerNotEmpty = 12,
    ItemPolicyViolation = 13,
    UnauthorizedContainer = 14,
}

/// <summary>Authoritative mutation result plus affected runtime entities for immediate projection.</summary>
public readonly record struct InventoryMutationResult(
    InventoryMutationStatus Status,
    Entity Item,
    Entity CreatedItem,
    string Message)
{
    public bool Accepted => Status == InventoryMutationStatus.Accepted;
}

/// <summary>Generation-local inventory projection row paired with durable identity and content provenance.</summary>
public readonly record struct InventoryItemView(
    Entity Entity,
    PersistentEntityId PersistentId,
    PrefabKey Prefab,
    string DisplayName,
    InventoryEquipSlot EquipSlot,
    int Quantity);

/// <summary>Generation-owned capability exposing the derived Inventory state paired with its ECS world.</summary>
public interface IInventoryGenerationStateProvider
{
    InventoryGenerationState Inventory { get; }
}

/// <summary>
/// Generation-owned inventory rules and derived index. Content supplies item
/// identities/policies; ECS remains authoritative for mutable relationships.
/// </summary>
public sealed class InventoryGenerationState : IInventoryGenerationStateProvider, IDisposable
{
    private readonly WorldId _worldId;
    private readonly IInventoryContentCatalog _content;
    private readonly ComponentLookup<InventoryContainer> _containerLookup;
    private readonly ComponentLookup<InventoryContainerOwner> _ownerLookup;
    private readonly ComponentLookup<InventoryItem> _itemLookup;
    private readonly ComponentLookup<InventoryStackPolicy> _stackLookup;
    private readonly ComponentLookup<InventoryEquipCapability> _equipLookup;
    private bool _disposed;

    public InventoryGenerationState(
        EcsWorld world,
        IInventoryContentCatalog content,
        InventoryLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _content = content ?? throw new ArgumentNullException(nameof(content));
        if (content.Prefabs.WorldId != world.Id)
            throw new ArgumentException("Inventory content belongs to another world.", nameof(content));
        _worldId = world.Id;
        _containerLookup = world.PrepareLookup<InventoryContainer>();
        _ownerLookup = world.PrepareLookup<InventoryContainerOwner>();
        _itemLookup = world.PrepareLookup<InventoryItem>();
        _stackLookup = world.PrepareLookup<InventoryStackPolicy>();
        _equipLookup = world.PrepareLookup<InventoryEquipCapability>();
        Limits = limits ?? InventoryLimits.Default;
        Limits.Validate();
        Index = new InventoryIndex(Limits);
    }

    public InventoryLimits Limits { get; }
    public InventoryIndex Index { get; }
    public WorldId WorldId => _worldId;
    InventoryGenerationState IInventoryGenerationStateProvider.Inventory => this;

    public void Rebuild(EcsWorld world)
    {
        EnsureWorld(world);
        Index.Rebuild(
            world,
            _containerLookup,
            _ownerLookup,
            _itemLookup,
            _stackLookup,
            _equipLookup,
            _content);
    }

    public bool TryCreateContainer(
        EcsWorld world,
        PrefabHandle prefab,
        out Entity entity)
    {
        EnsureWorld(world);
        if (!_content.TryGetContainer(prefab, out InventoryContainerDefinition definition))
        {
            entity = Entity.Null;
            return false;
        }
        return TryCreateContainerCore(
            world, prefab, Entity.Null, hasOwner: false, definition.DefaultCapacity, out entity);
    }

    public bool TryCreateContainer(
        EcsWorld world,
        PrefabHandle prefab,
        int capacity,
        out Entity entity) =>
        TryCreateContainerCore(world, prefab, Entity.Null, hasOwner: false, capacity, out entity);

    public bool TryCreateOwnedContainer(
        EcsWorld world,
        PrefabHandle prefab,
        Entity owner,
        out Entity entity)
    {
        EnsureWorld(world);
        if (!_content.TryGetContainer(prefab, out InventoryContainerDefinition definition))
        {
            entity = Entity.Null;
            return false;
        }
        return TryCreateContainerCore(
            world, prefab, owner, hasOwner: true, definition.DefaultCapacity, out entity);
    }

    public bool TryCreateOwnedContainer(
        EcsWorld world,
        PrefabHandle prefab,
        Entity owner,
        int capacity,
        out Entity entity) =>
        TryCreateContainerCore(world, prefab, owner, hasOwner: true, capacity, out entity);

    public bool TryCreateItem(
        EcsWorld world,
        Entity parent,
        in InventoryItemDefinition definition,
        int quantity,
        out Entity entity)
    {
        EnsureWorld(world);
        entity = Entity.Null;
        if (!IsBoundDefinition(definition) || quantity <= 0 || quantity > definition.MaximumQuantity ||
            !world.IsAlive(parent) || !world.Has(parent, _containerLookup) ||
            !Index.CanAddContainedItem(parent))
        {
            return false;
        }
        if (!world.TryInstantiate(definition.Handle, out entity))
            return false;
        ValidateInstantiatedItemOrThrow(world, entity, definition);
        var value = new InventoryItem
        {
            Parent = parent,
            EquipSlot = InventoryEquipSlot.None,
            Quantity = quantity,
        };
        if (!world.TryWrite(entity, _itemLookup, value))
            DestroyFailedCreationOrThrow(world, entity, "item initialization");
        if (!Index.TryAddItem(entity, value))
            throw new InvalidOperationException("A prevalidated inventory item was rejected by the index.");
        return true;
    }

    public InventoryMutationResult Move(EcsWorld world, Entity item, Entity source, Entity destination)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!Index.ContainsContainer(source) || !Index.ContainsContainer(destination))
            return Reject(InventoryMutationStatus.MissingContainer, item, "The source or destination container is missing.");
        if (!Index.TryGetLocation(item, out InventoryLocation location))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is not indexed.");
        if (location.Parent != source)
            return Reject(InventoryMutationStatus.WrongSource, item, "The item is not in the requested source container.");
        if (location.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.ItemAlreadyEquipped, item, "An equipped item cannot be moved as a container item.");
        if (!world.TryRead(item, _itemLookup, out InventoryItem value))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item component is missing.");
        if (value.Parent != source || value.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.InvariantViolation, item, "ECS and inventory index topology disagree.");
        if (source == destination)
            return Accept(item, Entity.Null, "The item is already in the destination container.");
        if (!Index.CanMoveItem(item, source, destination))
            return Reject(InventoryMutationStatus.CapacityExceeded, item, "The destination container is full.");

        InventoryItem moved = value;
        moved.Parent = destination;
        if (!world.TryWrite(item, _itemLookup, moved))
            throw new InvalidOperationException("A validated inventory move rejected its ECS update.");
        if (!Index.TryMoveItem(item, source, destination))
            throw new InvalidOperationException("A prevalidated inventory move was rejected by the index.");
        return Accept(item, Entity.Null, "Move accepted.");
    }

    public InventoryMutationResult Split(EcsWorld world, Entity item, Entity source, Entity destination, int quantity)
    {
        EnsureWorld(world);
        if (quantity <= 0)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Split quantity must be positive.");
        if (!world.IsAlive(item) || !world.TryRead(item, _itemLookup, out InventoryItem original))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!Index.ContainsContainer(source) || !Index.ContainsContainer(destination))
            return Reject(InventoryMutationStatus.MissingContainer, item, "The source or destination container is missing.");
        if (!Index.TryGetLocation(item, out InventoryLocation location) || location.Parent != source)
            return Reject(InventoryMutationStatus.WrongSource, item, "The item is not in the requested source container.");
        if (location.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.ItemAlreadyEquipped, item, "An equipped item cannot be split.");
        if (original.Parent != source || original.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.InvariantViolation, item, "ECS and inventory index topology disagree.");
        if (!TryGetDefinition(world, item, out InventoryItemDefinition definition) || !definition.IsStackable)
            return Reject(InventoryMutationStatus.ItemPolicyViolation, item, "The item content is not stackable.");
        if (quantity >= original.Quantity)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Split quantity must leave a positive source stack.");
        if (!Index.CanAddContainedItem(destination))
            return Reject(InventoryMutationStatus.CapacityExceeded, item, "The destination container is full.");
        if (!world.TryInstantiate(definition.Handle, out Entity created))
            return Reject(InventoryMutationStatus.CapacityExceeded, item, "The inventory entity capacity was exhausted.");
        ValidateInstantiatedItemOrThrow(world, created, definition);

        InventoryItem sourceAfter = original;
        sourceAfter.Quantity -= quantity;
        InventoryItem split = original;
        split.Parent = destination;
        split.Quantity = quantity;
        if (!world.TryWrite(created, _itemLookup, split))
            DestroyFailedCreationOrThrow(world, created, "split-item initialization");
        if (!world.TryWrite(item, _itemLookup, sourceAfter))
            throw new InvalidOperationException("A validated split source rejected its quantity update.");
        if (!Index.TryAddItem(created, split))
            throw new InvalidOperationException("A prevalidated inventory split was rejected by the index.");
        return Accept(item, created, "Split accepted.");
    }

    public InventoryMutationResult Equip(
        EcsWorld world,
        Entity item,
        Entity source,
        Entity owner,
        InventoryEquipSlot slot)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!world.IsAlive(owner))
            return Reject(InventoryMutationStatus.MissingEquipOwner, item, "The equipment owner is missing.");
        InventoryEquipSlots requested = slot.ToCapability();
        if (requested == InventoryEquipSlots.None)
            return Reject(InventoryMutationStatus.InvalidEquipmentSlot, item, "The equipment slot is invalid.");
        if (Index.TryGetEquipped(owner, slot, out _))
            return Reject(InventoryMutationStatus.SlotOccupied, item, "The equipment slot is occupied.");
        if (!Index.TryGetLocation(item, out InventoryLocation location) || location.Parent != source)
            return Reject(InventoryMutationStatus.WrongSource, item, "The item is not in the requested source container.");
        if (location.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.ItemAlreadyEquipped, item, "The item is already equipped.");
        if (!Index.ContainsContainer(source))
            return Reject(InventoryMutationStatus.MissingContainer, item, "The source container is missing.");
        if (!world.TryRead(item, _itemLookup, out InventoryItem original))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item component is missing.");
        if (original.Parent != source || original.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.InvariantViolation, item, "ECS and inventory index topology disagree.");
        if (!world.TryRead(item, _equipLookup, out InventoryEquipCapability capability) ||
            (capability.AllowedSlots & requested) == 0)
        {
            return Reject(InventoryMutationStatus.ItemPolicyViolation, item, "The item cannot use the requested equipment slot.");
        }
        if (original.Quantity != 1)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Only a single item may be equipped.");
        if (!Index.CanEquipItem(item, source, owner, slot))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "The inventory index cannot prove the equipment mutation.");

        InventoryItem equipped = original;
        equipped.Parent = owner;
        equipped.EquipSlot = slot;
        if (!world.TryWrite(item, _itemLookup, equipped))
            throw new InvalidOperationException("A validated equip rejected its ECS update.");
        if (!Index.TryEquipItem(item, source, owner, slot))
            throw new InvalidOperationException("A prevalidated inventory equip was rejected by the index.");
        return Accept(item, Entity.Null, "Equip accepted.");
    }

    public InventoryMutationResult Unequip(
        EcsWorld world,
        Entity item,
        Entity owner,
        InventoryEquipSlot slot,
        Entity destination)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!world.IsAlive(owner))
            return Reject(InventoryMutationStatus.MissingEquipOwner, item, "The equipment owner is missing.");
        if (!Index.ContainsContainer(destination))
            return Reject(InventoryMutationStatus.MissingContainer, item, "The destination container is missing.");
        if (slot.ToCapability() == InventoryEquipSlots.None)
            return Reject(InventoryMutationStatus.InvalidEquipmentSlot, item, "The equipment slot is invalid.");
        if (!Index.TryGetLocation(item, out InventoryLocation location) ||
            location.Parent != owner || location.EquipSlot != slot)
        {
            return Reject(InventoryMutationStatus.WrongSource, item,
                "The item is not equipped by the requested owner and slot.");
        }
        if (!Index.TryGetEquipped(owner, slot, out Entity indexed) || indexed != item)
        {
            return Reject(InventoryMutationStatus.InvariantViolation, item,
                "The inventory index equipment relationship is inconsistent.");
        }
        if (!world.TryRead(item, _itemLookup, out InventoryItem original))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item component is missing.");
        if (original.Parent != owner || original.EquipSlot != slot)
            return Reject(InventoryMutationStatus.InvariantViolation, item,
                "ECS and inventory index equipment topology disagree.");
        if (!Index.CanUnequipItem(item, owner, slot, destination))
        {
            return Reject(InventoryMutationStatus.CapacityExceeded, item,
                "The destination container cannot accept the equipped item.");
        }

        InventoryItem unequipped = original;
        unequipped.Parent = destination;
        unequipped.EquipSlot = InventoryEquipSlot.None;
        if (!world.TryWrite(item, _itemLookup, unequipped))
            throw new InvalidOperationException("A validated inventory unequip rejected its ECS update.");
        if (!Index.TryUnequipItem(item, owner, slot, destination))
            throw new InvalidOperationException("A prevalidated inventory unequip was rejected by the index.");
        return Accept(item, Entity.Null, "Unequip accepted.");
    }

    public InventoryMutationResult DestroyItem(EcsWorld world, Entity item)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item) || !world.TryRead(item, _itemLookup, out InventoryItem value))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!Index.CanRemoveItem(item, value))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "ECS and inventory index topology disagree.");
        if (!world.TryDestroy(item))
            throw new InvalidOperationException("A validated inventory item rejected ECS destruction.");
        if (!Index.TryRemoveItem(item, value))
            throw new InvalidOperationException("A destroyed inventory item was not removed from the index.");
        return Accept(item, Entity.Null, "Destroy accepted.");
    }

    public InventoryMutationResult DestroyContainer(EcsWorld world, Entity container)
    {
        EnsureWorld(world);
        if (!world.IsAlive(container) || !world.TryRead(container, _containerLookup, out _))
            return Reject(InventoryMutationStatus.MissingContainer, Entity.Null, "The container is stale or missing.");
        if (!Index.CanRemoveContainer(container))
            return Reject(InventoryMutationStatus.ContainerNotEmpty, Entity.Null, "A non-empty container cannot be destroyed.");
        if (!world.TryDestroy(container))
            throw new InvalidOperationException("A validated inventory container rejected ECS destruction.");
        if (!Index.TryRemoveContainer(container))
            throw new InvalidOperationException("A destroyed inventory container was not removed from the index.");
        return Accept(Entity.Null, Entity.Null, "Container destroy accepted.");
    }

    public InventoryItemView[] Snapshot(EcsWorld world, Entity container)
    {
        EnsureWorld(world);
        if (!Index.ContainsContainer(container))
            return Array.Empty<InventoryItemView>();
        var result = new InventoryItemView[Index.GetItems(container).Count];
        int index = 0;
        foreach (Entity item in Index.GetItems(container))
        {
            if (!TryProjectItem(world, item, out InventoryItemView view))
                throw new InvalidOperationException("The inventory index contains an unprojectable item.");
            result[index++] = view;
        }
        Array.Sort(result, static (left, right) => left.PersistentId.CompareTo(right.PersistentId));
        return result;
    }

    public bool TryProjectItem(EcsWorld world, Entity item, out InventoryItemView view)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item) || !world.TryRead(item, _itemLookup, out InventoryItem value) ||
            !world.TryGetPersistentId(item, out PersistentEntityId id) ||
            !TryGetDefinition(world, item, out InventoryItemDefinition definition))
        {
            view = default;
            return false;
        }
        view = new InventoryItemView(
            item,
            id,
            definition.Key,
            definition.DisplayName,
            value.EquipSlot,
            value.Quantity);
        return true;
    }

    public bool TryGetContainerOwner(EcsWorld world, Entity container, out Entity owner)
    {
        EnsureWorld(world);
        if (!world.TryRead(container, _ownerLookup, out InventoryContainerOwner value) ||
            !world.IsAlive(value.Owner))
        {
            owner = Entity.Null;
            return false;
        }
        owner = value.Owner;
        return true;
    }

    public bool TryGetContainerDefinition(
        EcsWorld world,
        Entity container,
        out InventoryContainerDefinition definition)
    {
        EnsureWorld(world);
        if (world.IsAlive(container) && world.Has(container, _containerLookup) &&
            world.TryGetPrefabProvenance(container, out PrefabKey key))
        {
            return _content.TryGetContainer(key, out definition);
        }
        definition = default;
        return false;
    }

    /// <summary>Resolves content policy plus the persistent instance item capacity.</summary>
    public bool TryGetEffectiveContainerRules(
        EcsWorld world,
        Entity container,
        out InventoryEffectiveRules rules)
    {
        EnsureWorld(world);
        if (!world.TryRead(container, _containerLookup, out InventoryContainer value) ||
            !TryGetContainerDefinition(world, container, out InventoryContainerDefinition definition))
        {
            rules = default;
            return false;
        }
        return TryResolveOperationalRules(definition, value.Capacity, out rules);
    }

    public bool TryReadItem(EcsWorld world, Entity item, out InventoryItem value)
    {
        EnsureWorld(world);
        return world.TryRead(item, _itemLookup, out value);
    }

    private bool TryCreateContainerCore(
        EcsWorld world,
        PrefabHandle prefab,
        Entity owner,
        bool hasOwner,
        int capacity,
        out Entity entity)
    {
        EnsureWorld(world);
        entity = Entity.Null;
        if (!prefab.IsValid || prefab.World != world.Id ||
            !_content.TryGetContainer(prefab, out InventoryContainerDefinition definition) ||
            definition.HasOwner != hasOwner ||
            !TryResolveOperationalRules(definition, capacity, out InventoryEffectiveRules rules) ||
            !Index.CanAddContainer(rules.ItemCapacity) ||
            (hasOwner && !world.IsAlive(owner)))
        {
            return false;
        }
        if (!world.TryInstantiate(prefab, out entity))
            return false;
        if (!world.Has(entity, _containerLookup) || world.Has(entity, _ownerLookup) != hasOwner)
            DestroyFailedCreationOrThrow(world, entity, "container content validation");
        if (!world.TryWrite(entity, _containerLookup, new InventoryContainer { Capacity = rules.ItemCapacity }))
            DestroyFailedCreationOrThrow(world, entity, "container initialization");
        if (hasOwner && !world.TryWrite(entity, _ownerLookup, new InventoryContainerOwner { Owner = owner }))
            DestroyFailedCreationOrThrow(world, entity, "container ownership initialization");
        if (!Index.TryAddContainer(entity, rules.ItemCapacity))
            throw new InvalidOperationException("A prevalidated inventory container was rejected by the index.");
        return true;
    }

    private bool TryResolveOperationalRules(
        in InventoryContainerDefinition definition,
        int capacity,
        out InventoryEffectiveRules rules) =>
        _content.InventoryRules.TryResolve(definition.RuleProfile, capacity, out rules) &&
        rules.IsOperational;

    private bool IsBoundDefinition(in InventoryItemDefinition definition) =>
        definition.Handle.IsValid && definition.Handle.World == _worldId &&
        _content.TryGetItem(definition.Handle, out InventoryItemDefinition bound) &&
        bound == definition;

    private bool TryGetDefinition(EcsWorld world, Entity item, out InventoryItemDefinition definition)
    {
        if (world.TryGetPrefabProvenance(item, out PrefabKey key) && _content.TryGetItem(key, out definition))
            return true;
        definition = default;
        return false;
    }

    private void ValidateInstantiatedItemOrThrow(
        EcsWorld world,
        Entity entity,
        in InventoryItemDefinition definition)
    {
        if (!world.Has(entity, _itemLookup))
            DestroyFailedCreationOrThrow(world, entity, "item content validation");
        int maximum = world.TryRead(entity, _stackLookup, out InventoryStackPolicy stack)
            ? stack.MaximumQuantity
            : 1;
        InventoryEquipSlots allowed = world.TryRead(entity, _equipLookup, out InventoryEquipCapability equip)
            ? equip.AllowedSlots
            : InventoryEquipSlots.None;
        if (maximum != definition.MaximumQuantity || allowed != definition.AllowedEquipSlots)
            DestroyFailedCreationOrThrow(world, entity, "item policy validation");
    }

    private static void DestroyFailedCreationOrThrow(EcsWorld world, Entity entity, string operation)
    {
        if (entity.IsValid && !world.TryDestroy(entity))
            throw new InvalidOperationException($"Inventory {operation} failed and its entity could not be reclaimed.");
        throw new InvalidOperationException($"Compiled inventory content rejected {operation}.");
    }

    private void EnsureWorld(EcsWorld world)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != _worldId || world.OwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Inventory state belongs to a different ECS generation or thread.");
    }

    private void EnsureOperational()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InventoryGenerationState));
    }

    private static InventoryMutationResult Accept(Entity item, Entity created, string message) =>
        new(InventoryMutationStatus.Accepted, item, created, message);

    private static InventoryMutationResult Reject(InventoryMutationStatus status, Entity item, string message) =>
        new(status, item, Entity.Null, message);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Index.Dispose();
    }
}
