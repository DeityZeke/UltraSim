namespace UltraSim.Inventory;

public sealed record InventoryContainerViewerRegistrySnapshot(
    string ContainerId,
    long Revision,
    IReadOnlyList<InventoryContainerViewSnapshot> Viewers)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(ContainerId, nameof(ContainerId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (Revision < 0)
        {
            return InventoryValidationResult.Invalid("Container viewer registry revision cannot be negative.");
        }

        for (int i = 0; i < Viewers.Count; i++)
        {
            InventoryContainerViewSnapshot viewer = Viewers[i];
            InventoryValidationResult viewerResult = viewer.Validate();
            if (!viewerResult.IsValid)
            {
                return viewerResult;
            }

            if (!string.Equals(viewer.ContainerId, ContainerId, StringComparison.Ordinal))
            {
                return InventoryValidationResult.Invalid("Container viewer registry contains a viewer for a different container.");
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(Viewers[j].ViewId, viewer.ViewId, StringComparison.Ordinal))
                {
                    return InventoryValidationResult.Invalid($"Container viewer registry contains duplicate view id '{viewer.ViewId}'.");
                }
            }
        }

        return InventoryValidationResult.Valid;
    }

    public int CountRecipients(InventoryContainerDelta delta)
    {
        int count = 0;
        for (int i = 0; i < Viewers.Count; i++)
        {
            if (Viewers[i].CanReceive(delta))
            {
                count++;
            }
        }

        return count;
    }
}
