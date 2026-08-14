using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Inventory;

namespace UltraSim.Game.Composition;

/// <summary>Capability for the concrete coherent game generation.</summary>
public interface IGameGenerationStateProvider :
    IAccountingGenerationStateProvider,
    IInventoryGenerationStateProvider
{
    GameContent Content { get; }

    bool TryReadSimulationState(
        EcsWorld world,
        Entity entity,
        out GameSimulationState state);
}

/// <summary>
/// Concrete game-owned aggregate published atomically with one ECS world.
/// Feature projects no longer own sibling game domains or publication.
/// </summary>
public sealed class GameGenerationState : IGameGenerationStateProvider, IDisposable
{
    private bool _disposed;

    public GameGenerationState(
        AccountingStore accounting,
        InventoryGenerationState inventory,
        GameContent content,
        ComponentLookup<GameSimulationState> simulationLookup)
    {
        Accounting = accounting ?? throw new ArgumentNullException(nameof(accounting));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        if (!simulationLookup.IsValid)
            throw new ArgumentException("A prepared simulation lookup is required.", nameof(simulationLookup));
        if (inventory.WorldId != content.Prefabs.WorldId)
            throw new ArgumentException("Inventory and content belong to different ECS worlds.", nameof(content));
        _simulationLookup = simulationLookup;
    }

    public AccountingStore Accounting { get; }
    public InventoryGenerationState Inventory { get; }
    public GameContent Content { get; }

    private readonly ComponentLookup<GameSimulationState> _simulationLookup;

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Exception? first = null;
        try { Inventory.Dispose(); }
        catch (Exception exception) { first = exception; }
        try { Accounting.Dispose(); }
        catch (Exception exception) { first ??= exception; }
        if (first is not null)
            throw new InvalidOperationException("The game generation could not release all owned state.", first);
    }
}
