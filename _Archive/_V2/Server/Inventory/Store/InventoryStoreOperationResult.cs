using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed record InventoryStoreOperationResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<InventoryStoreEvent> Events,
    IReadOnlyList<InventoryStoreDeltaDispatch> Dispatches,
    InventoryMutationResult? MutationResult)
{
    public static InventoryStoreOperationResult Success(
        IReadOnlyList<InventoryStoreEvent>? events = null,
        IReadOnlyList<InventoryStoreDeltaDispatch>? dispatches = null,
        InventoryMutationResult? mutationResult = null)
    {
        return new InventoryStoreOperationResult(
            Succeeded: true,
            Error: null,
            events ?? [],
            dispatches ?? [],
            mutationResult);
    }

    public static InventoryStoreOperationResult Failure(string error)
    {
        return new InventoryStoreOperationResult(
            Succeeded: false,
            error,
            Events: [],
            Dispatches: [],
            MutationResult: null);
    }
}
