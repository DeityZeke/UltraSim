using UltraSim.ECS;
using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public readonly record struct InventoryResolvedRuntimeRef(
    string StableId,
    InventoryOwnerKind? OwnerKind,
    InventoryRequesterKind? RequesterKind,
    Entity Entity,
    bool HasEntity)
{
    public static InventoryResolvedRuntimeRef ForOwner(InventoryOwnerRef owner)
    {
        return new InventoryResolvedRuntimeRef(owner.Id, owner.Kind, null, Entity.Null, HasEntity: false);
    }

    public static InventoryResolvedRuntimeRef ForRequester(InventoryRequesterRef requester)
    {
        return new InventoryResolvedRuntimeRef(requester.Id, null, requester.Kind, Entity.Null, HasEntity: false);
    }

    public static InventoryResolvedRuntimeRef ForOwnerEntity(InventoryOwnerRef owner, Entity entity)
    {
        return new InventoryResolvedRuntimeRef(owner.Id, owner.Kind, null, entity, HasEntity: true);
    }

    public static InventoryResolvedRuntimeRef ForRequesterEntity(InventoryRequesterRef requester, Entity entity)
    {
        return new InventoryResolvedRuntimeRef(requester.Id, null, requester.Kind, entity, HasEntity: true);
    }
}
