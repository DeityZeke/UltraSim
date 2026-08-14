using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

public enum InventoryMutationStatus : byte
{
    Accepted = 0,
    RuntimeWorldMismatch = 1,
    MissingItem = 2,
    MissingContainer = 3,
    MissingCharacter = 4,
    WrongSource = 5,
    InvalidQuantity = 6,
    CapacityExceeded = 7,
    InvalidEquipmentSlot = 8,
    SlotOccupied = 9,
    ItemAlreadyEquipped = 10,
    InvariantViolation = 11,
    ContainerNotEmpty = 12,
    InvalidItemKind = 13,
}

/// <summary>Outcome of one atomic game-owned inventory intent.</summary>
public readonly record struct InventoryMutationResult(
    InventoryMutationStatus Status,
    Entity Item,
    Entity CreatedItem,
    string Message)
{
    public bool Accepted => Status == InventoryMutationStatus.Accepted;
}

/// <summary>One projected inventory row before UI-specific encoding.</summary>
public readonly record struct InventoryItemView(
    Entity Entity,
    PersistentEntityId PersistentId,
    InventoryItemKind Kind,
    InventoryEquipSlot EquipSlot,
    int Quantity);

/// <summary>
/// Capability implemented by generation roots that own inventory derived state.
/// It allows Inventory to compose with Accounting and other game-owned roots
/// without teaching ServerRuntime any game-domain structure.
/// </summary>
public interface IInventoryGenerationStateProvider
{
    InventoryGenerationState Inventory { get; }
}

/// <summary>
/// The single generation-owned inventory state root. It contains only a
/// derived index and cold-bound ECS access plans; authoritative item/container
/// facts remain in ECS components.
/// </summary>
public sealed class InventoryGenerationState : IInventoryGenerationStateProvider, IDisposable
{
    private readonly WorldId _worldId;
    private readonly InventoryPrefabHandles _prefabs;
    private readonly ComponentLookup<InventoryCharacter> _characterLookup;
    private readonly ComponentLookup<InventoryContainer> _containerLookup;
    private readonly ComponentLookup<InventoryItem> _itemLookup;
    private bool _disposed;

    public InventoryGenerationState(
        EcsWorld world,
        InventoryPrefabHandles prefabs,
        InventoryLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worldId = world.Id;
        ValidatePrefabs(world, prefabs);
        _prefabs = prefabs;
        _characterLookup = world.PrepareLookup<InventoryCharacter>();
        _containerLookup = world.PrepareLookup<InventoryContainer>();
        _itemLookup = world.PrepareLookup<InventoryItem>();
        Limits = limits ?? InventoryLimits.Default;
        Limits.Validate();
        Index = new InventoryIndex(Limits);
    }

    public InventoryLimits Limits { get; }

    public InventoryIndex Index { get; }

    InventoryGenerationState IInventoryGenerationStateProvider.Inventory => this;

    /// <summary>Rebuilds derived state after ECS restore and before publication.</summary>
    public void Rebuild(EcsWorld world)
    {
        EnsureWorld(world);
        Index.Rebuild(world, _characterLookup, _containerLookup, _itemLookup);
    }

    public bool TryCreateCharacter(EcsWorld world, out Entity entity)
    {
        EnsureWorld(world);
        return world.TryInstantiate(_prefabs.Character, out entity);
    }

    public bool TryCreateContainer(EcsWorld world, Entity owner, int capacity, out Entity entity)
    {
        EnsureWorld(world);
        entity = Entity.Null;
        if (!world.IsAlive(owner) || !world.Has(owner, _characterLookup) ||
            !Index.CanAddContainer(capacity))
            return false;
        if (!world.TryInstantiate(_prefabs.Backpack, out entity))
            return false;
        if (!world.TryWrite(entity, _containerLookup, new InventoryContainer { Owner = owner, Capacity = capacity }))
        {
            DestroyFailedCreationOrThrow(world, entity, "container initialization");
        }
        if (!Index.TryAddContainer(entity, capacity))
            throw new InvalidOperationException("A prevalidated inventory container was rejected by the index.");
        return true;
    }

