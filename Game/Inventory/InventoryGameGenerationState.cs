using UltraSim.Game.Accounting;

namespace UltraSim.Game.Inventory;

/// <summary>
/// Composition contract for a generation root that owns both Accounting and
/// Inventory. Persistence depends on this capability, not on one fixture's
/// concrete aggregate-root type.
/// </summary>
public interface IInventoryGameGenerationStateProvider :
    IAccountingGenerationStateProvider,
    IInventoryGenerationStateProvider
{
}

/// <summary>
/// Minimal S0 game-owned aggregate published atomically with one ECS world.
/// Runtime remains unaware of both Accounting and Inventory semantics.
/// </summary>
public sealed class InventoryGameGenerationState :
    IInventoryGameGenerationStateProvider,
    IDisposable
{
    private bool _disposed;

    public InventoryGameGenerationState(
        AccountingStore accounting,
        InventoryGenerationState inventory)
    {
        Accounting = accounting ?? throw new ArgumentNullException(nameof(accounting));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public AccountingStore Accounting { get; }

    public InventoryGenerationState Inventory { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Exception? failure = null;
        try
        {
            Inventory.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        try
        {
            Accounting.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        if (failure is not null)
            throw new InvalidOperationException("The inventory game generation could not release all owned state.", failure);
    }
}
