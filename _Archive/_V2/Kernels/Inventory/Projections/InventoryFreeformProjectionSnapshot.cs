namespace UltraSim.Inventory;

public sealed record InventoryFreeformProjectionSnapshot(
    string ProjectionId,
    string ContainerId,
    long ContainerRevision,
    InventoryPlacementMode SourcePlacementMode,
    IReadOnlyList<InventoryFreeformProjectionRow> Rows,
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
            return InventoryValidationResult.Invalid("Inventory freeform projection container revision cannot be negative.");
        }

        if (SourcePlacementMode != InventoryPlacementMode.Freeform)
        {
            return InventoryValidationResult.Invalid("Inventory freeform projection source placement mode must be Freeform.");
        }

        if (!string.IsNullOrWhiteSpace(ViewId))
        {
            idResult = InventoryValidation.ValidateStableId(ViewId, nameof(ViewId));
            if (!idResult.IsValid)
            {
                return idResult;
            }
        }

        var displayOrders = new HashSet<int>();
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Rows.Count; i++)
        {
            InventoryFreeformProjectionRow row = Rows[i];
            InventoryValidationResult rowResult = row.Validate();
            if (!rowResult.IsValid)
            {
                return rowResult;
            }

            if (!displayOrders.Add(row.DisplayOrder))
            {
                return InventoryValidationResult.Invalid($"Inventory freeform projection contains duplicate display order '{row.DisplayOrder}'.");
            }

            if (!itemIds.Add(row.Item.ItemInstanceId))
            {
                return InventoryValidationResult.Invalid($"Inventory freeform projection contains duplicate item id '{row.Item.ItemInstanceId}'.");
            }
        }

        return InventoryValidationResult.Valid;
    }

    public static InventoryFreeformProjectionSnapshot FromContainer(
        string projectionId,
        InventoryContainerSnapshot container,
        long containerRevision,
        string? viewId = null)
    {
        var rows = new InventoryFreeformProjectionRow[container.Items.Count];
        for (int i = 0; i < container.Items.Count; i++)
        {
            rows[i] = new InventoryFreeformProjectionRow(i, container.Items[i]);
        }

        return new InventoryFreeformProjectionSnapshot(
            projectionId,
            container.ContainerId,
            containerRevision,
            container.PlacementMode,
            rows,
            viewId);
    }
}