    public bool TryCreateItem(
        EcsWorld world,
        Entity parent,
        InventoryItemKind kind,
        int quantity,
        out Entity entity)
    {
        EnsureWorld(world);
        entity = Entity.Null;
        if (!world.IsAlive(parent) || !world.Has(parent, _containerLookup) ||
            !IsValidContainedItem(kind, quantity) || !Index.CanAddContainedItem(parent))
            return false;
        PrefabHandle prefab = kind == InventoryItemKind.Bread ? _prefabs.Bread : _prefabs.IronSword;
        if (!world.TryInstantiate(prefab, out entity))
            return false;
        var value = new InventoryItem
        {
            Parent = parent,
            Kind = kind,
            EquipSlot = InventoryEquipSlot.None,
            Quantity = quantity,
        };
        if (!world.TryWrite(entity, _itemLookup, value))
        {
            DestroyFailedCreationOrThrow(world, entity, "item initialization");
        }
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
        if (!Index.CanMoveItem(item, source, destination) ||
            Index.GetItems(destination).Count >= GetContainerCapacity(world, destination))
            return Reject(InventoryMutationStatus.CapacityExceeded, item, "The destination container is full.");

        InventoryItem moved = value;
        moved.Parent = destination;
        if (!world.TryWrite(item, _itemLookup, moved))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "The item could not be updated.");
        if (!Index.TryMoveItem(item, source, destination))
            throw new InvalidOperationException("A prevalidated inventory move was rejected by the index.");
        return Accept(item, Entity.Null, "Move accepted.");
    }

    public InventoryMutationResult Split(EcsWorld world, Entity item, Entity source, Entity destination, int quantity)
    {
        EnsureWorld(world);
        if (quantity <= 0)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Split quantity must be positive.");
        if (!world.IsAlive(item))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!Index.ContainsContainer(source) || !Index.ContainsContainer(destination))
            return Reject(InventoryMutationStatus.MissingContainer, item, "The source or destination container is missing.");
        if (!Index.TryGetLocation(item, out InventoryLocation location) || location.Parent != source)
            return Reject(InventoryMutationStatus.WrongSource, item, "The item is not in the requested source container.");
        if (location.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.ItemAlreadyEquipped, item, "An equipped item cannot be split.");
        if (!world.TryRead(item, _itemLookup, out InventoryItem original))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item component is missing.");
        if (original.Parent != source || original.EquipSlot != InventoryEquipSlot.None)
            return Reject(InventoryMutationStatus.InvariantViolation, item, "ECS and inventory index topology disagree.");
        if (original.Kind != InventoryItemKind.Bread)
            return Reject(InventoryMutationStatus.InvalidItemKind, item, "Only stackable bread can be split in S0.");
        if (quantity >= original.Quantity)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Split quantity must leave a positive source stack.");
        if (!Index.CanAddContainedItem(destination) ||
            Index.GetItems(destination).Count >= GetContainerCapacity(world, destination))
            return Reject(InventoryMutationStatus.CapacityExceeded, item, "The destination container is full.");
        if (!world.TryInstantiate(_prefabs.Bread, out Entity created))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "The inventory item capacity was exhausted.");

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
        Entity character,
        InventoryEquipSlot slot)
    {
        EnsureWorld(world);
        if (!world.IsAlive(item))
            return Reject(InventoryMutationStatus.MissingItem, item, "The item is stale or missing.");
        if (!world.IsAlive(character) || !world.Has(character, _characterLookup))
            return Reject(InventoryMutationStatus.MissingCharacter, item, "The target character is missing.");
        if (slot == InventoryEquipSlot.None || (byte)slot > (byte)InventoryEquipSlot.MainHand)
            return Reject(InventoryMutationStatus.InvalidEquipmentSlot, item, "The equipment slot is invalid.");
        if (Index.TryGetEquipped(character, slot, out _))
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
        if (original.Kind != InventoryItemKind.IronSword)
            return Reject(InventoryMutationStatus.InvalidItemKind, item, "The item cannot use the requested equipment slot.");
        if (original.Quantity != 1)
            return Reject(InventoryMutationStatus.InvalidQuantity, item, "Only a single item may be equipped.");
        if (!Index.CanEquipItem(item, source, character, slot))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "The inventory index cannot prove the equipment mutation.");

        InventoryItem equipped = original;
        equipped.Parent = character;
        equipped.EquipSlot = slot;
        if (!world.TryWrite(item, _itemLookup, equipped))
            return Reject(InventoryMutationStatus.InvariantViolation, item, "The item could not be equipped.");
        if (!Index.TryEquipItem(item, source, character, slot))
            throw new InvalidOperationException("A prevalidated inventory equip was rejected by the index.");
        return Accept(item, Entity.Null, "Equip accepted.");
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

    public IReadOnlyList<InventoryItemView> Snapshot(EcsWorld world, Entity container)
    {
        EnsureWorld(world);
        if (!Index.ContainsContainer(container))
            return Array.Empty<InventoryItemView>();
        var result = new List<InventoryItemView>(Index.GetItems(container).Count);
        foreach (Entity item in Index.GetItems(container))
        {
            if (!world.TryRead(item, _itemLookup, out InventoryItem value) || !world.IsAlive(item))
                throw new InvalidOperationException("The inventory index contains a stale item.");
            if (!world.TryGetPersistentId(item, out PersistentEntityId id))
                throw new InvalidOperationException("An inventory item has no durable identity.");
            result.Add(new InventoryItemView(item, id, value.Kind, value.EquipSlot, value.Quantity));
        }
        result.Sort(static (left, right) => left.PersistentId.CompareTo(right.PersistentId));
        return result;
    }

    public bool TryGetContainerOwner(EcsWorld world, Entity container, out Entity owner)
    {
        EnsureWorld(world);
        if (!world.TryRead(container, _containerLookup, out InventoryContainer value) ||
            !world.IsAlive(value.Owner) || !world.Has(value.Owner, _characterLookup))
        {
            owner = Entity.Null;
            return false;
        }
        owner = value.Owner;
        return true;
    }

    public bool TryReadItem(EcsWorld world, Entity item, out InventoryItem value)
    {
        EnsureWorld(world);
        return world.TryRead(item, _itemLookup, out value);
    }

    private int GetContainerCapacity(EcsWorld world, Entity container)
    {
        if (!world.TryRead(container, _containerLookup, out InventoryContainer value))
            return 0;
        return value.Capacity;
    }

    private static bool IsValidContainedItem(InventoryItemKind kind, int quantity) =>
        kind == InventoryItemKind.Bread ? quantity > 0 :
        kind == InventoryItemKind.IronSword && quantity == 1;

    private static void ValidatePrefabs(EcsWorld world, InventoryPrefabHandles prefabs)
    {
        if (!prefabs.Character.IsValid || !prefabs.Backpack.IsValid ||
            !prefabs.Bread.IsValid || !prefabs.IronSword.IsValid ||
            prefabs.Character.World != world.Id || prefabs.Backpack.World != world.Id ||
            prefabs.Bread.World != world.Id || prefabs.IronSword.World != world.Id)
        {
            throw new ArgumentException("Inventory prefab handles are invalid or belong to another world.", nameof(prefabs));
        }
    }

    private static void DestroyFailedCreationOrThrow(EcsWorld world, Entity entity, string operation)
    {
        if (entity.IsValid && !world.TryDestroy(entity))
            throw new InvalidOperationException($"Inventory {operation} failed and its created entity could not be reclaimed.");
        throw new InvalidOperationException($"A compiled inventory prefab rejected {operation}.");
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
