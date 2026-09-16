using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Inventory;
using UltraSim.Game.Physics;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Composition;

/// <summary>
/// Opt-in owner-thread timing of the concrete post-ECS Game maintenance phases. Values are the
/// most recent fixed step in raw <see cref="Stopwatch"/> ticks. This is diagnostic state only;
/// it does not participate in simulation, publication, persistence, or replication decisions.
/// </summary>
public readonly record struct GameGenerationStepTimingSample(
    bool Enabled,
    ulong Sequence,
    long LocomotionAndPhysicsTicks,
    long InventoryAndItemTicks,
    long WorldCommitTicks,
    long CompletePostEcsTicks);

/// <summary>Capability for the concrete coherent game generation.</summary>
public interface IGameGenerationStateProvider :
    IAccountingGenerationStateProvider,
    IInventoryGenerationStateProvider
{
    GameContent Content { get; }

    GameWorldState WorldState { get; }

    GameWorldActions WorldActions { get; }

    GameItemGenerationState Items { get; }

    bool TryRetireTransientParticipantInventory(
        EcsWorld world,
        Entity actor,
        Entity backpack);

    UltraSimSimplePhysics SimplePhysics { get; }

    bool TryReadSimulationState(
        EcsWorld world,
        Entity entity,
        out GameSimulationState state);
}

