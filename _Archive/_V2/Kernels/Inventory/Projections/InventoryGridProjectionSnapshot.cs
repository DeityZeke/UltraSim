namespace UltraSim.Inventory;

public sealed record InventoryGridProjectionSnapshot(
    string ProjectionId,
    string ContainerId,
    long ContainerRevision,
    InventoryPlacementMode SourcePlacementMode,
    int GridWidth,
    int GridHeight,
    IReadOnlyList<InventoryGridProjectionItem> Items,
    string? ViewId = null)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(ProjectionId, nameof(ProjectionId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        idResult = InventoryValidation.ValidateStableId(ContainerId, nameof(ContainerId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (ContainerRevision < 0)
        {
            return InventoryValidationResult.Invalid("Inventory grid projection container revision cannot be negative.");
        }

        if (SourcePlacementMode != InventoryPlacementMode.Grid)
        {
            return InventoryValidationResult.Invalid("Inventory grid projection source placement mode must be Grid.");
        }

        if (GridWidth <= 0 || GridHeight <= 0)
        {
            return InventoryValidationResult.Invalid("Inventory grid projection dimensions must be positive.");
        }

        if (!string.IsNullOrWhiteSpace(ViewId))
        {
            idResult = InventoryValidation.ValidateStableId(ViewId, nameof(ViewId));
            if (!idResult.IsValid)
            {
                return idResult;
            }
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var occupiedCells = new HashSet<long>();
        for (int i = 0; i < Items.Count; i++)
        {
            InventoryGridProjectionItem projectionItem = Items[i];
            InventoryValidationResult itemResult = projectionItem.Validate(GridWidth, GridHeight);
            if (!itemResult.IsValid)
            {
                return itemResult;
            }

            InventoryItemSnapshot item = projectionItem.Item;
            if (!itemIds.Add(item.ItemInstanceId))
            {
                return InventoryValidationResult.Invalid($"Inventory grid projection contains duplicate item id '{item.ItemInstanceId}'.");
            }

            InventoryPlacementDescriptor placement = item.Placement;
            for (int y = placement.Y; y < placement.Y + placement.Height; y++)
            {
                for (int x = placement.X; x < placement.X + placement.Width; x++)
                {
                    long cellKey = ((long)y << 32) | (uint)x;
                    if (!occupiedCells.Add(cellKey))
                    {
                        return InventoryValidationResult.Invalid("Inventory grid projection contains overlapping items.");
                    }
                }
            }
        }

        return InventoryValidationResult.Valid;
    }

    public static InventoryGridProjectionSnapshot FromContainer(
        string projectionId,
        InventoryContainerSnapshot container,
        long containerRevision,
        int gridWidth,
        int gridHeight,
        string? viewId = null)
    {
        var items = new InventoryGridProjectionItem[container.Items.Count];
        for (int i = 0; i < container.Items.Count; i++)
        {
            items[i] = new InventoryGridProjectionItem(container.Items[i]);
        }

        return new InventoryGridProjectionSnapshot(
            projectionId,
            container.ContainerId,
            containerRevision,
            container.PlacementMode,
            gridWidth,
            gridHeight,
            items,
            viewId);
    }
}
