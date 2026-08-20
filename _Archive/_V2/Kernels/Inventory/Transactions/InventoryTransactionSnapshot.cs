namespace UltraSim.Inventory;

public sealed record InventoryTransactionSnapshot(
    string TransactionId,
    InventoryTransactionState State,
    IReadOnlyList<string> ParticipantContainerIds,
    IReadOnlyList<InventorySuspendedItemSnapshot> SuspendedItems)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(TransactionId, nameof(TransactionId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (!InventoryValidation.IsDefined(State))
        {
            return InventoryValidationResult.Invalid("Inventory transaction state is invalid.");
        }

        if (ParticipantContainerIds.Count == 0)
        {
            return InventoryValidationResult.Invalid("Inventory transaction requires at least one participant container.");
        }

        for (int i = 0; i < ParticipantContainerIds.Count; i++)
        {
            string participantId = ParticipantContainerIds[i];
            idResult = InventoryValidation.ValidateStableId(participantId, nameof(ParticipantContainerIds));
            if (!idResult.IsValid)
            {
                return idResult;
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(ParticipantContainerIds[j], participantId, StringComparison.Ordinal))
                {
                    return InventoryValidationResult.Invalid($"Inventory transaction contains duplicate participant container '{participantId}'.");
                }
            }
        }

        for (int i = 0; i < SuspendedItems.Count; i++)
        {
            InventorySuspendedItemSnapshot item = SuspendedItems[i];
            InventoryValidationResult itemResult = item.Validate();
            if (!itemResult.IsValid)
            {
                return itemResult;
            }

            if (!ContainsParticipant(item.SourceContainerId) ||
                (!string.IsNullOrWhiteSpace(item.DestinationContainerId) && !ContainsParticipant(item.DestinationContainerId)))
            {
                return InventoryValidationResult.Invalid("Suspended item source/destination must be transaction participant containers.");
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(SuspendedItems[j].ItemInstanceId, item.ItemInstanceId, StringComparison.Ordinal))
                {
                    return InventoryValidationResult.Invalid($"Inventory transaction contains duplicate suspended item '{item.ItemInstanceId}'.");
                }
            }
        }

        return InventoryValidationResult.Valid;
    }

    private bool ContainsParticipant(string containerId)
    {
        for (int i = 0; i < ParticipantContainerIds.Count; i++)
        {
            if (string.Equals(ParticipantContainerIds[i], containerId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
