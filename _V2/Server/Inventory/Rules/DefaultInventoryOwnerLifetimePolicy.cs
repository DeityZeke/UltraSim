using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class DefaultInventoryOwnerLifetimePolicy : IInventoryOwnerLifetimePolicy
{
    public InventoryOwnerInvalidationDecision Evaluate(
        InventoryContainerSnapshot container,
        InventoryRuntimeRefResolution ownerResolution)
    {
        if (ownerResolution.IsResolved)
        {
            return InventoryOwnerInvalidationDecision.KeepOpen();
        }

        if (container.Owner.Kind != InventoryOwnerKind.Entity ||
            ownerResolution.Kind != InventoryRuntimeRefResolutionKind.EntityNotAlive)
        {
            return InventoryOwnerInvalidationDecision.CloseViewers(
                $"Inventory owner resolution failed: {ownerResolution.Kind}.");
        }

        return container.Lifecycle switch
        {
            InventoryContainerLifecycle.Persistent or
            InventoryContainerLifecycle.WorldPersistent =>
                InventoryOwnerInvalidationDecision.CloseViewers("Entity owner despawned; persistent container remains."),

            InventoryContainerLifecycle.Transactional =>
                InventoryOwnerInvalidationDecision.CloseViewersAndReleaseTransactions(
                    "Entity owner despawned during transaction."),

            InventoryContainerLifecycle.Runtime or
            InventoryContainerLifecycle.Ephemeral or
            InventoryContainerLifecycle.Destructive =>
                InventoryOwnerInvalidationDecision.CloseViewersAndDestroyContainer(
                    "Entity owner despawned; runtime container is invalid."),

            _ => InventoryOwnerInvalidationDecision.CloseViewers(
                $"Inventory container lifecycle is not supported: {container.Lifecycle}."),
        };
    }
}
