namespace UltraSim.Server.Inventory;

public sealed record InventoryStoreOperationResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<InventoryStoreEvent> Events,
    IReadOnlyList<InventoryStoreDeltaDispatch> Dispatches)
{
    public static InventoryStoreOperationResult Success(
        IReadOnlyList<InventoryStoreEvent>? events = null,
        IReadOnlyList<InventoryStoreDeltaDispatch>? dispatches = null)
    {
        return new InventoryStoreOperationResult(
            Succeeded: true,
            Error: null,
            events ?? [],
            dispatches ?? []);
    }

    public static InventoryStoreOperationResult Failure(string error)
    {
        return new InventoryStoreOperationResult(
            Succeeded: false,
            error,
            Events: [],
            Dispatches: []);
    }
}
