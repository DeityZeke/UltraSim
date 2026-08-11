using UltraSim.ECS;
using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class EcsInventoryRuntimeRefResolver : IInventoryRuntimeRefResolver
{
    private readonly IEntityReadOnly _entities;

    public EcsInventoryRuntimeRefResolver(IEntityReadOnly entities)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public InventoryRuntimeRefResolution ResolveOwner(InventoryOwnerRef owner)
    {
        InventoryValidationResult validation = owner.Validate();
        if (!validation.IsValid)
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidRef, validation.Error);
        }

        if (owner.Kind != InventoryOwnerKind.Entity)
        {
            return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForOwner(owner));
        }

        if (!InventoryEcsEntityRefCodec.TryParse(owner.Id, out Entity entity))
        {
            return InventoryRuntimeRefResolution.Failure(
                InventoryRuntimeRefResolutionKind.InvalidEntityFormat,
                "Inventory entity owner ref must use the entity:{packed} format.");
        }

        if (!_entities.IsAlive(entity))
        {
            return InventoryRuntimeRefResolution.Failure(
                InventoryRuntimeRefResolutionKind.EntityNotAlive,
                "Inventory entity owner is not alive.");
        }

        return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForOwnerEntity(owner, entity));
    }

    public InventoryRuntimeRefResolution ResolveRequester(InventoryRequesterRef requester)
    {
        InventoryValidationResult validation = requester.Validate();
        if (!validation.IsValid)
        {
            return InventoryRuntimeRefResolution.Failure(InventoryRuntimeRefResolutionKind.InvalidRef, validation.Error);
        }

        if (requester.Kind != InventoryRequesterKind.EntityAi)
        {
            return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequester(requester));
        }

        if (!InventoryEcsEntityRefCodec.TryParse(requester.Id, out Entity entity))
        {
            return InventoryRuntimeRefResolution.Failure(
                InventoryRuntimeRefResolutionKind.InvalidEntityFormat,
                "Inventory entity requester ref must use the entity:{packed} format.");
        }

        if (!_entities.IsAlive(entity))
        {
            return InventoryRuntimeRefResolution.Failure(
                InventoryRuntimeRefResolutionKind.EntityNotAlive,
                "Inventory entity requester is not alive.");
        }

        return InventoryRuntimeRefResolution.Success(InventoryResolvedRuntimeRef.ForRequesterEntity(requester, entity));
    }
}
