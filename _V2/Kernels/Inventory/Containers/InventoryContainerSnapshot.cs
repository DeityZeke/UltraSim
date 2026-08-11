namespace UltraSim.Inventory;

public sealed record InventoryContainerSnapshot(
    string ContainerId,
    string ContainerTypeId,
    InventoryOwnerRef Owner,
    InventoryContainerLifecycle Lifecycle,
    InventoryPlacementMode PlacementMode,
    IReadOnlyList<InventoryPermissionDescriptor> Permissions,
    IReadOnlyList<InventoryItemSnapshot> Items)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult containerResult = InventoryValidation.ValidateStableId(ContainerId, nameof(ContainerId));
        if (!containerResult.IsValid)
        {
            return containerResult;
        }

        InventoryValidationResult typeResult = InventoryValidation.ValidateStableId(
            ContainerTypeId,
            nameof(ContainerTypeId),
            InventoryValidation.MaxTypeIdLength);
        if (!typeResult.IsValid)
        {
            return typeResult;
        }

        InventoryValidationResult ownerResult = Owner.Validate();
        if (!ownerResult.IsValid)
        {
            return ownerResult;
        }

        if (!InventoryValidation.IsDefined(Lifecycle))
        {
            return InventoryValidationResult.Invalid("Container lifecycle is invalid.");
        }

        if (!InventoryValidation.IsDefined(PlacementMode))
        {
            return InventoryValidationResult.Invalid("Container placement mode is invalid.");
        }

        if (Permissions.Count == 0)
        {
            return InventoryValidationResult.Invalid("Container requires at least one permission descriptor.");
        }

        for (int i = 0; i < Permissions.Count; i++)
        {
            InventoryValidationResult permissionResult = Permissions[i].Validate();
            if (!permissionResult.IsValid)
            {
                return permissionResult;
            }
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Items.Count; i++)
        {
            InventoryItemSnapshot item = Items[i];
            InventoryValidationResult itemResult = item.Validate(PlacementMode);
            if (!itemResult.IsValid)
            {
                return itemResult;
            }

            if (!itemIds.Add(item.ItemInstanceId))
            {
                return InventoryValidationResult.Invalid($"Container contains duplicate item id '{item.ItemInstanceId}'.");
            }
        }

        return InventoryValidationResult.Valid;
    }
}