/// <summary>
/// Concrete game-owned aggregate published atomically with one ECS world.
/// Feature projects no longer own sibling game domains or publication.
/// </summary>
public sealed class GameGenerationState :
    IGameGenerationStateProvider,
    IInventoryWorldItemActions,
    IServerGenerationStepParticipant,
    IDisposable
{
    private bool _disposed;
    private bool _stepTimingEnabled;
    private ulong _stepTimingSequence;
    private GameGenerationStepTimingSample _lastStepTiming;

    public GameGenerationState(
        AccountingStore accounting,
        InventoryGenerationState inventory,
        GameItemGenerationState items,
        GameContent content,
        GameWorldState worldState,
        ComponentLookup<GameSimulationState> simulationLookup,
        GameReplicationBindings? replication = null,
        GameWorldActions? worldActions = null)
    {
        Accounting = accounting ?? throw new ArgumentNullException(nameof(accounting));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        WorldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
        if (!simulationLookup.IsValid)
            throw new ArgumentException("A prepared simulation lookup is required.", nameof(simulationLookup));
        if (inventory.WorldId != content.Prefabs.WorldId)
            throw new ArgumentException("Inventory and content belong to different ECS worlds.", nameof(content));
        if (worldState.WorldId != inventory.WorldId)
            throw new ArgumentException("World state and Inventory belong to different ECS worlds.", nameof(worldState));
        _simulationLookup = simulationLookup;
        _worldActions = worldActions;
        Replication = replication;
    }

    public AccountingStore Accounting { get; }
    public InventoryGenerationState Inventory { get; }
    public GameItemGenerationState Items { get; }
    public GameContent Content { get; }
    public GameWorldState WorldState { get; }
    public GameWorldActions WorldActions => _worldActions ??
        throw new InvalidOperationException("World actions are unavailable before generation initialization.");

    /// <summary>Concrete generation-owned authoritative P0.0 endpoint resolver.</summary>
    public UltraSimSimplePhysics SimplePhysics => WorldActions.SimplePhysics;

    /// <summary>Gets generation-bound replication capabilities when the network profile was selected.</summary>
    public GameReplicationBindings? Replication { get; }

    /// <summary>Selects exact per-step phase timestamps for cold benchmark/diagnostic use.</summary>
    public void ConfigureStepTiming(bool enabled)
    {
        _stepTimingEnabled = enabled;
        _stepTimingSequence = 0;
        _lastStepTiming = enabled
            ? new GameGenerationStepTimingSample(true, 0, 0, 0, 0, 0)
            : default;
    }

    /// <summary>Returns the last complete post-ECS timing sample without mutating Game state.</summary>
    public GameGenerationStepTimingSample GetLastStepTiming() => _lastStepTiming;

    private readonly ComponentLookup<GameSimulationState> _simulationLookup;
    private GameWorldActions? _worldActions;

    /// <summary>Reads the minimal scheduled simulation state through its prepared world route.</summary>
    public bool TryReadSimulationState(
        EcsWorld world,
        Entity entity,
        out GameSimulationState state)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != Inventory.WorldId)
            throw new ArgumentException("The ECS world is not owned by this game generation.", nameof(world));
        return world.TryRead(entity, _simulationLookup, out state);
    }

    public InventoryMutationResult DropOwnedItem(EcsWorld world, Entity item, Entity owner)
    {
        GameWorldActionStatus status = Items.TryDrop(world, WorldState, Inventory, owner, item);
        return status == GameWorldActionStatus.Accepted
            ? new InventoryMutationResult(
                InventoryMutationStatus.Accepted, item, Entity.Null, "Item dropped into the world.")
            : new InventoryMutationResult(
                status == GameWorldActionStatus.UnauthorizedContainer
                    ? InventoryMutationStatus.UnauthorizedContainer
                    : status == GameWorldActionStatus.MissingTarget
                        ? InventoryMutationStatus.MissingItem
                        : InventoryMutationStatus.InvariantViolation,
                item,
                Entity.Null,
                $"Game rejected world drop: {status}.");
    }

    /// <summary>
    /// Applies the first guest-lifetime policy before a transient participant retires. Persistent
    /// spawned weapons return to authoritative world space; ordinary session starter items retire
    /// with the temporary inventory. This does not make the connection the item owner.
    /// </summary>
    public bool TryRetireTransientParticipantInventory(
        EcsWorld world,
        Entity actor,
        Entity backpack)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(actor) || !Inventory.Index.ContainsContainer(backpack) ||
            !Inventory.TryGetContainerOwner(world, backpack, out Entity owner) || owner != actor)
            return false;

        Entity[] contained = Inventory.Index.GetItems(backpack).ToArray();
        Entity equipped = Inventory.Index.TryGetEquipped(
            actor, InventoryEquipSlot.MainHand, out Entity mainHand)
            ? mainHand
            : Entity.Null;
        for (int index = 0; index < contained.Length; index++)
        {
            Entity item = contained[index];
            if (Items.TryReadWeapon(world, item, out _))
            {
                if (Items.TryDrop(world, WorldState, Inventory, actor, item) !=
                    GameWorldActionStatus.Accepted)
                    return false;
            }
            else if (!Inventory.DestroyItem(world, item).Accepted)
            {
                return false;
            }
        }
        if (equipped.IsValid &&
            (Items.TryReadWeapon(world, equipped, out _)
                ? Items.TryDrop(world, WorldState, Inventory, actor, equipped) !=
                    GameWorldActionStatus.Accepted
                : !Inventory.DestroyItem(world, equipped).Accepted))
            return false;
        return Inventory.DestroyContainer(world, backpack).Accepted;
    }

    void IServerGenerationStepParticipant.InitializeGeneration(EcsWorld world)
    {
        WorldState.Initialize(world);
        Items.Rebuild(world);
        WorldActions.InitializePhysicalState(world, WorldState);
    }

    void IServerGenerationStepParticipant.CommitAfterEcsStep(
        EcsWorld world,
        in SimulationStepDelta delta)
    {
        if (!_stepTimingEnabled)
        {
            WorldActions.CommitLocomotionAfterEcsStep(world, WorldState, delta);
            Items.CommitAfterEcsStep(world, WorldState, Inventory);
            WorldState.CommitAfterEcsStep(world);
            return;
        }

        long completeStarted = Stopwatch.GetTimestamp();
        long started = completeStarted;
        WorldActions.CommitLocomotionAfterEcsStep(world, WorldState, delta);
        long locomotionTicks = Stopwatch.GetTimestamp() - started;
        started = Stopwatch.GetTimestamp();
        Items.CommitAfterEcsStep(world, WorldState, Inventory);
        long inventoryTicks = Stopwatch.GetTimestamp() - started;
        started = Stopwatch.GetTimestamp();
        WorldState.CommitAfterEcsStep(world);
        long worldTicks = Stopwatch.GetTimestamp() - started;
        _lastStepTiming = new GameGenerationStepTimingSample(
            true,
            checked(++_stepTimingSequence),
            locomotionTicks,
            inventoryTicks,
            worldTicks,
            Stopwatch.GetTimestamp() - completeStarted);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Exception? first = null;
        try { WorldState.Dispose(); }
        catch (Exception exception) { first = exception; }
        try { Inventory.Dispose(); }
        catch (Exception exception) { first ??= exception; }
        try { Accounting.Dispose(); }
        catch (Exception exception) { first ??= exception; }
        if (first is not null)
            throw new InvalidOperationException("The game generation could not release all owned state.", first);
    }
}
