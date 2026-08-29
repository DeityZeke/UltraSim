using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Inventory;
using UltraSim.Game.Physics;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Composition;

/// <summary>Capability for the concrete coherent game generation.</summary>
public interface IGameGenerationStateProvider :
    IAccountingGenerationStateProvider,
    IInventoryGenerationStateProvider
{
    GameContent Content { get; }

    GameWorldState WorldState { get; }

    GameWorldActions WorldActions { get; }

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
    IServerGenerationStepParticipant,
    IDisposable
{
    private bool _disposed;

    public GameGenerationState(
        AccountingStore accounting,
        InventoryGenerationState inventory,
        GameContent content,
        GameWorldState worldState,
        ComponentLookup<GameSimulationState> simulationLookup,
        GameReplicationBindings? replication = null,
        GameWorldActions? worldActions = null)
    {
        Accounting = accounting ?? throw new ArgumentNullException(nameof(accounting));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
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
    public GameContent Content { get; }
    public GameWorldState WorldState { get; }
    public GameWorldActions WorldActions => _worldActions ??
        throw new InvalidOperationException("World actions are unavailable before generation initialization.");

    /// <summary>Concrete generation-owned authoritative P0.0 endpoint resolver.</summary>
    public UltraSimSimplePhysics SimplePhysics => WorldActions.SimplePhysics;

    /// <summary>Gets generation-bound replication capabilities when the network profile was selected.</summary>
    public GameReplicationBindings? Replication { get; }

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

    void IServerGenerationStepParticipant.InitializeGeneration(EcsWorld world)
    {
        WorldState.Initialize(world);
        WorldActions.ValidateRestoredDoorConsistency(world, WorldState);
    }

    void IServerGenerationStepParticipant.CommitAfterEcsStep(
        EcsWorld world,
        in SimulationStepDelta delta)
    {
        WorldActions.CommitLocomotionAfterEcsStep(world, WorldState, delta);
        WorldState.CommitAfterEcsStep(world);
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
