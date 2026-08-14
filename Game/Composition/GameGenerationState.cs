using UltraSim.Game.Accounting;
using UltraSim.Game.Inventory;

namespace UltraSim.Game.Composition;

/// <summary>Capability for the concrete coherent game generation.</summary>
public interface IGameGenerationStateProvider :
    IAccountingGenerationStateProvider,
    IInventoryGenerationStateProvider
{
    GameContent Content { get; }
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
        GameContent content)
    {
        Accounting = accounting ?? throw new ArgumentNullException(nameof(accounting));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        if (inventory.WorldId != content.Prefabs.WorldId)
            throw new ArgumentException("Inventory and content belong to different ECS worlds.", nameof(content));
    }

    public AccountingStore Accounting { get; }
    public InventoryGenerationState Inventory { get; }
    public GameContent Content { get; }

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
