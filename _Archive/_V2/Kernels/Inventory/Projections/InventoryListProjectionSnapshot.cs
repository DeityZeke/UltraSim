namespace UltraSim.Inventory;

public sealed record InventoryListProjectionSnapshot(
    string ProjectionId,
    string ContainerId,
    long ContainerRevision,
    InventoryPlacementMode SourcePlacementMode,
    InventoryProjectionSortDescriptor Sort,
    IReadOnlyList<InventoryListProjectionRow> Rows,
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
            return InventoryValidationResult.Invalid("Inventory list projection container revision cannot be negative.");
        }

        if (!InventoryValidation.IsDefined(SourcePlacementMode))
        {
            return InventoryValidationResult.Invalid("Inventory list projection source placement mode is invalid.");
        }

        InventoryValidationResult sortResult = Sort.Validate();
        if (!sortResult.IsValid)
        {
            return sortResult;
        }

        if (!string.IsNullOrWhiteSpace(ViewId))
        {
            idResult = InventoryValidation.ValidateStableId(ViewId, nameof(ViewId));
            if (!idResult.IsValid)
            {
                return idResult;
            }
        }

        var displayIndexes = new HashSet<int>();
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Rows.Count; i++)
        {
            InventoryListProjectionRow row = Rows[i];
            InventoryValidationResult rowResult = row.Validate(SourcePlacementMode);
            if (!rowResult.IsValid)
            {
                return rowResult;
            }

            if (!displayIndexes.Add(row.DisplayIndex))
            {
                return InventoryValidationResult.Invalid($"Inventory list projection contains duplicate display index '{row.DisplayIndex}'.");
            }

            if (!itemIds.Add(row.Item.ItemInstanceId))
            {
                return InventoryValidationResult.Invalid($"Inventory list projection contains duplicate item id '{row.Item.ItemInstanceId}'.");
            }
        }

        return InventoryValidationResult.Valid;
    }

    public static InventoryListProjectionSnapshot FromContainer(
        string projectionId,
        InventoryContainerSnapshot container,
        long containerRevision,
        InventoryProjectionSortDescriptor sort,
        string? viewId = null)
    {
        var rows = new InventoryListProjectionRow[container.Items.Count];
        for (int i = 0; i < container.Items.Count; i++)
        {
            rows[i] = new InventoryListProjectionRow(i, container.Items[i]);
        }

        return new InventoryListProjectionSnapshot(
            projectionId,
            container.ContainerId,
            containerRevision,
            container.PlacementMode,
            sort,
            rows,
            viewId);
    }
}
